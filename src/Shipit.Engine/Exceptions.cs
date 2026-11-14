namespace Shipit.Engine;

/// <summary>The pipeline or job already has a queued or running run (only one active run per workload is allowed).</summary>
public class RunConflictException : Exception
{
    public long? PipelineId { get; }
    public long? JobId { get; }

    public RunConflictException(long pipelineId)
        : base($"Pipeline {pipelineId} already has an active run.")
    {
        PipelineId = pipelineId;
    }

    public RunConflictException(long id, bool isJob)
        : base(isJob ? $"Job {id} already has an active run." : $"Pipeline {id} already has an active run.")
    {
        if (isJob)
        {
            JobId = id;
        }
        else
        {
            PipelineId = id;
        }
    }

    public static RunConflictException ForJob(long jobId) => new JobRunConflictException(jobId);
}

public sealed class JobRunConflictException(long jobId)
    : RunConflictException(jobId, isJob: true)
{
}

public sealed class PipelineNotFoundException(long pipelineId)
    : Exception($"Pipeline {pipelineId} was not found.")
{
    public long PipelineId { get; } = pipelineId;
}

public sealed class JobNotFoundException(long jobId)
    : Exception($"Job {jobId} was not found.")
{
    public long JobId { get; } = jobId;
}
