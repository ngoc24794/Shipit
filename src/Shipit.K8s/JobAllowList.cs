using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// Controls which namespaces and resources (ConfigMaps, Secrets, image pull secrets)
/// can be referenced or run by Kubernetes Jobs in Shipit.
/// </summary>
public sealed class JobAllowList : IJobAllowList
{
    private readonly IJobAllowStore? _store;
    private readonly HashSet<string>? _namespaces;
    private readonly HashSet<(string Namespace, string Name)>? _configMaps;
    private readonly HashSet<(string Namespace, string Name)>? _secrets;
    private readonly HashSet<(string Namespace, string Name)>? _pullSecrets;

    public JobAllowList(IJobAllowStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public JobAllowList(
        IEnumerable<string>? allowedNamespaces = null,
        IEnumerable<(string Namespace, string Name)>? allowedConfigMaps = null,
        IEnumerable<(string Namespace, string Name)>? allowedSecrets = null,
        IEnumerable<(string Namespace, string Name)>? allowedPullSecrets = null)
    {
        _namespaces = allowedNamespaces != null
            ? new HashSet<string>(allowedNamespaces.Where(s => !string.IsNullOrEmpty(s)), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        _configMaps = allowedConfigMaps != null
            ? new HashSet<(string Namespace, string Name)>(
                allowedConfigMaps.Where(e => !string.IsNullOrEmpty(e.Namespace) && !string.IsNullOrEmpty(e.Name)),
                TupleComparer.Instance)
            : new HashSet<(string Namespace, string Name)>(TupleComparer.Instance);

        _secrets = allowedSecrets != null
            ? new HashSet<(string Namespace, string Name)>(
                allowedSecrets.Where(e => !string.IsNullOrEmpty(e.Namespace) && !string.IsNullOrEmpty(e.Name)),
                TupleComparer.Instance)
            : new HashSet<(string Namespace, string Name)>(TupleComparer.Instance);

        _pullSecrets = allowedPullSecrets != null
            ? new HashSet<(string Namespace, string Name)>(
                allowedPullSecrets.Where(e => !string.IsNullOrEmpty(e.Namespace) && !string.IsNullOrEmpty(e.Name)),
                TupleComparer.Instance)
            : new HashSet<(string Namespace, string Name)>(TupleComparer.Instance);
    }

    public bool IsNamespaceAllowed(string ns) =>
        IsNamespaceAllowedAsync(ns).GetAwaiter().GetResult();

    public async Task<bool> IsNamespaceAllowedAsync(string ns, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return false;
        }

        if (_store != null)
        {
            return await _store.IsNamespaceAllowedAsync(ns, cancellationToken);
        }

        return _namespaces != null && _namespaces.Contains(ns);
    }

    public bool IsConfigMapAllowed(string ns, string name) =>
        IsConfigMapAllowedAsync(ns, name).GetAwaiter().GetResult();

    public async Task<bool> IsConfigMapAllowedAsync(string ns, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (_store != null)
        {
            return await _store.IsReferenceAllowedAsync(ns, "configmap", name, cancellationToken);
        }

        return _configMaps != null && _configMaps.Contains((ns, name));
    }

    public bool IsSecretAllowed(string ns, string name) =>
        IsSecretAllowedAsync(ns, name).GetAwaiter().GetResult();

    public async Task<bool> IsSecretAllowedAsync(string ns, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (_store != null)
        {
            return await _store.IsReferenceAllowedAsync(ns, "secret", name, cancellationToken);
        }

        return _secrets != null && _secrets.Contains((ns, name));
    }

    public bool IsPullSecretAllowed(string ns, string name) =>
        IsPullSecretAllowedAsync(ns, name).GetAwaiter().GetResult();

    public async Task<bool> IsPullSecretAllowedAsync(string ns, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (_store != null)
        {
            return await _store.IsReferenceAllowedAsync(ns, "pullsecret", name, cancellationToken);
        }

        return _pullSecrets != null && _pullSecrets.Contains((ns, name));
    }

    private sealed class TupleComparer : IEqualityComparer<(string Namespace, string Name)>
    {
        public static readonly TupleComparer Instance = new();

        public bool Equals((string Namespace, string Name) x, (string Namespace, string Name) y) =>
            string.Equals(x.Namespace, y.Namespace, StringComparison.Ordinal) &&
            string.Equals(x.Name, y.Name, StringComparison.Ordinal);

        public int GetHashCode((string Namespace, string Name) obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Namespace),
                StringComparer.Ordinal.GetHashCode(obj.Name));
    }
}
