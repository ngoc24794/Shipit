namespace Shipit.Core;

/// <summary>
/// Checks whether a namespace or a referenced resource (ConfigMap, Secret, pull Secret)
/// in that namespace is allowed for Jobs.
/// </summary>
public interface IJobAllowStore
{
    Task<bool> IsNamespaceAllowedAsync(string ns, CancellationToken cancellationToken = default);

    Task<bool> IsReferenceAllowedAsync(string ns, string kind, string name, CancellationToken cancellationToken = default);
}
