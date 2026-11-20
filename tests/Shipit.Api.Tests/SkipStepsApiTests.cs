using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Core;
using Shipit.K8s;

namespace Shipit.Api.Tests;

public sealed class SkipStepsApiTests
{
    [Fact]
    public async Task Pipeline_trigger_with_empty_or_null_skipSteps_runs_all_steps()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var r1 = await client.PostAsJsonAsync($"/api/pipelines/{seeded.Pipeline}/runs", new { });
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);
        var id1 = (await r1.ReadJsonAsync()).GetProperty("id").GetInt64();
        var done1 = await client.WaitForRunAsync(id1, "success", "failed");
        Assert.Equal("success", done1.GetProperty("status").GetString());
        Assert.All(done1.GetProperty("steps").EnumerateArray(), s => Assert.Equal("success", s.GetProperty("status").GetString()));

        var r2 = await client.PostAsJsonAsync($"/api/pipelines/{seeded.Pipeline}/runs", new { skipSteps = (string[]?)null });
        Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);
        var id2 = (await r2.ReadJsonAsync()).GetProperty("id").GetInt64();
        var done2 = await client.WaitForRunAsync(id2, "success", "failed");
        Assert.Equal("success", done2.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pipeline_trigger_with_skip_deploy_succeeds_and_deployer_never_called()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/pipelines/{seeded.Pipeline}/runs",
            new { skipSteps = new[] { "deploy" } });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.ReadJsonAsync();
        var runId = accepted.GetProperty("id").GetInt64();

        var finished = await client.WaitForRunAsync(runId, "success", "failed");
        Assert.Equal("success", finished.GetProperty("status").GetString());
        var steps = finished.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal("success", steps.Single(s => s.GetProperty("name").GetString() == "checkout").GetProperty("status").GetString());
        Assert.Equal("success", steps.Single(s => s.GetProperty("name").GetString() == "build").GetProperty("status").GetString());
        Assert.Equal("success", steps.Single(s => s.GetProperty("name").GetString() == "push").GetProperty("status").GetString());
        Assert.Equal("skipped", steps.Single(s => s.GetProperty("name").GetString() == "deploy").GetProperty("status").GetString());

        var deployer = Assert.IsType<FakeK8sDeployer>(factory.Services.GetRequiredService<IK8sDeployer>());
        Assert.Empty(deployer.Requests);
    }

    [Fact]
    public async Task Pipeline_trigger_with_skip_push_and_deploy_succeeds()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/pipelines/{seeded.Pipeline}/runs",
            new { skipSteps = new[] { "push", "deploy" } });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var runId = (await response.ReadJsonAsync()).GetProperty("id").GetInt64();

        var finished = await client.WaitForRunAsync(runId, "success", "failed");
        Assert.Equal("success", finished.GetProperty("status").GetString());
        var steps = finished.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal("success", steps.Single(s => s.GetProperty("name").GetString() == "checkout").GetProperty("status").GetString());
        Assert.Equal("success", steps.Single(s => s.GetProperty("name").GetString() == "build").GetProperty("status").GetString());
        Assert.Equal("skipped", steps.Single(s => s.GetProperty("name").GetString() == "push").GetProperty("status").GetString());
        Assert.Equal("skipped", steps.Single(s => s.GetProperty("name").GetString() == "deploy").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(new object[] { new[] { "unknown" } })]
    [InlineData(new object[] { new[] { "deploy", "deploy" } })]
    [InlineData(new object[] { new[] { "checkout" } })]
    [InlineData(new object[] { new[] { "build" } })]
    [InlineData(new object[] { new[] { "build", "deploy" } })]
    public async Task Pipeline_trigger_rejects_invalid_skipSteps_with_400(string[] invalid)
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/pipelines/{seeded.Pipeline}/runs",
            new { skipSteps = invalid });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.True(problem.TryGetProperty("detail", out var detail));
        Assert.False(string.IsNullOrWhiteSpace(detail.GetString()));
    }

    [Fact]
    public async Task Pipeline_trigger_rejects_malformed_json_with_400()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        using var content = new StringContent("{bad json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/pipelines/{seeded.Pipeline}/runs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Job_trigger_supports_skip_runjob_and_rejects_invalid_skipSteps()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        // Create a job
        var createRes = await client.PostAsJsonAsync("/api/jobs", new
        {
            name = "skip-test-job",
            gitUrl = "https://git.example.com/team/job.git",
            gitBranch = "main",
            dockerfilePath = "Dockerfile",
            buildContext = ".",
            imageName = "skip-test-job",
            registryHost = "registry.example.com",
            slaveId = seeded.Slave,
            gitCredentialId = seeded.GitCred,
            registryCredentialId = seeded.RegCred,
            @namespace = "devops",
            containerName = "app",
            tasks = new[]
            {
                new { position = 0, label = "step-1", command = new[] { "echo" }, args = new[] { "hi" }, timeoutSeconds = 60 }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var jobId = (await createRes.ReadJsonAsync()).GetProperty("id").GetInt64();

        // Invalid: pipeline step 'deploy' on a job -> 400
        var bad1 = await client.PostAsJsonAsync($"/api/jobs/{jobId}/runs", new { skipSteps = new[] { "deploy" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad1.StatusCode);

        // Invalid: non-suffix 'build' -> 400
        var bad2 = await client.PostAsJsonAsync($"/api/jobs/{jobId}/runs", new { skipSteps = new[] { "build" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad2.StatusCode);

        // Valid: skip runjob -> 202 and succeeds with tasks skipped
        var okRes = await client.PostAsJsonAsync($"/api/jobs/{jobId}/runs", new { skipSteps = new[] { "runjob" } });
        Assert.Equal(HttpStatusCode.Accepted, okRes.StatusCode);
        var runId = (await okRes.ReadJsonAsync()).GetProperty("id").GetInt64();

        var finished = await client.WaitForRunAsync(runId, "success", "failed");
        Assert.Equal("success", finished.GetProperty("status").GetString());
        var steps = finished.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal("skipped", steps.Single(s => s.GetProperty("name").GetString() == "runjob").GetProperty("status").GetString());
        var tasks = finished.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Single(tasks);
        Assert.Equal("skipped", tasks[0].GetProperty("status").GetString());
    }
}
