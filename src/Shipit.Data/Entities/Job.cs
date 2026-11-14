namespace Shipit.Data.Entities;

/// <summary>
/// A run-to-completion workload: built from git on a slave like a <see cref="Pipeline"/>, but instead of
/// updating a Deployment it runs an ordered list of <see cref="JobTask"/>s as Kubernetes Jobs.
/// </summary>
public class Job
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
    public string? DeployRegistryHost { get; set; }
    public long? RegistryCredentialId { get; set; }
    public Credential? RegistryCredential { get; set; }
    public string Namespace { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "app";
    public string? ServiceAccount { get; set; }

    /// <summary>JSON array of Secret names used as imagePullSecrets (names only).</summary>
    public string ImagePullSecretsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<JobTask> Tasks { get; set; } = new List<JobTask>();
    public ICollection<Run> Runs { get; set; } = new List<Run>();
}
