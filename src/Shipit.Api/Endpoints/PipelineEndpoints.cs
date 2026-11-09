using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shipit.Data;
using Shipit.Data.Entities;
using Shipit.K8s;
using Shipit.Ssh;

namespace Shipit.Api;

internal static partial class PipelineEndpoints
{
    // Kubernetes object names (DNS-1123 label / subdomain).
    [GeneratedRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    private static partial Regex K8sLabel();

    public static void MapPipelines(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pipelines");

        group.MapGet("", async (ShipitDbContext db, CancellationToken ct) =>
        {
            var pipelines = await db.Pipelines.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
            var last = await LastRunsAsync(db, pipelines.Select(p => p.Id).ToList(), ct);
            return Results.Ok(pipelines.Select(p => p.ToDto(last.GetValueOrDefault(p.Id))));
        });

        group.MapGet("{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
        {
            var pipeline = await db.Pipelines.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (pipeline is null)
            {
                return Results.NotFound();
            }

            var last = await LastRunsAsync(db, [id], ct);
            return Results.Ok(pipeline.ToDto(last.GetValueOrDefault(id)));
        });

        group.MapPost("", async (PipelineInput input, ShipitDbContext db, DeployAllowList allowList, CancellationToken ct) =>
        {
            var errors = await ValidateAsync(input, db, allowList, existingId: null, ct);
            if (errors.Any)
            {
                return errors.ToResult();
            }

            var now = DateTime.UtcNow;
            var pipeline = new Pipeline { CreatedAt = now, UpdatedAt = now };
            Apply(pipeline, input);
            db.Pipelines.Add(pipeline);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/pipelines/{pipeline.Id}", pipeline.ToDto(null));
        });

        group.MapPut("{id:long}", async (long id, PipelineInput input, ShipitDbContext db, DeployAllowList allowList, CancellationToken ct) =>
        {
            var pipeline = await db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (pipeline is null)
            {
                return Results.NotFound();
            }

            var errors = await ValidateAsync(input, db, allowList, existingId: id, ct);
            if (errors.Any)
            {
                return errors.ToResult();
            }

            Apply(pipeline, input);
            pipeline.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            var last = await LastRunsAsync(db, [id], ct);
            return Results.Ok(pipeline.ToDto(last.GetValueOrDefault(id)));
        });

        group.MapDelete("{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
        {
            var pipeline = await db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (pipeline is null)
            {
                return Results.NotFound();
            }

            if (await db.Runs.AnyAsync(r => r.PipelineId == id && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running), ct))
            {
                return Problems.Conflict("Pipeline has an active run", "Wait for the run to finish before deleting the pipeline.");
            }

            db.Pipelines.Remove(pipeline);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<Dictionary<long, Run>> LastRunsAsync(ShipitDbContext db, List<long> pipelineIds, CancellationToken ct)
    {
        if (pipelineIds.Count == 0)
        {
            return [];
        }

        var latestIds = await db.Runs.AsNoTracking()
            .Where(r => pipelineIds.Contains(r.PipelineId))
            .GroupBy(r => r.PipelineId)
            .Select(g => g.Max(r => r.Id))
            .ToListAsync(ct);
        var runs = await db.Runs.AsNoTracking().Where(r => latestIds.Contains(r.Id)).ToListAsync(ct);
        return runs.ToDictionary(r => r.PipelineId);
    }

    private static void Apply(Pipeline p, PipelineInput i)
    {
        p.Name = i.Name!.Trim();
        p.GitUrl = i.GitUrl!.Trim();
        p.GitBranch = string.IsNullOrWhiteSpace(i.GitBranch) ? "main" : i.GitBranch.Trim();
        p.GitCredentialId = i.GitCredentialId!.Value;
        p.SlaveId = i.SlaveId!.Value;
        p.DockerfilePath = string.IsNullOrWhiteSpace(i.DockerfilePath) ? "Dockerfile" : i.DockerfilePath.Trim();
        p.ImageName = i.ImageName!.Trim();
        p.RegistryHost = i.RegistryHost!.Trim();
        p.RegistryCredentialId = i.RegistryCredentialId!.Value;
        p.DeployNamespace = i.DeployNamespace!.Trim();
        p.DeployDeployment = i.DeployDeployment!.Trim();
        p.DeployContainer = i.DeployContainer!.Trim();
    }

    private static async Task<FieldErrors> ValidateAsync(
        PipelineInput i, ShipitDbContext db, DeployAllowList allowList, long? existingId, CancellationToken ct)
    {
        var errors = new FieldErrors();

        var name = (i.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            errors.Add("name", "Name is required.");
        }
        else if (await db.Pipelines.AnyAsync(p => p.Name == name && p.Id != existingId, ct))
        {
            errors.Add("name", "A pipeline with this name already exists.");
        }

        // The same validators the engine uses when it builds commands, so a saved pipeline can always be run.
        var branch = string.IsNullOrWhiteSpace(i.GitBranch) ? "main" : i.GitBranch.Trim();
        Check(errors, "gitUrl", i.GitUrl, v => RemoteCommandBuilder.Clone(v, branch, "/tmp/shipit/run-1", "x"), required: true);
        Check(errors, "gitBranch", branch, v => RemoteCommandBuilder.Clone("https://example.com/r.git", v, "/tmp/shipit/run-1", "x"));
        var dockerfile = string.IsNullOrWhiteSpace(i.DockerfilePath) ? "Dockerfile" : i.DockerfilePath.Trim();
        Check(errors, "dockerfilePath", dockerfile, v => RemoteCommandBuilder.DockerBuild("/tmp/shipit/run-1", v, "example.com/x:t"));
        Check(errors, "registryHost", i.RegistryHost, v => RemoteCommandBuilder.DockerLogout(v), required: true);
        Check(errors, "imageName", i.ImageName, v => RemoteCommandBuilder.BuildImageRef("example.com", v, "t"), required: true);

        await CheckCredentialAsync(errors, db, "gitCredentialId", i.GitCredentialId, CredentialKind.GitToken, "git_token", ct);
        await CheckCredentialAsync(errors, db, "registryCredentialId", i.RegistryCredentialId, CredentialKind.Registry, "registry", ct);

        if (i.SlaveId is null)
        {
            errors.Add("slaveId", "A slave is required.");
        }
        else if (!await db.Slaves.AnyAsync(s => s.Id == i.SlaveId, ct))
        {
            errors.Add("slaveId", "Slave does not exist.");
        }

        var ns = (i.DeployNamespace ?? string.Empty).Trim();
        var deployment = (i.DeployDeployment ?? string.Empty).Trim();
        var container = (i.DeployContainer ?? string.Empty).Trim();
        var targetOk = true;
        if (!K8sLabel().IsMatch(ns) || ns.Length > 63)
        {
            errors.Add("deployNamespace", "Namespace must be a valid Kubernetes name.");
            targetOk = false;
        }

        if (!K8sLabel().IsMatch(deployment) || deployment.Length > 253)
        {
            errors.Add("deployDeployment", "Deployment must be a valid Kubernetes name.");
            targetOk = false;
        }

        if (!K8sLabel().IsMatch(container) || container.Length > 63)
        {
            errors.Add("deployContainer", "Container must be a valid Kubernetes name.");
        }

        if (targetOk && !allowList.IsAllowed(ns, deployment))
        {
            errors.Add("deployDeployment", "This deployment is not allowed by Shipit's deploy allowlist (SHIPIT_DEPLOY_ALLOWLIST).");
        }

        return errors;
    }

    private static void Check(FieldErrors errors, string field, string? value, Action<string> validate, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add(field, "This field is required.");
            }

            return;
        }

        try
        {
            validate(value.Trim());
        }
        catch (ArgumentException)
        {
            errors.Add(field, "Value is not valid.");
        }
    }

    private static async Task CheckCredentialAsync(
        FieldErrors errors, ShipitDbContext db, string field, long? id, CredentialKind expected, string expectedWire, CancellationToken ct)
    {
        if (id is null)
        {
            errors.Add(field, "A credential is required.");
            return;
        }

        var kind = await db.Credentials.Where(c => c.Id == id).Select(c => (CredentialKind?)c.Kind).FirstOrDefaultAsync(ct);
        if (kind is null)
        {
            errors.Add(field, "Credential does not exist.");
        }
        else if (kind != expected)
        {
            errors.Add(field, $"Credential must be of kind {expectedWire}.");
        }
    }
}
