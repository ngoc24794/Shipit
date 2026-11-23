using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Core;
using Shipit.Ssh;

namespace Shipit.Api.Tests;

public sealed class TriggerBranchApiTests
{
    [Fact]
    public async Task Pipeline_trigger_with_gitBranch_overrides_default_and_records_on_run()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/pipelines/{seeded.Pipeline}/runs",
            new { gitBranch = "release/v1.2.3" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.ReadJsonAsync();
        Assert.Equal("release/v1.2.3", accepted.GetProperty("gitBranch").GetString());

        var runId = accepted.GetProperty("id").GetInt64();
        var finished = await client.WaitForRunAsync(runId, "success", "failed");
        Assert.Equal("success", finished.GetProperty("status").GetString());
        Assert.Equal("release/v1.2.3", finished.GetProperty("gitBranch").GetString());

        var ssh = Assert.IsType<FakeSshExecutor>(factory.Services.GetRequiredService<ISshExecutor>());
        Assert.Contains(ssh.ExecutedCommands, c => c.CommandText.Contains("--branch 'release/v1.2.3'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Job_trigger_with_git_tag_succeeds_and_records_on_run()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var jobRes = await client.PostAsJsonAsync("/api/jobs", new
        {
            name = "tag-job",
            gitUrl = "https://github.com/org/repo.git",
            gitBranch = "main",
            dockerfilePath = "Dockerfile",
            buildContext = ".",
            gitCredentialId = seeded.GitCred,
            registryCredentialId = seeded.RegCred,
            slaveId = seeded.Slave,
            imageName = "tag-job-img",
            registryHost = "registry.local:5000",
            @namespace = "devops",
            containerName = "app",
            tasks = new[] { new { label = "migrate", args = new[] { "migrate" }, timeoutSeconds = 60 } }
        });
        Assert.Equal(HttpStatusCode.Created, jobRes.StatusCode);
        var jobId = (await jobRes.ReadJsonAsync()).GetProperty("id").GetInt64();

        var runRes = await client.PostAsJsonAsync(
            $"/api/jobs/{jobId}/runs",
            new { gitBranch = "refs/tags/v2.0.0" });

        Assert.Equal(HttpStatusCode.Accepted, runRes.StatusCode);
        var accepted = await runRes.ReadJsonAsync();
        Assert.Equal("refs/tags/v2.0.0", accepted.GetProperty("gitBranch").GetString());

        var runId = accepted.GetProperty("id").GetInt64();
        var finished = await client.WaitForRunAsync(runId, "success", "failed");
        Assert.Equal("success", finished.GetProperty("status").GetString());
        Assert.Equal("refs/tags/v2.0.0", finished.GetProperty("gitBranch").GetString());

        var ssh = Assert.IsType<FakeSshExecutor>(factory.Services.GetRequiredService<ISshExecutor>());
        Assert.Contains(ssh.ExecutedCommands, c => c.CommandText.Contains("'refs/tags/v2.0.0'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("; rm -rf /")]
    [InlineData("-bad-branch")]
    [InlineData("feat..bad")]
    public async Task Trigger_with_invalid_gitBranch_returns_400_with_field_error(string invalidBranch)
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/pipelines/{seeded.Pipeline}/runs",
            new { gitBranch = invalidBranch });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty("gitBranch", out _));
    }

    [Fact]
    public async Task Trigger_without_gitBranch_uses_pipeline_default_branch()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var response = await client.PostAsJsonAsync($"/api/pipelines/{seeded.Pipeline}/runs", new { });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.ReadJsonAsync();
        Assert.Equal("main", accepted.GetProperty("gitBranch").GetString());
    }
}
