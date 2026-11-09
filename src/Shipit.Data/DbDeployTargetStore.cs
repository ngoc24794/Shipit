using Microsoft.EntityFrameworkCore;
using Shipit.Core;

namespace Shipit.Data;

public sealed class DbDeployTargetStore(IDbContextFactory<ShipitDbContext> dbFactory) : IDeployTargetStore
{
    public async Task<bool> IsAllowedAsync(string ns, string deployment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(deployment))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DeployTargets.AsNoTracking()
            .AnyAsync(t => t.Namespace == ns && t.Deployment == deployment, cancellationToken);
    }
}
