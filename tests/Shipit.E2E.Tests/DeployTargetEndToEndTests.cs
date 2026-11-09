using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Data;

namespace Shipit.E2E.Tests;

public class DeployTargetEndToEndTests
{
    [Fact]
    public async Task AddTarget_CreatePipeline_RunSucceeds_RemoveTargetFromDb_RunFailsAtDeploy()
    {
        // Start with empty allowlist so we test dynamic target addition
        using var factory = new E2EFactory(extraSettings: new Dictionary<string, string>
        {
            [Shipit.Core.ShipitOptions.AllowListVar] = ""
        });
        var client = await factory.LoggedInClientAsync();

        // 1. Create credentials and slave
        var sshId = await client.CreateAsync("/api/credentials", new
        {
            name = "e2e-dt-ssh",
            kind = "ssh_key",
            value = E2EFactory.SshKey
        });
        var gitId = await client.CreateAsync("/api/credentials", new
        {
            name = "e2e-dt-git",
            kind = "git_token",
            value = E2EFactory.GitToken
        });
        var regId = await client.CreateAsync("/api/credentials", new
        {
            name = "e2e-dt-reg",
            kind = "registry",
            username = "ci",
            value = E2EFactory.RegistryPassword
        });
        var slaveId = await client.CreateAsync("/api/slaves", new
        {
            name = "e2e-dt-slave",
            host = "192.168.1.55",
            port = 22,
            user = "devops",
            sshCredentialId = sshId
        });

        const string ns = "staging";
        const string deployment = "order-service";

        // 2. Add target via POST /api/deploy-targets
        var addTargetResp = await client.PostAsJsonAsync("/api/deploy-targets", new
        {
            @namespace = ns,
            deployment = deployment
        });
        Assert.Equal(HttpStatusCode.Created, addTargetResp.StatusCode);

        // 3. Create pipeline using the added target
        var pipelineId = await client.CreateAsync("/api/pipelines", new
        {
            name = "e2e-dt-pipeline",
            gitUrl = "https://git.example.com/team/order.git",
            gitBranch = "main",
            gitCredentialId = gitId,
            slaveId = slaveId,
            dockerfilePath = "Dockerfile",
            imageName = "order-service",
            registryHost = "192.168.1.241",
            registryCredentialId = regId,
            deployNamespace = ns,
            deployDeployment = deployment,
            deployContainer = "app"
        });

        // 4. Trigger first run -> must succeed end-to-end
        var runId1 = await client.TriggerRunAsync(pipelineId);
        var run1 = await client.WaitForRunAsync(runId1, "success", "failed");
        Assert.Equal("success", run1.GetProperty("status").GetString());

        var steps1 = run1.GetProperty("steps").EnumerateArray().ToDictionary(
            s => s.GetProperty("name").GetString()!,
            s => s.GetProperty("status").GetString()!);
        Assert.Equal("success", steps1["deploy"]);

        // 5. Remove target directly from DB (defense-in-depth simulation)
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContextAsync())
        {
            await db.DeployTargets.Where(t => t.Namespace == ns && t.Deployment == deployment).ExecuteDeleteAsync();
        }

        // 6. Trigger second run -> must fail at deploy step because target was removed from allowlist
        var runId2 = await client.TriggerRunAsync(pipelineId);
        var run2 = await client.WaitForRunAsync(runId2, "failed", "success");
        Assert.Equal("failed", run2.GetProperty("status").GetString());

        var steps2 = run2.GetProperty("steps").EnumerateArray().ToDictionary(
            s => s.GetProperty("name").GetString()!,
            s => s.GetProperty("status").GetString()!);
        Assert.Equal("success", steps2["checkout"]);
        Assert.Equal("success", steps2["build"]);
        Assert.Equal("success", steps2["push"]);
        Assert.Equal("failed", steps2["deploy"]);

        var failureReason = run2.GetProperty("failureReason").GetString();
        Assert.NotNull(failureReason);
        Assert.Contains("not allowed", failureReason, StringComparison.OrdinalIgnoreCase);
    }
}
