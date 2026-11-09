namespace Shipit.Data.Entities;

public class RunStep
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public Run? Run { get; set; }
    public StepName Name { get; set; }
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
