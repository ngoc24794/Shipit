namespace Shipit.Core;

/// <summary>
/// Checks whether modifying a deployment target in a given namespace is permitted.
/// </summary>
public interface IDeployTargetStore
{
    Task<bool> IsAllowedAsync(string ns, string deployment, CancellationToken cancellationToken = default);
}
