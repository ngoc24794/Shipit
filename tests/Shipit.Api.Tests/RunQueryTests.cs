using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api.Tests;

public class RunQueryTests
{
    private static async Task<(long PipelineA, long PipelineB, long JobA)> SeedRunsAsync(ApiFactory factory, HttpClient client)
    {
        var seeded = await client.SeedAsync();
        var pipelineB = await client.CreateAsync("/api/pipelines", new
        {
            name = "billing-api",
            gitUrl = "https://git.example.com/team/billing.git",
            gitBranch = "main",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            dockerfilePath = "Dockerfile",
            imageName = "billing-svc",
            registryHost = "registry.example.com",
            registryCredentialId = seeded.RegCred,
            deployNamespace = "devops",
            deployDeployment = "shipit-demo",
            deployContainer = "app",
        });

        var jobA = await client.CreateAsync("/api/jobs", new
        {
            name = "db-migrate",
            gitUrl = "https://git.example.com/team/migrate.git",
            gitBranch = "main",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            dockerfilePath = "Dockerfile",
            imageName = "db-migrator",
            registryHost = "registry.example.com",
            registryCredentialId = seeded.RegCred,
            @namespace = "devops",
            containerName = "migrator",
            tasks = new[]
            {
                new { label = "migrate", args = new[] { "up" }, timeoutSeconds = 60, backoffLimit = 0, ttlSeconds = 300 },
            },
        });

        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContextAsync();
        var baseTime = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        db.Runs.AddRange(
            new Run { PipelineId = seeded.Pipeline, Status = RunStatus.Success, Tag = "v1.0.0", Image = "registry.example.com/shipit-demo:v1.0.0", CreatedAt = baseTime.AddMinutes(1) },
            new Run { PipelineId = seeded.Pipeline, Status = RunStatus.Failed, Tag = "abc1234", Image = "registry.example.com/shipit-demo:abc1234", CreatedAt = baseTime.AddMinutes(2) },
            new Run { PipelineId = pipelineB, Status = RunStatus.Success, Tag = "100%_rel", Image = "registry.example.com/billing-svc:100%_rel", CreatedAt = baseTime.AddMinutes(3) },
            new Run { PipelineId = pipelineB, Status = RunStatus.Cancelled, Tag = "1000Xrel", Image = "registry.example.com/billing-svc:1000Xrel", CreatedAt = baseTime.AddMinutes(4) },
            new Run { JobId = jobA, Status = RunStatus.Success, Tag = "sha-999", Image = "registry.example.com/db-migrator:sha-999", CreatedAt = baseTime.AddMinutes(5) },
            new Run { JobId = jobA, Status = RunStatus.Queued, Tag = "sha-1000", Image = "registry.example.com/db-migrator:sha-1000", CreatedAt = baseTime.AddMinutes(6) });
        await db.SaveChangesAsync();

        return (seeded.Pipeline, pipelineB, jobA);
    }

    [Fact]
    public async Task Filters_by_kind_status_pipelineId_jobId_and_q()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var (pipelineA, pipelineB, jobA) = await SeedRunsAsync(factory, client);

        // kind=job
        var jobRunsResp = await client.GetAsync("/api/runs?kind=job");
        Assert.Equal(HttpStatusCode.OK, jobRunsResp.StatusCode);
        Assert.Equal("2", jobRunsResp.Headers.GetValues("X-Total-Count").Single());
        var jobRuns = await jobRunsResp.ReadJsonAsync();
        Assert.Equal(2, jobRuns.GetArrayLength());
        Assert.All(jobRuns.EnumerateArray(), r => Assert.Equal("job", r.GetProperty("kind").GetString()));

        // kind=pipeline + status=success
        var successPipelinesResp = await client.GetAsync("/api/runs?kind=pipeline&status=success");
        Assert.Equal("2", successPipelinesResp.Headers.GetValues("X-Total-Count").Single());
        var successPipelines = await successPipelinesResp.ReadJsonAsync();
        Assert.Equal(2, successPipelines.GetArrayLength());

        // pipelineId + status=failed
        var failedAResp = await client.GetAsync($"/api/runs?pipelineId={pipelineA}&status=failed");
        Assert.Equal("1", failedAResp.Headers.GetValues("X-Total-Count").Single());
        var failedA = await failedAResp.ReadJsonAsync();
        Assert.Equal("abc1234", failedA[0].GetProperty("tag").GetString());

        // jobId + status=queued
        var queuedJobResp = await client.GetAsync($"/api/runs?jobId={jobA}&status=queued");
        Assert.Equal("1", queuedJobResp.Headers.GetValues("X-Total-Count").Single());

        // q case-insensitive on pipeline name, job name, image, tag, run id
        var qBilling = await (await client.GetAsync("/api/runs?q=BILLING-API")).ReadJsonAsync();
        Assert.Equal(2, qBilling.GetArrayLength());
        Assert.All(qBilling.EnumerateArray(), r => Assert.Equal(pipelineB, r.GetProperty("pipelineId").GetInt64()));

        var qMigrate = await (await client.GetAsync("/api/runs?q=DB-MIGRATE")).ReadJsonAsync();
        Assert.Equal(2, qMigrate.GetArrayLength());

        var qTag = await (await client.GetAsync("/api/runs?q=ABC1234")).ReadJsonAsync();
        Assert.Equal(1, qTag.GetArrayLength());

        var targetRunId = qTag[0].GetProperty("id").GetInt64();
        var qById = await (await client.GetAsync($"/api/runs?q=%23{targetRunId}")).ReadJsonAsync();
        Assert.Contains(qById.EnumerateArray(), r => r.GetProperty("id").GetInt64() == targetRunId);
    }

    [Fact]
    public async Task Pagination_and_backward_compatible_limit_work_with_total_count()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        await SeedRunsAsync(factory, client);

        // Page 1, pageSize 2 (total 6)
        var page1Resp = await client.GetAsync("/api/runs?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, page1Resp.StatusCode);
        Assert.Equal("6", page1Resp.Headers.GetValues("X-Total-Count").Single());
        var page1 = await page1Resp.ReadJsonAsync();
        Assert.Equal(2, page1.GetArrayLength());

        // Page 2, pageSize 2
        var page2Resp = await client.GetAsync("/api/runs?page=2&pageSize=2");
        Assert.Equal("6", page2Resp.Headers.GetValues("X-Total-Count").Single());
        var page2 = await page2Resp.ReadJsonAsync();
        Assert.Equal(2, page2.GetArrayLength());

        // Newest first, no overlap between page 1 and page 2
        var p1Ids = page1.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        var p2Ids = page2.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        Assert.Empty(p1Ids.Intersect(p2Ids));

        // page < 1 treated as 1
        var pageZeroResp = await client.GetAsync("/api/runs?page=0&pageSize=2");
        var pageZero = await pageZeroResp.ReadJsonAsync();
        Assert.Equal(p1Ids, pageZero.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList());

        // pageSize clamped to 1..100
        var clampedResp = await client.GetAsync("/api/runs?page=1&pageSize=0");
        var clamped = await clampedResp.ReadJsonAsync();
        Assert.Equal(1, clamped.GetArrayLength());

        // Backward compatible: no page -> uses limit
        var legacyResp = await client.GetAsync("/api/runs?limit=3");
        Assert.Equal(HttpStatusCode.OK, legacyResp.StatusCode);
        Assert.Equal("6", legacyResp.Headers.GetValues("X-Total-Count").Single());
        var legacy = await legacyResp.ReadJsonAsync();
        Assert.Equal(3, legacy.GetArrayLength());
    }

    [Fact]
    public async Task Escapes_like_wildcards_percent_and_underscore()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        await SeedRunsAsync(factory, client);

        // "100%_rel" should match ONLY tag "100%_rel", NOT "1000Xrel"
        var resp = await client.GetAsync("/api/runs?q=" + Uri.EscapeDataString("100%_rel"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var arr = await resp.ReadJsonAsync();
        Assert.Equal(1, arr.GetArrayLength());
        Assert.Equal("100%_rel", arr[0].GetProperty("tag").GetString());

        // "_" alone should match ONLY the run containing literal "_"
        var respUnderscore = await client.GetAsync("/api/runs?q=_");
        var arrUnderscore = await respUnderscore.ReadJsonAsync();
        Assert.Equal(1, arrUnderscore.GetArrayLength());
        Assert.Equal("100%_rel", arrUnderscore[0].GetProperty("tag").GetString());
    }

    [Fact]
    public async Task Invalid_kind_or_status_returns_400_problem_details()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        var badKind = await client.GetAsync("/api/runs?kind=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        var kindJson = await badKind.ReadJsonAsync();
        Assert.True(kindJson.GetProperty("errors").TryGetProperty("kind", out _));

        var badStatus = await client.GetAsync("/api/runs?status=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
        var statusJson = await badStatus.ReadJsonAsync();
        Assert.True(statusJson.GetProperty("errors").TryGetProperty("status", out _));
    }
}
