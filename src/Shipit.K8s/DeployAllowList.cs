using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// Controls which namespaces and deployments can be modified by Shipit.
/// </summary>
public sealed class DeployAllowList
{
    private readonly Dictionary<string, HashSet<string>> _allowed;

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

        return _allowed.TryGetValue(ns, out var deployments) && deployments.Contains(deployment);
    }
}
