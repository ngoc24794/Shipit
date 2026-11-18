namespace Shipit.Data.Entities;

/// <summary>One Kubernetes Job in a <see cref="Job"/>. Tasks run in <see cref="Position"/> order, stopping at the first failure.</summary>
public class JobTask
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public Job? Job { get; set; }
    public int Position { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>JSON array of container args.</summary>
    public string ArgsJson { get; set; } = "[]";

    /// <summary>JSON array of ConfigMap names for envFrom (names only).</summary>
    public string EnvFromConfigMapsJson { get; set; } = "[]";

    /// <summary>JSON array of Secret names for envFrom (names only; Shipit never reads the values).</summary>
    public string EnvFromSecretsJson { get; set; } = "[]";

    /// <summary>JSON array of {name,value} or {name,secretName,secretKey}.</summary>
    public string EnvJson { get; set; } = "[]";

    public int TimeoutSeconds { get; set; } = 1800;
    public int BackoffLimit { get; set; }
    public int TtlSeconds { get; set; } = 3600;
}
