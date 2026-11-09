namespace Shipit.Engine;

/// <summary>The pipeline already has a queued or running run (only one active run per pipeline is allowed).</summary>
public sealed class RunConflictException(long pipelineId)
    : Exception($"Pipeline {pipelineId} already has an active run.")
{
    public long PipelineId { get; } = pipelineId;
}

public sealed class PipelineNotFoundException(long pipelineId)
    : Exception($"Pipeline {pipelineId} was not found.")
{
    public long PipelineId { get; } = pipelineId;
}
