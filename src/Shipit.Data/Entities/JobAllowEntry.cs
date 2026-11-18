namespace Shipit.Data.Entities;

/// <summary>
/// What Jobs may touch. A <see cref="JobAllowKind.Namespace"/> entry allows running in that namespace; the other kinds
/// allow referencing a ConfigMap / Secret / pull Secret by name inside that namespace. Names only, never values.
/// </summary>
public class JobAllowEntry
{
    public long Id { get; set; }
    public string Namespace { get; set; } = string.Empty;
    public JobAllowKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
