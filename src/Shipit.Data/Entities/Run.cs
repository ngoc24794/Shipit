namespace Shipit.Data.Entities;

public class Run
{
    public long Id { get; set; }

    /// <summary>Exactly one of <see cref="PipelineId"/> and <see cref="JobId"/> is set.</summary>
    public long? PipelineId { get; set; }
    public Pipeline? Pipeline { get; set; }
    public long? JobId { get; set; }
    public Job? Job { get; set; }
    public RunStatus Status { get; set; }
    public string? Tag { get; set; }
    public string? Image { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public ICollection<RunStep> Steps { get; set; } = new List<RunStep>();
    public ICollection<RunTask> Tasks { get; set; } = new List<RunTask>();
    public ICollection<RunLog> Logs { get; set; } = new List<RunLog>();
}
