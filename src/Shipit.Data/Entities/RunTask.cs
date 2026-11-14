namespace Shipit.Data.Entities;

/// <summary>Outcome of one <see cref="JobTask"/> within a <see cref="Run"/> (snapshot of label/position at run time).</summary>
public class RunTask
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public Run? Run { get; set; }
    public int Position { get; set; }
    public string Label { get; set; } = string.Empty;
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public int? ExitCode { get; set; }
    public string? K8sJobName { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
