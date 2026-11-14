using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api.Tests;

public class JobEndpointStubTests
{
    private static readonly (string Method, string Url)[] JobRoutes =
    [
        ("GET", "/api/jobs"),
        ("POST", "/api/jobs"),
        ("POST", "/api/jobs/validate"),
        ("POST", "/api/jobs/preview"),
        ("GET", "/api/jobs/1"),
        ("PUT", "/api/jobs/1"),
        ("DELETE", "/api/jobs/1"),
        ("POST", "/api/jobs/1/runs"),
        ("GET", "/api/job-allow-entries"),
        ("POST", "/api/job-allow-entries"),
        ("DELETE", "/api/job-allow-entries/1"),
    ];

    [Theory]
    [InlineData("GET", "/api/jobs")]
    [InlineData("POST", "/api/jobs")]
    [InlineData("POST", "/api/jobs/validate")]
    [InlineData("POST", "/api/jobs/preview")]
    [InlineData("GET", "/api/jobs/1")]
    [InlineData("PUT", "/api/jobs/1")]
    [InlineData("DELETE", "/api/jobs/1")]
    [InlineData("POST", "/api/jobs/1/runs")]
    [InlineData("GET", "/api/job-allow-entries")]
    [InlineData("POST", "/api/job-allow-entries")]
    [InlineData("DELETE", "/api/job-allow-entries/1")]
    public async Task Job_endpoints_return_401_without_session(string method, string url)
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { name = "sample" });
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/jobs")]
    [InlineData("POST", "/api/jobs")]
    [InlineData("POST", "/api/jobs/validate")]
    [InlineData("POST", "/api/jobs/preview")]
    [InlineData("GET", "/api/jobs/1")]
    [InlineData("PUT", "/api/jobs/1")]
    [InlineData("DELETE", "/api/jobs/1")]
    [InlineData("POST", "/api/jobs/1/runs")]
    [InlineData("GET", "/api/job-allow-entries")]
    [InlineData("POST", "/api/job-allow-entries")]
    [InlineData("DELETE", "/api/job-allow-entries/1")]
    public async Task Job_endpoints_return_501_not_implemented_when_logged_in(string method, string url)
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { name = "sample" });
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        var body = await response.ReadJsonAsync();
        Assert.Equal("Not implemented", body.GetProperty("title").GetString());
        Assert.Equal("Implemented in task J5 (job-api).", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Get_runs_filters_by_job_id_and_returns_correct_kind_and_tasks()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        // 1. Seed a pipeline and pipeline run
        var seeded = await client.SeedAsync();
        var pipeRunResp = await client.PostAsync($"/api/pipelines/{seeded.Pipeline}/runs", null);
        Assert.Equal(HttpStatusCode.Accepted, pipeRunResp.StatusCode);
        var pipeRunJson = await pipeRunResp.ReadJsonAsync();
        var pipelineRunId = pipeRunJson.GetProperty("id").GetInt64();

        // 2. Insert a Job and Job run directly into the database
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>();
        long jobId;
        long jobRunId;

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var slave = await db.Slaves.FirstAsync();
            var cred = await db.Credentials.FirstAsync(c => c.Kind == CredentialKind.GitToken);

            var job = new Job
            {
                Name = "job-run-filter-test",
                GitUrl = "https://git.example.com/repo.git",
                GitCredentialId = cred.Id,
                SlaveId = slave.Id,
                ImageName = "job-filter",
                RegistryHost = "registry.local",
                Namespace = "devops",
            };
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;

            var jobRun = new Run
            {
                JobId = jobId,
                PipelineId = null,
                Status = RunStatus.Success,
                Tag = "jobsha1",
                Image = "registry.local/job-filter:jobsha1",
            };
            db.Runs.Add(jobRun);
            await db.SaveChangesAsync();
            jobRunId = jobRun.Id;

            db.RunTasks.Add(new RunTask
            {
                RunId = jobRunId,
                Position = 0,
                Label = "task-alpha",
                Status = StepStatus.Success,
                ExitCode = 0,
                K8sJobName = "job-filter-1-0",
            });
            await db.SaveChangesAsync();
        }

        // 3. Filter runs by jobId
        var jobRunsResp = await client.GetAsync($"/api/runs?jobId={jobId}");
        Assert.Equal(HttpStatusCode.OK, jobRunsResp.StatusCode);
        var jobRuns = await jobRunsResp.ReadJsonAsync();
        Assert.Single(jobRuns.EnumerateArray());
        var singleJobRun = jobRuns[0];
        Assert.Equal(jobRunId, singleJobRun.GetProperty("id").GetInt64());
        Assert.Equal("job", singleJobRun.GetProperty("kind").GetString());
        Assert.Equal(jobId, singleJobRun.GetProperty("jobId").GetInt64());
        Assert.Equal(JsonValueKind.Null, singleJobRun.GetProperty("pipelineId").ValueKind);

        // 4. Filter runs by pipelineId
        var pipeRunsResp = await client.GetAsync($"/api/runs?pipelineId={seeded.Pipeline}");
        Assert.Equal(HttpStatusCode.OK, pipeRunsResp.StatusCode);
        var pipeRuns = await pipeRunsResp.ReadJsonAsync();
        Assert.Contains(pipeRuns.EnumerateArray(), r => r.GetProperty("id").GetInt64() == pipelineRunId);
        Assert.All(pipeRuns.EnumerateArray(), r =>
        {
            Assert.Equal("pipeline", r.GetProperty("kind").GetString());
            Assert.Equal(seeded.Pipeline, r.GetProperty("pipelineId").GetInt64());
            Assert.Equal(JsonValueKind.Null, r.GetProperty("jobId").ValueKind);
        });

        // 5. Query without filter returns both
        var allRunsResp = await client.GetAsync("/api/runs");
        Assert.Equal(HttpStatusCode.OK, allRunsResp.StatusCode);
        var allRuns = (await allRunsResp.ReadJsonAsync()).EnumerateArray().ToList();
        Assert.Contains(allRuns, r => r.GetProperty("id").GetInt64() == jobRunId);
        Assert.Contains(allRuns, r => r.GetProperty("id").GetInt64() == pipelineRunId);

        // 6. Query single run detail for Job run returns tasks
        var runDetailResp = await client.GetAsync($"/api/runs/{jobRunId}");
        Assert.Equal(HttpStatusCode.OK, runDetailResp.StatusCode);
        var detail = await runDetailResp.ReadJsonAsync();
        Assert.Equal("job", detail.GetProperty("kind").GetString());
        Assert.Equal(jobId, detail.GetProperty("jobId").GetInt64());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("pipelineId").ValueKind);
        var tasks = detail.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Single(tasks);
        Assert.Equal(0, tasks[0].GetProperty("position").GetInt32());
        Assert.Equal("task-alpha", tasks[0].GetProperty("label").GetString());
        Assert.Equal("success", tasks[0].GetProperty("status").GetString());
        Assert.Equal(0, tasks[0].GetProperty("exitCode").GetInt32());
    }
}
