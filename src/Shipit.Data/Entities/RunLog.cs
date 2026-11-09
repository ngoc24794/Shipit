using Shipit.Core;

namespace Shipit.Data.Entities;

public class RunLog
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public Run? Run { get; set; }
    public StepName Step { get; set; }
    public LogStream Stream { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime At { get; set; } = DateTime.UtcNow;
}
