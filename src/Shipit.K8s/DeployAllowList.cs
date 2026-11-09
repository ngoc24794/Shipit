using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// Controls which namespaces and deployments can be modified by Shipit.
/// </summary>
public sealed class DeployAllowList
{
    private readonly Dictionary<string, HashSet<string>>? _allowed;
    private readonly IDeployTargetStore? _store;

    public DeployAllowList(IEnumerable<DeployAllowListEntry>? entries)
    {
        _allowed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Namespace))
                {
                    continue;
                }

                if (!_allowed.TryGetValue(entry.Namespace, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    _allowed[entry.Namespace] = set;
                }

                if (entry.Deployments != null)
                {
                    foreach (var dep in entry.Deployments)
                    {
                        if (!string.IsNullOrEmpty(dep))
                        {
                            set.Add(dep);
                        }
                    }
                }
            }
        }
    }

    public DeployAllowList(IDeployTargetStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Checks whether modifying <paramref name="deployment"/> in <paramref name="ns"/> is permitted.
    /// Comparison is exact and case-sensitive. An empty allowlist denies everything.
    /// </summary>
    public bool IsAllowed(string ns, string deployment)
    {
        if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(deployment))
        {
            return false;
        }

        if (_store != null)
        {
            return _store.IsAllowedAsync(ns, deployment).GetAwaiter().GetResult();
        }

        return _allowed != null && _allowed.TryGetValue(ns, out var deployments) && deployments.Contains(deployment);
    }

    /// <summary>
    /// Asynchronously checks whether modifying <paramref name="deployment"/> in <paramref name="ns"/> is permitted.
    /// </summary>
    public async Task<bool> IsAllowedAsync(string ns, string deployment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(deployment))
        {
            return false;
        }

        if (_store != null)
        {
            return await _store.IsAllowedAsync(ns, deployment, cancellationToken);
        }

        return _allowed != null && _allowed.TryGetValue(ns, out var deployments) && deployments.Contains(deployment);
    }
}
