using Shipit.Data.Entities;

namespace Shipit.Engine;

/// <summary>Entry point used by the API to start pipeline runs.</summary>
public interface IRunCoordinator
{
    /// <summary>
    /// Creates a queued run (with its four pending steps) and schedules it.
    /// </summary>
    /// <exception cref="PipelineNotFoundException">No such pipeline.</exception>
    /// <exception cref="RunConflictException">The pipeline already has an active run.</exception>
    Task<Run> TriggerAsync(long pipelineId, CancellationToken cancellationToken = default);
}
