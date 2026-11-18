using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api.Tests;

public class SearchEndpointsTests
{
    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        using var factory = new ApiFactory();
        var anon = factory.CreateClient();
        var resp = await anon.GetAsync("/api/search?q=demo");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Empty_or_whitespace_q_returns_empty_groups()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        var resp = await client.GetAsync("/api/search?q=%20%20");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.ReadJsonAsync();
        Assert.Equal(string.Empty, json.GetProperty("query").GetString());
        Assert.Equal(0, json.GetProperty("groups").GetArrayLength());
    }

    [Fact]
    public async Task Searches_all_groups_in_fixed_order_with_spa_paths_and_no_secrets()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        const string fakeSecret = "top-secret-alpha-value-999";
        var sshCred = await client.CreateAsync("/api/credentials", new { name = "alpha-ssh", kind = "ssh_key", value = ApiFactory.SshKey });
        var gitCred = await client.CreateAsync("/api/credentials", new { name = "alpha-git", kind = "git_token", value = fakeSecret });
        var regCred = await client.CreateAsync("/api/credentials", new { name = "reg-cred", kind = "registry", username = "ci", value = ApiFactory.RegistryPassword });
        var slave = await client.CreateAsync("/api/slaves", new { name = "alpha-slave", host = "alpha.example.com", port = 22, user = "devops", sshCredentialId = sshCred });
        var deployTarget = await client.CreateAsync("/api/deploy-targets", new { @namespace = "alpha-ns", deployment = "alpha-app" });
        var jobAllow = await client.CreateAsync("/api/job-allow-entries", new { @namespace = "alpha-ns", kind = "configmap", name = "alpha-cm" });

        var pipeline = await client.CreateAsync("/api/pipelines", new
        {
            name = "alpha-pipeline",
            gitUrl = "https://git.example.com/team/alpha.git",
            gitBranch = "main",
            gitCredentialId = gitCred,
            slaveId = slave,
            dockerfilePath = "Dockerfile",
            imageName = "alpha-img",
            registryHost = "registry.example.com",
            registryCredentialId = regCred,
            deployNamespace = "alpha-ns",
            deployDeployment = "alpha-app",
            deployContainer = "app",
        });

        var job = await client.CreateAsync("/api/jobs", new
        {
            name = "alpha-job",
            gitUrl = "https://git.example.com/team/alpha-job.git",
            gitBranch = "main",
            gitCredentialId = gitCred,
            slaveId = slave,
            dockerfilePath = "Dockerfile",
            imageName = "alpha-job-img",
            registryHost = "registry.example.com",
            registryCredentialId = regCred,
            @namespace = "devops",
            containerName = "job",
            tasks = new[]
            {
                new { label = "run", args = new[] { "echo" }, timeoutSeconds = 60, backoffLimit = 0, ttlSeconds = 300 },
            },
        });

        long runId;
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContextAsync())
        {
            var run = new Run
            {
                PipelineId = pipeline,
                Status = RunStatus.Success,
                Tag = "alpha-v1",
                Image = "registry.example.com/alpha-img:alpha-v1",
                CreatedAt = DateTime.UtcNow,
            };
            db.Runs.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var resp = await client.GetAsync("/api/search?q=ALPHA&limit=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var raw = await resp.Content.ReadAsStringAsync();

        // Never leak secrets
        Assert.DoesNotContain(fakeSecret, raw);
        Assert.DoesNotContain("ProtectedValue", raw, StringComparison.OrdinalIgnoreCase);
        foreach (var secret in ApiFactory.Secrets)
        {
            Assert.DoesNotContain(secret, raw);
        }

        var json = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("ALPHA", json.GetProperty("query").GetString());
        var groups = json.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(
            ["run", "pipeline", "job", "slave", "credential", "deploy-target", "job-allow"],
            groups.Select(g => g.GetProperty("kind").GetString()));

        Assert.Equal($"/runs/{runId}", groups[0].GetProperty("items")[0].GetProperty("path").GetString());
        Assert.Equal($"/pipelines/{pipeline}/edit", groups[1].GetProperty("items")[0].GetProperty("path").GetString());
        Assert.Equal($"/jobs/{job}", groups[2].GetProperty("items")[0].GetProperty("path").GetString());
        Assert.Equal($"/slaves/{slave}/edit", groups[3].GetProperty("items")[0].GetProperty("path").GetString());

        var credItems = groups[4].GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, credItems.Count);
        Assert.Contains(credItems, c => c.GetProperty("title").GetString() == "alpha-git" && c.GetProperty("subtitle").GetString() == "git_token" && c.GetProperty("path").GetString() == $"/credentials/{gitCred}/edit");

        Assert.Equal("/settings/allowlist?tab=deployment", groups[5].GetProperty("items")[0].GetProperty("path").GetString());
        Assert.Equal("/settings/allowlist?tab=job", groups[6].GetProperty("items")[0].GetProperty("path").GetString());
        _ = deployTarget;
        _ = jobAllow;
    }

    [Fact]
    public async Task Omits_empty_groups_respects_limit_and_escapes_like()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        await client.CreateAsync("/api/credentials", new { name = "cred_100%_a", kind = "git_token", value = "secret-1" });
        await client.CreateAsync("/api/credentials", new { name = "cred_100%_b", kind = "git_token", value = "secret-2" });
        await client.CreateAsync("/api/credentials", new { name = "cred_100%_c", kind = "git_token", value = "secret-3" });
        await client.CreateAsync("/api/credentials", new { name = "credX1000Xa", kind = "git_token", value = "secret-4" });

        var resp = await client.GetAsync("/api/search?q=" + Uri.EscapeDataString("100%_") + "&limit=2");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.ReadJsonAsync();
        var groups = json.GetProperty("groups").EnumerateArray().ToList();

        // Only credential group matched -> empty groups omitted
        Assert.Single(groups);
        Assert.Equal("credential", groups[0].GetProperty("kind").GetString());
        var items = groups[0].GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Contains("100%_", i.GetProperty("title").GetString()));
    }
}
