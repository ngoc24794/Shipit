using System.Text;
using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api;

/// <summary>
/// Coordinates Kubernetes RBAC reconciliation against the current allowlist state in <see cref="ShipitDbContext"/>.
/// </summary>
public sealed class RbacSyncService
{
    public const string WarningHeaderName = "X-Shipit-Rbac-Warning";

    private readonly IDbContextFactory<ShipitDbContext> _dbFactory;
    private readonly IK8sRbacSync _rbacSync;
    private readonly ILogger<RbacSyncService> _logger;

    public RbacSyncService(
        IDbContextFactory<ShipitDbContext> dbFactory,
        IK8sRbacSync rbacSync,
        ILogger<RbacSyncService> logger)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _rbacSync = rbacSync ?? throw new ArgumentNullException(nameof(rbacSync));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RbacSyncResult> SyncNamespaceAsync(string targetNamespace, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNamespace);
        var ns = targetNamespace.Trim();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var deployments = await db.DeployTargets.AsNoTracking()
            .Where(t => t.Namespace == ns)
            .Select(t => t.Deployment)
            .ToListAsync(ct);

        var allowJobs = await db.JobAllowEntries.AsNoTracking()
            .AnyAsync(e => e.Namespace == ns && e.Kind == JobAllowKind.Namespace, ct);

        return await _rbacSync.SyncNamespaceAsync(ns, deployments, allowJobs, ct);
    }

    public async Task<RbacSyncResult> SyncNamespaceAndWarnAsync(
        string targetNamespace,
        HttpResponse response,
        CancellationToken ct = default)
    {
        var result = await SyncNamespaceAsync(targetNamespace, ct);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Kubernetes RBAC sync failed for namespace '{Namespace}': {Error}",
                targetNamespace,
                result.ErrorMessage);
            response.Headers[WarningHeaderName] = FormatWarningHeader(targetNamespace, result.ErrorMessage);
        }

        return result;
    }

    public async Task<IReadOnlyList<RbacNamespaceSyncResult>> SyncAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var deployTargets = await db.DeployTargets.AsNoTracking()
            .Select(t => new { t.Namespace, t.Deployment })
            .ToListAsync(ct);

        var jobNamespaces = await db.JobAllowEntries.AsNoTracking()
            .Where(e => e.Kind == JobAllowKind.Namespace)
            .Select(e => e.Namespace)
            .Distinct()
            .ToListAsync(ct);

        var deploymentsByNs = deployTargets
            .GroupBy(t => t.Namespace, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<string>)g.Select(x => x.Deployment).ToList(),
                StringComparer.Ordinal);

        var jobNsSet = new HashSet<string>(jobNamespaces, StringComparer.Ordinal);

        var allNamespaces = deploymentsByNs.Keys
            .Union(jobNsSet, StringComparer.Ordinal)
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToList();

        var results = new List<RbacNamespaceSyncResult>(allNamespaces.Count);
        foreach (var ns in allNamespaces)
        {
            var deployments = deploymentsByNs.TryGetValue(ns, out var list)
                ? list
                : Array.Empty<string>();
            var allowJobs = jobNsSet.Contains(ns);

            var result = await _rbacSync.SyncNamespaceAsync(ns, deployments, allowJobs, ct);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Kubernetes RBAC sync failed for namespace '{Namespace}': {Error}",
                    ns,
                    result.ErrorMessage);
            }

            results.Add(new RbacNamespaceSyncResult(ns, result.Succeeded, result.ErrorMessage));
        }

        return results;
    }

    internal static string FormatWarningHeader(string targetNamespace, string? errorMessage)
    {
        var raw = string.IsNullOrWhiteSpace(errorMessage)
            ? $"RBAC sync failed for namespace '{targetNamespace}'."
            : $"RBAC sync failed for namespace '{targetNamespace}': {errorMessage}";

        return SanitizeHeaderValue(raw);
    }

    internal static string SanitizeHeaderValue(string value, int maxLength = 512)
    {
        var sb = new StringBuilder(Math.Min(value.Length, maxLength));
        var lastWasSpace = false;

        foreach (var ch in value)
        {
            if (sb.Length >= maxLength)
            {
                break;
            }

            if (ch is '\r' or '\n' or '\t' || char.IsControl(ch))
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            // HTTP header values in Kestrel must be ASCII printable (0x20..0x7E).
            if (ch < 0x20 || ch > 0x7E)
            {
                continue;
            }

            sb.Append(ch);
            lastWasSpace = ch == ' ';
        }

        return sb.ToString().Trim();
    }
}
