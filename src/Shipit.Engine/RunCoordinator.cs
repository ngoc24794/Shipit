using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Engine;

/// <summary>
/// Owns the run queue. <see cref="TriggerAsync"/> and <see cref="TriggerJobAsync"/> create queued runs (one active run per pipeline / job,
/// also enforced by database indexes); the hosted loop executes them with bounded concurrency. On start-up it repairs runs
/// left behind by a restart: running ones become <c>failed (interrupted)</c>, orphan Kubernetes jobs are cleaned up,
/// and queued ones are scheduled again.
/// </summary>
public sealed class RunCoordinator(
    IDbContextFactory<ShipitDbContext> dbFactory,
    PipelineRunner runner,
    EngineOptions options,
    TimeProvider time,
    ILogger<RunCoordinator> logger,
    IK8sJobRunner? jobRunner = null) : BackgroundService, IRunCoordinator
{
    private static readonly StepName[] PipelineStepOrder = SkipStepsValidator.PipelineStepOrder;
    private static readonly StepName[] JobStepOrder = SkipStepsValidator.JobStepOrder;

    private readonly Channel<long> _queue = Channel.CreateUnbounded<long>();
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _inFlight = new();

    public async Task<Run> TriggerAsync(long pipelineId, CancellationToken cancellationToken = default, IReadOnlySet<StepName>? skipSteps = null)
    {
        SkipStepsValidator.ValidateOrThrow(skipSteps, isJob: false);
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
            Steps = PipelineStepOrder.Select(n => new RunStep
            {
                Name = n,
                Status = skipSteps?.Contains(n) == true ? StepStatus.Skipped : StepStatus.Pending,
            }).ToList(),
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

    public async Task<Run> TriggerJobAsync(long jobId, CancellationToken cancellationToken = default, IReadOnlySet<StepName>? skipSteps = null)
    {
        SkipStepsValidator.ValidateOrThrow(skipSteps, isJob: true);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (!await db.Jobs.AnyAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false))
        {
            throw new JobNotFoundException(jobId);
        }

        if (await HasActiveJobRunAsync(db, jobId, cancellationToken).ConfigureAwait(false))
        {
            throw new JobRunConflictException(jobId);
        }

        var tasks = await db.JobTasks
            .Where(t => t.JobId == jobId)
            .OrderBy(t => t.Position)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var skipRunJob = skipSteps?.Contains(StepName.RunJob) == true;
        var run = new Run
        {
            JobId = jobId,
            Status = RunStatus.Queued,
            CreatedAt = time.GetUtcNow().UtcDateTime,
            Steps = JobStepOrder.Select(n => new RunStep
            {
                Name = n,
                Status = skipSteps?.Contains(n) == true ? StepStatus.Skipped : StepStatus.Pending,
            }).ToList(),
            Tasks = tasks.Select(t => new RunTask
            {
                Position = t.Position,
                Label = t.Label,
                Status = skipRunJob ? StepStatus.Skipped : StepStatus.Pending,
            }).ToList(),
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
            if (await HasActiveJobRunAsync(check, jobId, cancellationToken).ConfigureAwait(false))
            {
                throw new JobRunConflictException(jobId);
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

        var stale = await db.Runs
            .Include(r => r.Steps)
            .Include(r => r.Tasks)
            .Include(r => r.Job)
            .Where(r => r.Status == RunStatus.Running)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var run in stale)
        {
            PipelineRunner.MarkInterrupted(run, now);
            db.RunLogs.Add(new RunLog
            {
                RunId = run.Id,
                Step = run.Steps.FirstOrDefault(s => s.Status == StepStatus.Failed)?.Name ?? (run.JobId != null ? StepName.RunJob : StepName.Checkout),
                Stream = Shipit.Core.LogStream.System,
                Text = "Run interrupted by a restart of Shipit.",
                At = now,
            });
            logger.LogWarning("Run {RunId} was running when Shipit stopped; marked as interrupted.", run.Id);

            if (run.JobId != null && jobRunner != null)
            {
                var jobNs = run.Job?.Namespace;
                if (string.IsNullOrEmpty(jobNs))
                {
                    jobNs = await db.Jobs.Where(j => j.Id == run.JobId.Value).Select(j => j.Namespace).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(jobNs))
                {
                    try
                    {
                        await jobRunner.DeleteByRunAsync(jobNs, run.Id, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to delete orphan Kubernetes jobs for run {RunId} in namespace {Namespace}.", run.Id, jobNs);
                    }
                }
            }
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
        using var userCts = new CancellationTokenSource();
        _inFlight[runId] = userCts;
        try
        {
            await runner.ExecuteAsync(runId, stoppingToken, userCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} crashed the runner.", runId);
        }
        finally
        {
            _inFlight.TryRemove(runId, out _);
            gate.Release();
        }
    }

    public async Task<(CancelResult Status, Run? Run)> CancelAsync(long runId, CancellationToken cancellationToken = default)
    {
        // 1. If currently executing, signal user cancellation
        if (_inFlight.TryGetValue(runId, out var userCts))
        {
            userCts.Cancel();
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var running = await db.Runs.Include(r => r.Steps).Include(r => r.Tasks).FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);
            if (running is null)
            {
                return (CancelResult.NotFound, null);
            }

            if (running.Status is RunStatus.Success or RunStatus.Failed or RunStatus.Cancelled)
            {
                return (CancelResult.AlreadyFinished, running);
            }

            return (CancelResult.Accepted, running);
        }

        // 2. Not in flight: check database
        await using var dbAccess = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var run = await dbAccess.Runs.Include(r => r.Steps).Include(r => r.Tasks).FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return (CancelResult.NotFound, null);
        }

        if (run.Status is RunStatus.Success or RunStatus.Failed or RunStatus.Cancelled)
        {
            return (CancelResult.AlreadyFinished, run);
        }

        if (run.Status == RunStatus.Queued)
        {
            var now = time.GetUtcNow().UtcDateTime;
            run.Status = RunStatus.Cancelled;
            run.FailureReason = "cancelled by user";
            run.FinishedAt = now;
            foreach (var s in run.Steps)
            {
                if (s.Status == StepStatus.Pending)
                {
                    s.Status = StepStatus.Skipped;
                }
            }
            foreach (var t in run.Tasks)
            {
                if (t.Status == StepStatus.Pending)
                {
                    t.Status = StepStatus.Skipped;
                }
            }

            dbAccess.RunLogs.Add(new RunLog
            {
                RunId = run.Id,
                Step = run.JobId != null ? StepName.RunJob : StepName.Checkout,
                Stream = Shipit.Core.LogStream.System,
                Text = "Run cancelled by user.",
                At = now,
            });

            await dbAccess.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (CancelResult.Accepted, run);
        }

        if (run.Status == RunStatus.Running)
        {
            if (_inFlight.TryGetValue(runId, out var cts))
            {
                cts.Cancel();
            }

            return (CancelResult.Accepted, run);
        }

        return (CancelResult.Accepted, run);
    }

    private static Task<bool> HasActiveRunAsync(ShipitDbContext db, long pipelineId, CancellationToken ct) =>
        db.Runs.AnyAsync(
            r => r.PipelineId == pipelineId && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running),
            ct);

    private static Task<bool> HasActiveJobRunAsync(ShipitDbContext db, long jobId, CancellationToken ct) =>
        db.Runs.AnyAsync(
            r => r.JobId == jobId && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running),
            ct);
}
