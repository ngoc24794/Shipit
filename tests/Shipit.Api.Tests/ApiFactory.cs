using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Shipit.Api.Tests;

/// <summary>Runs the real app in fake mode (SQLite file, fake SSH and cluster) with a throw-away database.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "test-password";
    public const string SshKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nAAAAFAKEAPIKEYLINE1abcdef\nAAAAFAKEAPIKEYLINE2ghijkl\n-----END OPENSSH PRIVATE KEY-----";
    public const string GitToken = "ghp_TESTTOKEN_api_12345";
    public const string RegistryPassword = "reg-pass-api-67890";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shipit-api-{Guid.NewGuid():N}.db");
    private readonly IReadOnlyDictionary<string, string> _extraSettings;

    public ApiFactory(IReadOnlyDictionary<string, string>? extraSettings = null)
    {
        _extraSettings = extraSettings ?? new Dictionary<string, string>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Shipit:UseFakes", "true");
        builder.UseSetting(Shipit.Api.ShipitHost.SqlitePathKey, _dbPath);
        builder.UseSetting("SHIPIT_ADMIN_USER", "admin");
        builder.UseSetting("SHIPIT_ADMIN_PASSWORD", AdminPassword);
        builder.UseSetting("SHIPIT_DEPLOY_ALLOWLIST", "{\"devops\":[\"shipit-demo\"],\"avina-workspace\":[\"avina-catalog\"]}");
        foreach (var (key, value) in _extraSettings)
        {
            builder.UseSetting(key, value);
        }
    }

    public static readonly string[] Secrets = [GitToken, RegistryPassword, "AAAAFAKEAPIKEYLINE1abcdef", "AAAAFAKEAPIKEYLINE2ghijkl"];

    /// <summary>An HttpClient that keeps cookies, already logged in as admin.</summary>
    public async Task<HttpClient> LoggedInClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Temp file; best effort.
            }
        }
    }
}

internal static class ApiTestHelpers
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    public static async Task<long> CreateAsync(this HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"POST {url} returned {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetInt64();
    }

    /// <summary>Creates the three credentials, a slave and a pipeline that the fake deploy allowlist accepts.</summary>
    public static async Task<(long Pipeline, long Slave, long SshCred, long GitCred, long RegCred)> SeedAsync(
        this HttpClient client, string deployment = "shipit-demo")
    {
        var ssh = await client.CreateAsync("/api/credentials", new { name = "slave-key", kind = "ssh_key", value = ApiFactory.SshKey });
        var git = await client.CreateAsync("/api/credentials", new { name = "git", kind = "git_token", value = ApiFactory.GitToken });
        var reg = await client.CreateAsync("/api/credentials", new { name = "registry", kind = "registry", username = "ci", value = ApiFactory.RegistryPassword });
        var slave = await client.CreateAsync("/api/slaves", new { name = "build-1", host = "build.example.com", port = 22, user = "devops", sshCredentialId = ssh });
        var pipeline = await client.CreateAsync("/api/pipelines", new
        {
            name = "demo",
            gitUrl = "https://git.example.com/team/demo.git",
            gitBranch = "main",
            gitCredentialId = git,
            slaveId = slave,
            dockerfilePath = "Dockerfile",
            imageName = "shipit-demo",
            registryHost = "registry.example.com",
            registryCredentialId = reg,
            deployNamespace = "devops",
            deployDeployment = deployment,
            deployContainer = "app",
        });
        return (pipeline, slave, ssh, git, reg);
    }

    public static async Task<JsonElement> WaitForRunAsync(this HttpClient client, long runId, params string[] statuses)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var run = await (await client.GetAsync($"/api/runs/{runId}")).ReadJsonAsync();
            if (statuses.Contains(run.GetProperty("status").GetString()))
            {
                return run;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Run {runId} did not reach {string.Join('/', statuses)} in time.");
    }

    public static async Task<long> TriggerAsync(this HttpClient client, long pipelineId)
    {
        var response = await client.PostAsync($"/api/pipelines/{pipelineId}/runs", content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt64();
    }
}
