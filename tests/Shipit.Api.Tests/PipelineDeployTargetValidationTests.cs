using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Shipit.Api.Tests;

public class PipelineDeployTargetValidationTests
{
    [Fact]
    public async Task Pipeline_Requires_DeployTarget_And_Allows_Immediately_After_Target_Added()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var pipeBody = new Dictionary<string, object?>
        {
            ["name"] = "new-dynamic-pipe",
            ["gitUrl"] = "https://git.example.com/team/demo.git",
            ["gitBranch"] = "main",
            ["gitCredentialId"] = seeded.GitCred,
            ["slaveId"] = seeded.Slave,
            ["dockerfilePath"] = "Dockerfile",
            ["imageName"] = "shipit-demo",
            ["registryHost"] = "registry.example.com",
            ["deployNamespace"] = "staging",
            ["deployDeployment"] = "unregistered-app",
            ["deployContainer"] = "app"
        };

        // 1. Try to create pipeline with deployment not in allowlist -> 400 Bad Request with field error on deployDeployment
        var createFailResp = await client.PostAsJsonAsync("/api/pipelines", pipeBody);
        Assert.Equal(HttpStatusCode.BadRequest, createFailResp.StatusCode);
        var errors = (await createFailResp.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("deployDeployment", out var msgProp));
        var msg = msgProp[0].GetString()!;
        Assert.DoesNotContain("SHIPIT_DEPLOY_ALLOWLIST", msg);
        Assert.Contains("Allowed Deployments", msg);

        // 2. Add target to allowlist via API
        var addTargetResp = await client.PostAsJsonAsync("/api/deploy-targets", new
        {
            @namespace = "staging",
            deployment = "unregistered-app"
        });
        Assert.Equal(HttpStatusCode.Created, addTargetResp.StatusCode);

        // 3. Immediately try creating pipeline again without restart -> 201 Created!
        var createSuccessResp = await client.PostAsJsonAsync("/api/pipelines", pipeBody);
        Assert.Equal(HttpStatusCode.Created, createSuccessResp.StatusCode);
        var created = await createSuccessResp.ReadJsonAsync();
        Assert.Equal("new-dynamic-pipe", created.GetProperty("name").GetString());
    }
}
