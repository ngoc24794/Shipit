using Shipit.Data.Entities;

namespace Shipit.Engine;

/// <summary>Result of attempting to cancel a run.</summary>
public enum CancelResult
{
    Accepted,
    NotFound,
    AlreadyFinished,
}

/// <summary>Entry point used by the API to start and cancel pipeline runs.</summary>
public interface IRunCoordinator
{
    /// <summary>
    /// Creates a queued run (with its four pending steps) and schedules it.
    /// </summary>
    /// <exception cref="PipelineNotFoundException">No such pipeline.</exception>
    /// <exception cref="RunConflictException">The pipeline already has an active run.</exception>
    Task<Run> TriggerAsync(long pipelineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a queued or currently executing run.
    /// </summary>
    Task<(CancelResult Status, Run? Run)> CancelAsync(long runId, CancellationToken cancellationToken = default);
}
