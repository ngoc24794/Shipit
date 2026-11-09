namespace Shipit.Data.Entities;

public class Credential
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CredentialKind Kind { get; set; }
    public string? Username { get; set; }
    public string ProtectedValue { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Slave> Slaves { get; set; } = new List<Slave>();
    public ICollection<Pipeline> GitPipelines { get; set; } = new List<Pipeline>();
    public ICollection<Pipeline> RegistryPipelines { get; set; } = new List<Pipeline>();
}
