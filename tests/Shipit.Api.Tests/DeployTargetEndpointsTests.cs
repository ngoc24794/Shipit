using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Data;

namespace Shipit.Api.Tests;

public class DeployTargetEndpointsTests
{
    [Fact]
    public async Task Anonymous_Requests_Return_401()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var getResp = await client.GetAsync("/api/deploy-targets");
        Assert.Equal(HttpStatusCode.Unauthorized, getResp.StatusCode);

        var postResp = await client.PostAsJsonAsync("/api/deploy-targets", new { @namespace = "devops", deployment = "app" });
        Assert.Equal(HttpStatusCode.Unauthorized, postResp.StatusCode);

        var delResp = await client.DeleteAsync("/api/deploy-targets/1");
        Assert.Equal(HttpStatusCode.Unauthorized, delResp.StatusCode);
    }

    [Fact]
    public async Task Can_Create_List_And_Delete_DeployTarget()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            [Shipit.Core.ShipitOptions.AllowListVar] = "" // start empty
        });
        var client = await factory.LoggedInClientAsync();

        // 1. Initially empty (since allowlist was empty)
        var listResp1 = await client.GetAsync("/api/deploy-targets");
        Assert.Equal(HttpStatusCode.OK, listResp1.StatusCode);
        var list1 = await listResp1.ReadJsonAsync();
        Assert.Equal(0, list1.GetArrayLength());

        // 2. Create target "devops" / "web"
        var createResp = await client.PostAsJsonAsync("/api/deploy-targets", new
        {
            @namespace = "devops",
            deployment = "web"
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.ReadJsonAsync();
        Assert.Equal("devops", created.GetProperty("namespace").GetString());
        Assert.Equal("web", created.GetProperty("deployment").GetString());
        var id = created.GetProperty("id").GetInt64();
        Assert.True(id > 0);

        // 3. Create another target "alpha" / "api"
        var createResp2 = await client.PostAsJsonAsync("/api/deploy-targets", new
        {
            @namespace = "alpha",
            deployment = "api"
        });
        Assert.Equal(HttpStatusCode.Created, createResp2.StatusCode);

        // 4. List targets: sorted by namespace, then deployment
        var listResp2 = await client.GetAsync("/api/deploy-targets");
        var list2 = await listResp2.ReadJsonAsync();
        Assert.Equal(2, list2.GetArrayLength());
        Assert.Equal("alpha", list2[0].GetProperty("namespace").GetString());
        Assert.Equal("devops", list2[1].GetProperty("namespace").GetString());

        // 5. Delete target
        var delResp = await client.DeleteAsync($"/api/deploy-targets/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // 6. Delete again -> 404
        var delResp2 = await client.DeleteAsync($"/api/deploy-targets/{id}");
        Assert.Equal(HttpStatusCode.NotFound, delResp2.StatusCode);
    }

    [Fact]
    public async Task Duplicate_Target_Returns_409_Conflict()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        var create1 = await client.PostAsJsonAsync("/api/deploy-targets", new
        {
            @namespace = "custom-ns",
            deployment = "custom-dep"
        });
        Assert.Equal(HttpStatusCode.Created, create1.StatusCode);

        var create2 = await client.PostAsJsonAsync("/api/deploy-targets", new
        {
            @namespace = "custom-ns",
            deployment = "custom-dep"
        });
        Assert.Equal(HttpStatusCode.Conflict, create2.StatusCode);
    }

    [Fact]
    public async Task Invalid_Names_Return_400_With_Field_Errors()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        // Uppercase, punctuation, too long, leading hyphen, trailing hyphen
        var badNamespaces = new[] { "Devops", "dev_ops", "-bad", "bad-", new string('a', 64), "" };
        foreach (var badNs in badNamespaces)
        {
            var resp = await client.PostAsJsonAsync("/api/deploy-targets", new
            {
                @namespace = badNs,
                deployment = "good-deployment"
            });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var json = await resp.ReadJsonAsync();
            Assert.True(json.GetProperty("errors").TryGetProperty("namespace", out _), $"Expected namespace error for '{badNs}'");
        }

        var badDeployments = new[] { "App", "app_name", "-dep", "dep-", new string('a', 254), "" };
        foreach (var badDep in badDeployments)
        {
            var resp = await client.PostAsJsonAsync("/api/deploy-targets", new
            {
                @namespace = "devops",
                deployment = badDep
            });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var json = await resp.ReadJsonAsync();
            Assert.True(json.GetProperty("errors").TryGetProperty("deployment", out _), $"Expected deployment error for '{badDep}'");
        }
    }

    [Fact]
    public async Task Deleting_Target_In_Use_By_Pipeline_Returns_409_And_Lists_Pipeline_Names()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        // seeded.Pipeline uses devops/shipit-demo.
        // Find the target id for devops/shipit-demo:
        var listResp = await client.GetAsync("/api/deploy-targets");
        var list = await listResp.ReadJsonAsync();
        var target = list.EnumerateArray()
            .First(t => t.GetProperty("namespace").GetString() == "devops" &&
                        t.GetProperty("deployment").GetString() == "shipit-demo");
        var targetId = target.GetProperty("id").GetInt64();

        // Attempting to delete target in use -> 409 Conflict
        var delResp = await client.DeleteAsync($"/api/deploy-targets/{targetId}");
        Assert.Equal(HttpStatusCode.Conflict, delResp.StatusCode);
        var conflict = await delResp.ReadJsonAsync();
        var detail = conflict.GetProperty("detail").GetString()!;
        Assert.Contains("demo", detail); // pipeline name is "demo"

        // Delete the pipeline first
        var delPipeResp = await client.DeleteAsync($"/api/pipelines/{seeded.Pipeline}");
        Assert.Equal(HttpStatusCode.NoContent, delPipeResp.StatusCode);

        // Now delete target succeeds
        var delTargetResp = await client.DeleteAsync($"/api/deploy-targets/{targetId}");
        Assert.Equal(HttpStatusCode.NoContent, delTargetResp.StatusCode);
    }
}
