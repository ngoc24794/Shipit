namespace Shipit.Api;

internal static class RbacEndpoints
{
    public static void MapRbac(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rbac/sync", async (RbacSyncService rbacSync, CancellationToken ct) =>
        {
            var results = await rbacSync.SyncAllAsync(ct);
            var dtoResults = results
                .Select(r => new RbacNamespaceSyncResultDto(r.Namespace, r.Succeeded, r.ErrorMessage))
                .ToList();
            var allSucceeded = results.All(r => r.Succeeded);

            return Results.Ok(new RbacSyncResponseDto(dtoResults, allSucceeded));
        });
    }
}
