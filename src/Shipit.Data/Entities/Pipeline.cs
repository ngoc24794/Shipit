namespace Shipit.Data.Entities;

public class Pipeline
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GitUrl { get; set; } = string.Empty;
    public string GitBranch { get; set; } = "main";
    public long GitCredentialId { get; set; }
    public Credential? GitCredential { get; set; }
    public long SlaveId { get; set; }
    public Slave? Slave { get; set; }
    public string DockerfilePath { get; set; } = "Dockerfile";
    public string? BuildContext { get; set; }
    public string ImageName { get; set; } = string.Empty;
    public string RegistryHost { get; set; } = string.Empty;
    public long? RegistryCredentialId { get; set; }
    public Credential? RegistryCredential { get; set; }
    public string DeployNamespace { get; set; } = string.Empty;
    public string DeployDeployment { get; set; } = string.Empty;
    public string DeployContainer { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Run> Runs { get; set; } = new List<Run>();
}
