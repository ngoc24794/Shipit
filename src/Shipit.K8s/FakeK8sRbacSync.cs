using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// Recorded call to <see cref="FakeK8sRbacSync.SyncNamespaceAsync"/>.
/// </summary>
public sealed record FakeRbacSyncCall(
    string Namespace,
    IReadOnlyList<string> AllowedDeployments,
    bool AllowJobs,
    RbacSyncResult Result);

/// <summary>
/// Effective RBAC state recorded for a namespace by <see cref="FakeK8sRbacSync"/>.
/// </summary>
public sealed record FakeRbacNamespaceState(
    IReadOnlyList<string> AllowedDeployments,
    bool AllowJobs);

/// <summary>
/// In-memory fake implementation of <see cref="IK8sRbacSync"/> for <c>Shipit:UseFakes=true</c> and tests.
/// </summary>
public sealed class FakeK8sRbacSync : IK8sRbacSync
{
    private readonly object _lock = new();
    private readonly List<FakeRbacSyncCall> _calls = [];
    private readonly Dictionary<string, FakeRbacNamespaceState> _syncedNamespaces = new(StringComparer.Ordinal);

    /// <summary>
    /// Fixed result returned by <see cref="SyncNamespaceAsync"/> when <see cref="ResultFactory"/> is null.
    /// Defaults to success.
    /// </summary>
    public RbacSyncResult? ConfiguredResult { get; set; }

    /// <summary>
    /// Dynamic factory to customize or fail sync results per call in tests.
    /// </summary>
    public Func<string, IReadOnlyList<string>, bool, RbacSyncResult>? ResultFactory { get; set; }

    public IReadOnlyList<FakeRbacSyncCall> Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls.ToList();
            }
        }
    }

    public IReadOnlyDictionary<string, FakeRbacNamespaceState> SyncedNamespaces
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, FakeRbacNamespaceState>(_syncedNamespaces, StringComparer.Ordinal);
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _calls.Clear();
            _syncedNamespaces.Clear();
        }
    }

    public Task<RbacSyncResult> SyncNamespaceAsync(
        string targetNamespace,
        IReadOnlyCollection<string> allowedDeployments,
        bool allowJobs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNamespace);

        var ns = targetNamespace.Trim();
        var sorted = (allowedDeployments ?? Array.Empty<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        lock (_lock)
        {
            var result = ResultFactory?.Invoke(ns, sorted, allowJobs)
                ?? ConfiguredResult
                ?? new RbacSyncResult(true);

            _calls.Add(new FakeRbacSyncCall(ns, sorted, allowJobs, result));

            if (result.Succeeded)
            {
                if (sorted.Count == 0 && !allowJobs)
                {
                    _syncedNamespaces.Remove(ns);
                }
                else
                {
                    _syncedNamespaces[ns] = new FakeRbacNamespaceState(sorted, allowJobs);
                }
            }

            return Task.FromResult(result);
        }
    }
}
