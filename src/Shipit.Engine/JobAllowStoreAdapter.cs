using Shipit.Core;

namespace Shipit.Engine;

/// <summary>Adapts an <see cref="IJobAllowStore"/> to the <see cref="IJobAllowList"/> interface without requiring a reference to Shipit.K8s.</summary>
internal sealed class JobAllowStoreAdapter(IJobAllowStore store) : IJobAllowList
{
    public Task<bool> IsNamespaceAllowedAsync(string ns, CancellationToken cancellationToken = default) =>
        store.IsNamespaceAllowedAsync(ns, cancellationToken);

    public Task<bool> IsConfigMapAllowedAsync(string ns, string name, CancellationToken cancellationToken = default) =>
        store.IsReferenceAllowedAsync(ns, "configmap", name, cancellationToken);

    public Task<bool> IsSecretAllowedAsync(string ns, string name, CancellationToken cancellationToken = default) =>
        store.IsReferenceAllowedAsync(ns, "secret", name, cancellationToken);

    public Task<bool> IsPullSecretAllowedAsync(string ns, string name, CancellationToken cancellationToken = default) =>
        store.IsReferenceAllowedAsync(ns, "pullsecret", name, cancellationToken);
}
