using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;
using Shipit.Security;

namespace Shipit.Engine;

/// <summary>
/// Writes a run's log lines to the database: masks secrets first, splits multi-line chunks,
/// bounds line length and total lines, and persists in small batches so the live view stays close to real time.
/// </summary>
internal sealed class RunLogWriter : ILogSink
{
    private const string Ellipsis = "... [line truncated]";

    private readonly IDbContextFactory<ShipitDbContext> _dbFactory;
    private readonly long _runId;
    private readonly LogMasker _masker;
    private readonly EngineOptions _options;
    private readonly TimeProvider _time;
    private readonly Channel<RunLog> _channel = Channel.CreateUnbounded<RunLog>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly Task _pump;
    private int _accepted;
    private int _truncated;

    public RunLogWriter(
        IDbContextFactory<ShipitDbContext> dbFactory,
        long runId,
        LogMasker masker,
        EngineOptions options,
        TimeProvider time)
    {
        _dbFactory = dbFactory;
        _runId = runId;
        _masker = masker;
        _options = options;
        _time = time;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>Step the following lines belong to. Set by the runner before each step.</summary>
    public StepName CurrentStep { get; set; } = StepName.Checkout;

    public ValueTask WriteAsync(string line, LogStream stream, CancellationToken cancellationToken = default)
    {
        foreach (var part in (line ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            Enqueue(part, stream);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Flushes everything still buffered and stops the background writer.</summary>
    public async Task CompleteAsync()
    {
        _channel.Writer.TryComplete();
        await _pump.ConfigureAwait(false);
    }

    private void Enqueue(string rawLine, LogStream stream)
    {
        var count = Interlocked.Increment(ref _accepted);
        if (count > _options.MaxLogLines)
        {
            if (Interlocked.Exchange(ref _truncated, 1) == 0)
            {
                Push($"[log truncated: limit of {_options.MaxLogLines} lines reached]", LogStream.System);
            }

            return;
        }

        var masked = _masker.Mask(rawLine);
        if (masked.Length > _options.MaxLineLength)
        {
            masked = string.Concat(masked.AsSpan(0, _options.MaxLineLength), Ellipsis);
        }

        Push(masked, stream);
    }

    private void Push(string text, LogStream stream) =>
        _channel.Writer.TryWrite(new RunLog
        {
            RunId = _runId,
            Step = CurrentStep,
            Stream = stream,
            Text = text,
            At = _time.GetUtcNow().UtcDateTime,
        });

    private async Task PumpAsync()
    {
        var reader = _channel.Reader;
        var batch = new List<RunLog>(_options.LogBatchSize);
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            // Let a few more lines accumulate so we write in batches, but keep latency small.
            await Task.Delay(_options.LogFlushInterval, _time).ConfigureAwait(false);
            while (reader.TryRead(out var entry))
            {
                batch.Add(entry);
                if (batch.Count >= _options.LogBatchSize)
                {
                    await FlushAsync(batch).ConfigureAwait(false);
                }
            }

            await FlushAsync(batch).ConfigureAwait(false);
        }

        await FlushAsync(batch).ConfigureAwait(false);
    }

    private async Task FlushAsync(List<RunLog> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        db.RunLogs.AddRange(batch);
        await db.SaveChangesAsync().ConfigureAwait(false);
        batch.Clear();
    }
}
