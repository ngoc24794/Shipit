using Shipit.Data.Entities;

namespace Shipit.Engine;

/// <summary>Result of attempting to cancel a run.</summary>
public enum CancelResult
{
    Accepted,
    NotFound,
    AlreadyFinished,
}

/// <summary>Entry point used by the API to start and cancel pipeline and job runs.</summary>
public interface IRunCoordinator
{
    /// <summary>
    /// Creates a queued run (with its four pending or pre-skipped steps) and schedules it.
    /// </summary>
    /// <exception cref="PipelineNotFoundException">No such pipeline.</exception>
    /// <exception cref="RunConflictException">The pipeline already has an active run.</exception>
    Task<Run> TriggerAsync(long pipelineId, CancellationToken cancellationToken = default, IReadOnlySet<StepName>? skipSteps = null);

    /// <summary>
    /// Creates a queued run for a Job (with its pending or pre-skipped steps and tasks) and schedules it.
    /// </summary>
    /// <exception cref="JobNotFoundException">No such job.</exception>
    /// <exception cref="JobRunConflictException">The job already has an active run.</exception>
    Task<Run> TriggerJobAsync(long jobId, CancellationToken cancellationToken = default, IReadOnlySet<StepName>? skipSteps = null);

    /// <summary>
    /// Cancels a queued or currently executing run.
    /// </summary>
    Task<(CancelResult Status, Run? Run)> CancelAsync(long runId, CancellationToken cancellationToken = default);
}
