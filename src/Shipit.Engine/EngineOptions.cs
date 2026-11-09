namespace Shipit.Engine;

/// <summary>Tunable limits for the pipeline engine. Defaults suit production; tests shrink them.</summary>
public sealed class EngineOptions
{
    /// <summary>How many runs (of different pipelines) may execute at the same time.</summary>
    public int MaxConcurrentRuns { get; set; } = 2;

    public TimeSpan CloneTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan BuildTimeout { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan PushTimeout { get; set; } = TimeSpan.FromMinutes(20);

    public TimeSpan RolloutTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Upper bound for the cleanup commands (docker logout, rm -rf) that always run after a run.</summary>
    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Stored log lines per run; further lines are dropped after one truncation marker.</summary>
    public int MaxLogLines { get; set; } = 20_000;

    /// <summary>Longer lines are cut and suffixed with an ellipsis marker.</summary>
    public int MaxLineLength { get; set; } = 4_000;

    /// <summary>Log lines are written to the database in batches at most this far apart (live view latency).</summary>
    public TimeSpan LogFlushInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public int LogBatchSize { get; set; } = 100;
}
