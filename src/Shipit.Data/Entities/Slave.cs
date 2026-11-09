namespace Shipit.Data.Entities;

public class Slave
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string User { get; set; } = string.Empty;
    public long SshCredentialId { get; set; }
    public Credential? SshCredential { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Pipeline> Pipelines { get; set; } = new List<Pipeline>();
}
