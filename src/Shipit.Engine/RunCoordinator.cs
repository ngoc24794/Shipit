using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Engine;

/// <summary>
/// Owns the run queue. <see cref="TriggerAsync"/> creates queued runs (one active run per pipeline, also enforced
/// by a database index); the hosted loop executes them with bounded concurrency. On start-up it repairs runs
/// left behind by a restart: running ones become <c>failed (interrupted)</c>, queued ones are scheduled again.
/// </summary>
public sealed class RunCoordinator(
    IDbContextFactory<ShipitDbContext> dbFactory,
    PipelineRunner runner,
    EngineOptions options,
    TimeProvider time,
    ILogger<RunCoordinator> logger) : BackgroundService, IRunCoordinator
{
    private static readonly StepName[] StepOrder = [StepName.Checkout, StepName.Build, StepName.Push, StepName.Deploy];

    private readonly Channel<long> _queue = Channel.CreateUnbounded<long>();

    public async Task<Run> TriggerAsync(long pipelineId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (!await db.Pipelines.AnyAsync(p => p.Id == pipelineId, cancellationToken).ConfigureAwait(false))
        {
            throw new PipelineNotFoundException(pipelineId);
        }

        if (await HasActiveRunAsync(db, pipelineId, cancellationToken).ConfigureAwait(false))
        {
            throw new RunConflictException(pipelineId);
        }

        var run = new Run
        {
            PipelineId = pipelineId,
            Status = RunStatus.Queued,
            CreatedAt = time.GetUtcNow().UtcDateTime,
            Steps = StepOrder.Select(n => new RunStep { Name = n, Status = StepStatus.Pending }).ToList(),
        };
        db.Runs.Add(run);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Two triggers raced: the unique index on active runs rejected ours.
            await using var check = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            if (await HasActiveRunAsync(check, pipelineId, cancellationToken).ConfigureAwait(false))
            {
                throw new RunConflictException(pipelineId);
            }

            throw;
        }

        await _queue.Writer.WriteAsync(run.Id, cancellationToken).ConfigureAwait(false);
        return run;
    }

    /// <summary>Repairs runs left over from a previous process. Called once when the service starts.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var stale = await db.Runs.Include(r => r.Steps)
            .Where(r => r.Status == RunStatus.Running)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var run in stale)
        {
            PipelineRunner.MarkInterrupted(run, now);
            db.RunLogs.Add(new RunLog
            {
                RunId = run.Id,
                Step = run.Steps.FirstOrDefault(s => s.Status == StepStatus.Failed)?.Name ?? StepName.Checkout,
                Stream = Shipit.Core.LogStream.System,
                Text = "Run interrupted by a restart of Shipit.",
                At = now,
            });
            logger.LogWarning("Run {RunId} was running when Shipit stopped; marked as interrupted.", run.Id);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var queued = await db.Runs.Where(r => r.Status == RunStatus.Queued)
            .OrderBy(r => r.Id).Select(r => r.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in queued)
        {
            await _queue.Writer.WriteAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recovering interrupted runs failed.");
        }

        using var gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentRuns));
        var running = new List<Task>();
        try
        {
            await foreach (var runId in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await gate.WaitAsync(stoppingToken).ConfigureAwait(false);
                running.Add(RunOneAsync(runId, gate, stoppingToken));
                running.RemoveAll(t => t.IsCompleted);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: in-flight runs observe the same token and mark themselves interrupted.
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    private async Task RunOneAsync(long runId, SemaphoreSlim gate, CancellationToken stoppingToken)
    {
        try
        {
            await runner.ExecuteAsync(runId, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} crashed the runner.", runId);
        }
        finally
        {
            gate.Release();
        }
    }

    private static Task<bool> HasActiveRunAsync(ShipitDbContext db, long pipelineId, CancellationToken ct) =>
        db.Runs.AnyAsync(
            r => r.PipelineId == pipelineId && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running),
            ct);
}
