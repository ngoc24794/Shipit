using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Shipit.E2E.Tests;

public static class HttpTestClientExtensions
{
    public static async Task<HttpClient> LoggedInClientAsync(
        this WebApplicationFactory<Program> factory,
        string username = E2EFactory.AdminUser,
        string password = E2EFactory.AdminPassword)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var element = await response.Content.ReadFromJsonAsync<JsonElement>();
        return element.Clone();
    }

    public static async Task<long> CreateAsync(this HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"POST {url} returned {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("id").GetInt64();
    }

    public static async Task<(long PipelineId, long SlaveId, long SshId, long GitId, long RegId)> SeedResourcesAsync(
        this HttpClient client,
        string deployment = "shipit-demo",
        string deployNamespace = "devops")
    {
        var sshId = await client.CreateAsync("/api/credentials", new
        {
            name = "e2e-ssh-key",
            kind = "ssh_key",
            value = E2EFactory.SshKey
        });

        var gitId = await client.CreateAsync("/api/credentials", new
        {
            name = "e2e-git-token",
            kind = "git_token",
            value = E2EFactory.GitToken
        });

        var regId = await client.CreateAsync("/api/credentials", new
        {
            name = "e2e-registry-pass",
            kind = "registry",
            username = "e2e-user",
            value = E2EFactory.RegistryPassword
        });

        var slaveId = await client.CreateAsync("/api/slaves", new
        {
            name = "e2e-slave-1",
            host = "192.168.1.49",
            port = 22,
            user = "devops",
            sshCredentialId = sshId
        });

        var pipelineId = await client.CreateAsync("/api/pipelines", new
        {
            name = "e2e-pipeline",
            gitUrl = "https://git.example.com/team/demo.git",
            gitBranch = "main",
            gitCredentialId = gitId,
            slaveId = slaveId,
            dockerfilePath = "Dockerfile",
            imageName = "shipit-demo",
            registryHost = "192.168.1.241",
            registryCredentialId = regId,
            deployNamespace = deployNamespace,
            deployDeployment = deployment,
            deployContainer = "app"
        });

        return (pipelineId, slaveId, sshId, gitId, regId);
    }

    public static async Task<long> TriggerRunAsync(this HttpClient client, long pipelineId)
    {
        var response = await client.PostAsync($"/api/pipelines/{pipelineId}/runs", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.ReadJsonAsync();
        return json.GetProperty("id").GetInt64();
    }

    public static async Task<JsonElement> WaitForRunAsync(
        this HttpClient client,
        long runId,
        params string[] targetStatuses)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/runs/{runId}");
            if (response.IsSuccessStatusCode)
            {
                var run = await response.ReadJsonAsync();
                var status = run.GetProperty("status").GetString();
                if (targetStatuses.Contains(status))
                {
                    return run;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Run {runId} did not reach any of [{string.Join(", ", targetStatuses)}] within 30 seconds.");
    }
}
