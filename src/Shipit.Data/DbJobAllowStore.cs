using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data.Entities;

namespace Shipit.Data;

public sealed class DbJobAllowStore(IDbContextFactory<ShipitDbContext> dbFactory) : IJobAllowStore
{
    public async Task<bool> IsNamespaceAllowedAsync(string ns, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ns))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.JobAllowEntries.AsNoTracking()
            .AnyAsync(e => e.Namespace == ns && e.Kind == JobAllowKind.Namespace, cancellationToken);
    }

    public async Task<bool> IsReferenceAllowedAsync(string ns, string kind, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var allowKind = kind.ToLowerInvariant() switch
        {
            "namespace" => JobAllowKind.Namespace,
            "configmap" => JobAllowKind.ConfigMap,
            "secret" => JobAllowKind.Secret,
            "pullsecret" => JobAllowKind.PullSecret,
            _ => (JobAllowKind?)null
        };

        if (allowKind is null)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.JobAllowEntries.AsNoTracking()
            .AnyAsync(e => e.Namespace == ns && e.Kind == allowKind.Value && e.Name == name, cancellationToken);
    }
}
