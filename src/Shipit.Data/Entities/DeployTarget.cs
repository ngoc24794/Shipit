namespace Shipit.Data.Entities;

public class DeployTarget
{
    public long Id { get; set; }
    public string Namespace { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
