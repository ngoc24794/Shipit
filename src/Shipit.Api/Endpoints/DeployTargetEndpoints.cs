using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api;

internal static partial class DeployTargetEndpoints
{
    [GeneratedRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    private static partial Regex K8sLabel();

    [GeneratedRegex("^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$")]
    private static partial Regex K8sSubdomain();

    public static void MapDeployTargets(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/deploy-targets");

        group.MapGet("", async (ShipitDbContext db, CancellationToken ct) =>
        {
            var targets = await db.DeployTargets.AsNoTracking()
                .OrderBy(t => t.Namespace)
                .ThenBy(t => t.Deployment)
                .ToListAsync(ct);
            return Results.Ok(targets.Select(t => t.ToDto()));
        });

        group.MapPost("", async (DeployTargetInput input, ShipitDbContext db, CancellationToken ct) =>
        {
            var errors = new FieldErrors();
            var ns = (input.Namespace ?? string.Empty).Trim();
            var deployment = (input.Deployment ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(ns) || ns.Length > 63 || !K8sLabel().IsMatch(ns))
            {
                errors.Add("namespace", "Namespace must be a valid DNS-1123 label (1-63 lowercase alphanumeric characters or '-').");
            }

            if (string.IsNullOrEmpty(deployment) || deployment.Length > 253 || !K8sSubdomain().IsMatch(deployment))
            {
                errors.Add("deployment", "Deployment must be a valid DNS-1123 subdomain (1-253 lowercase alphanumeric characters, '-' or '.').");
            }

            if (errors.Any)
            {
                return errors.ToResult();
            }

            var exists = await db.DeployTargets.AnyAsync(t => t.Namespace == ns && t.Deployment == deployment, ct);
            if (exists)
            {
                return Problems.Conflict("Deploy target already exists", $"Deploy target '{ns}/{deployment}' already exists.");
            }

            var target = new DeployTarget
            {
                Namespace = ns,
                Deployment = deployment,
                CreatedAt = DateTime.UtcNow
            };
            db.DeployTargets.Add(target);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/deploy-targets/{target.Id}", target.ToDto());
        });

        group.MapDelete("{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
        {
            var target = await db.DeployTargets.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (target is null)
            {
                return Results.NotFound();
            }

            var usingPipelines = await db.Pipelines.AsNoTracking()
                .Where(p => p.DeployNamespace == target.Namespace && p.DeployDeployment == target.Deployment)
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToListAsync(ct);

            if (usingPipelines.Count > 0)
            {
                var names = string.Join(", ", usingPipelines.Select(n => $"'{n}'"));
                return Problems.Conflict(
                    "Deploy target is in use",
                    $"Cannot delete deploy target '{target.Namespace}/{target.Deployment}' because it is in use by pipeline(s): {names}.");
            }

            db.DeployTargets.Remove(target);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
