using Microsoft.EntityFrameworkCore;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api;

internal static class SearchEndpoints
{
    public static void MapSearch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", async (string? q, int? limit, ShipitDbContext db, CancellationToken ct) =>
        {
            var queryText = (q ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(queryText))
            {
                return Results.Ok(new SearchResponseDto(string.Empty, []));
            }

            var take = Math.Clamp(limit ?? 5, 1, 20);
            var pattern = SearchHelpers.ToLikePattern(queryText);
            var groups = new List<SearchGroupDto>();

            // 1. Runs
            var runRows = await SearchHelpers.FilterRunsByQuery(db.Runs.AsNoTracking(), queryText)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .Take(take)
                .Select(r => new
                {
                    r.Id,
                    r.PipelineId,
                    r.JobId,
                    PipelineName = r.Pipeline != null ? r.Pipeline.Name : null,
                    JobName = r.Job != null ? r.Job.Name : null,
                    r.Status,
                    r.Tag,
                    r.Image,
                })
                .ToListAsync(ct);
            if (runRows.Count > 0)
            {
                var items = runRows.Select(r =>
                {
                    var target = r.PipelineName ?? r.JobName ?? (r.JobId is not null ? $"Job #{r.JobId}" : $"Pipeline #{r.PipelineId}");
                    var parts = new[] { Wire.Of(r.Status), r.Tag, r.Image }
                        .Where(p => !string.IsNullOrWhiteSpace(p));
                    return new SearchItemDto(
                        r.Id.ToString(),
                        $"#{r.Id} · {target}",
                        string.Join(" · ", parts),
                        $"/runs/{r.Id}");
                }).ToList();
                groups.Add(new SearchGroupDto("run", "Lần chạy", items));
            }

            // 2. Pipelines
            var pipelineRows = await db.Pipelines.AsNoTracking()
                .Where(p =>
                    EF.Functions.Like(p.Name.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(p.ImageName.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(p.GitUrl.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(p.DeployNamespace.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(p.DeployDeployment.ToLower(), pattern, SearchHelpers.LikeEscape))
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Id)
                .Take(take)
                .Select(p => new { p.Id, p.Name, p.DeployNamespace, p.DeployDeployment, p.ImageName })
                .ToListAsync(ct);
            if (pipelineRows.Count > 0)
            {
                var items = pipelineRows
                    .Select(p => new SearchItemDto(
                        p.Id.ToString(),
                        p.Name,
                        $"{p.DeployNamespace}/{p.DeployDeployment} · {p.ImageName}",
                        $"/pipelines/{p.Id}/edit"))
                    .ToList();
                groups.Add(new SearchGroupDto("pipeline", "Pipeline", items));
            }

            // 3. Jobs
            var jobRows = await db.Jobs.AsNoTracking()
                .Where(j =>
                    EF.Functions.Like(j.Name.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(j.ImageName.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(j.GitUrl.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(j.Namespace.ToLower(), pattern, SearchHelpers.LikeEscape))
                .OrderBy(j => j.Name)
                .ThenBy(j => j.Id)
                .Take(take)
                .Select(j => new { j.Id, j.Name, j.Namespace, j.ImageName })
                .ToListAsync(ct);
            if (jobRows.Count > 0)
            {
                var items = jobRows
                    .Select(j => new SearchItemDto(
                        j.Id.ToString(),
                        j.Name,
                        $"{j.Namespace} · {j.ImageName}",
                        $"/jobs/{j.Id}"))
                    .ToList();
                groups.Add(new SearchGroupDto("job", "Job", items));
            }

            // 4. Slaves
            var slaveRows = await db.Slaves.AsNoTracking()
                .Where(s =>
                    EF.Functions.Like(s.Name.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(s.Host.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(s.User.ToLower(), pattern, SearchHelpers.LikeEscape))
                .OrderBy(s => s.Name)
                .ThenBy(s => s.Id)
                .Take(take)
                .Select(s => new { s.Id, s.Name, s.User, s.Host, s.Port })
                .ToListAsync(ct);
            if (slaveRows.Count > 0)
            {
                var items = slaveRows
                    .Select(s => new SearchItemDto(
                        s.Id.ToString(),
                        s.Name,
                        $"{s.User}@{s.Host}:{s.Port}",
                        $"/slaves/{s.Id}/edit"))
                    .ToList();
                groups.Add(new SearchGroupDto("slave", "Máy build", items));
            }

            // 5. Credentials (name and kind only — never touch ProtectedValue or secret fields)
            var credRows = await db.Credentials.AsNoTracking()
                .Where(c => EF.Functions.Like(c.Name.ToLower(), pattern, SearchHelpers.LikeEscape))
                .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
                .Take(take)
                .Select(c => new { c.Id, c.Name, c.Kind })
                .ToListAsync(ct);
            if (credRows.Count > 0)
            {
                var items = credRows
                    .Select(c => new SearchItemDto(
                        c.Id.ToString(),
                        c.Name,
                        Wire.Of(c.Kind),
                        $"/credentials/{c.Id}/edit"))
                    .ToList();
                groups.Add(new SearchGroupDto("credential", "Thông tin xác thực", items));
            }

            // 6. Deploy targets
            var deployRows = await db.DeployTargets.AsNoTracking()
                .Where(t =>
                    EF.Functions.Like(t.Namespace.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(t.Deployment.ToLower(), pattern, SearchHelpers.LikeEscape))
                .OrderBy(t => t.Namespace)
                .ThenBy(t => t.Deployment)
                .ThenBy(t => t.Id)
                .Take(take)
                .Select(t => new { t.Id, t.Namespace, t.Deployment })
                .ToListAsync(ct);
            if (deployRows.Count > 0)
            {
                var items = deployRows
                    .Select(t => new SearchItemDto(
                        t.Id.ToString(),
                        $"{t.Namespace}/{t.Deployment}",
                        t.Namespace,
                        "/settings/allowlist?tab=deployment"))
                    .ToList();
                groups.Add(new SearchGroupDto("deploy-target", "Mục tiêu deploy", items));
            }

            // 7. Job allow entries
            var allowRows = await db.JobAllowEntries.AsNoTracking()
                .Where(e =>
                    EF.Functions.Like(e.Namespace.ToLower(), pattern, SearchHelpers.LikeEscape)
                    || EF.Functions.Like(e.Name.ToLower(), pattern, SearchHelpers.LikeEscape))
                .OrderBy(e => e.Namespace)
                .ThenBy(e => e.Kind)
                .ThenBy(e => e.Name)
                .ThenBy(e => e.Id)
                .Take(take)
                .Select(e => new { e.Id, e.Namespace, e.Kind, e.Name })
                .ToListAsync(ct);
            if (allowRows.Count > 0)
            {
                var items = allowRows
                    .Select(e => new SearchItemDto(
                        e.Id.ToString(),
                        e.Kind == JobAllowKind.Namespace ? e.Namespace : $"{e.Namespace}/{e.Name}",
                        Wire.Of(e.Kind),
                        "/settings/allowlist?tab=job"))
                    .ToList();
                groups.Add(new SearchGroupDto("job-allow", "Quyền Job", items));
            }

            return Results.Ok(new SearchResponseDto(queryText, groups));
        });
    }
}
