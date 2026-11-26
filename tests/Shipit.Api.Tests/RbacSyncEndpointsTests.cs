using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Core;
using Shipit.K8s;

namespace Shipit.Api.Tests;

public class RbacSyncEndpointsTests
{
    private static FakeK8sRbacSync GetFakeRbacSync(ApiFactory factory) =>
        Assert.IsType<FakeK8sRbacSync>(factory.Services.GetRequiredService<IK8sRbacSync>());

    [Fact]
    public async Task Adding_And_Deleting_DeployTargets_Triggers_RbacSync_With_Exact_DeploymentList()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            [ShipitOptions.AllowListVar] = ""
        });
        var client = await factory.LoggedInClientAsync();
        var fakeRbac = GetFakeRbacSync(factory);
        fakeRbac.Reset();

        // 1. Add first target in ns-a
        var resp1 = await client.PostAsJsonAsync("/api/deploy-targets", new { @namespace = "ns-a", deployment = "web" });
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
        Assert.False(resp1.Headers.Contains(RbacSyncService.WarningHeaderName));
        var id1 = (await resp1.ReadJsonAsync()).GetProperty("id").GetInt64();

        var lastCall = fakeRbac.Calls.Last();
        Assert.Equal("ns-a", lastCall.Namespace);
        Assert.Equal(["web"], lastCall.AllowedDeployments);
        Assert.False(lastCall.AllowJobs);

        // 2. Add second target in ns-a -> sorted list ["api", "web"]
        var resp2 = await client.PostAsJsonAsync("/api/deploy-targets", new { @namespace = "ns-a", deployment = "api" });
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
        var id2 = (await resp2.ReadJsonAsync()).GetProperty("id").GetInt64();

        lastCall = fakeRbac.Calls.Last();
        Assert.Equal("ns-a", lastCall.Namespace);
        Assert.Equal(["api", "web"], lastCall.AllowedDeployments);
        Assert.False(lastCall.AllowJobs);

        // 3. Delete first target -> ["api"]
        var del1 = await client.DeleteAsync($"/api/deploy-targets/{id1}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);
        lastCall = fakeRbac.Calls.Last();
        Assert.Equal("ns-a", lastCall.Namespace);
        Assert.Equal(["api"], lastCall.AllowedDeployments);
        Assert.False(lastCall.AllowJobs);
        Assert.True(fakeRbac.SyncedNamespaces.ContainsKey("ns-a"));

        // 4. Delete last target -> [] and removed from SyncedNamespaces
        var del2 = await client.DeleteAsync($"/api/deploy-targets/{id2}");
        Assert.Equal(HttpStatusCode.NoContent, del2.StatusCode);
        lastCall = fakeRbac.Calls.Last();
        Assert.Equal("ns-a", lastCall.Namespace);
        Assert.Empty(lastCall.AllowedDeployments);
        Assert.False(lastCall.AllowJobs);
        Assert.False(fakeRbac.SyncedNamespaces.ContainsKey("ns-a"));
    }

    [Fact]
    public async Task Adding_And_Deleting_JobAllowEntries_Triggers_RbacSync_Only_For_Namespace_Kind()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            [ShipitOptions.AllowListVar] = ""
        });
        var client = await factory.LoggedInClientAsync();
        var fakeRbac = GetFakeRbacSync(factory);
        fakeRbac.Reset();

        // 1. Non-namespace entry (configmap) should NOT trigger RBAC sync
        var cmResp = await client.PostAsJsonAsync("/api/job-allow-entries", new
        {
            @namespace = "jobs-ns",
            kind = "configmap",
            name = "app-config"
        });
        Assert.Equal(HttpStatusCode.Created, cmResp.StatusCode);
        var cmId = (await cmResp.ReadJsonAsync()).GetProperty("id").GetInt64();
        Assert.Empty(fakeRbac.Calls);

        // 2. Namespace entry SHOULD trigger RBAC sync with allowJobs = true
        var nsResp = await client.PostAsJsonAsync("/api/job-allow-entries", new
        {
            @namespace = "jobs-ns",
            kind = "namespace",
            name = "jobs-ns"
        });
        Assert.Equal(HttpStatusCode.Created, nsResp.StatusCode);
        var nsId = (await nsResp.ReadJsonAsync()).GetProperty("id").GetInt64();
        var call = Assert.Single(fakeRbac.Calls);
        Assert.Equal("jobs-ns", call.Namespace);
        Assert.Empty(call.AllowedDeployments);
        Assert.True(call.AllowJobs);

        // 3. Deleting configmap entry should NOT trigger RBAC sync
        var delCm = await client.DeleteAsync($"/api/job-allow-entries/{cmId}");
        Assert.Equal(HttpStatusCode.NoContent, delCm.StatusCode);
        Assert.Single(fakeRbac.Calls);

        // 4. Deleting namespace entry SHOULD trigger RBAC sync with allowJobs = false
        var delNs = await client.DeleteAsync($"/api/job-allow-entries/{nsId}");
        Assert.Equal(HttpStatusCode.NoContent, delNs.StatusCode);
        Assert.Equal(2, fakeRbac.Calls.Count);
        var lastCall = fakeRbac.Calls.Last();
        Assert.Equal("jobs-ns", lastCall.Namespace);
        Assert.False(lastCall.AllowJobs);
    }

    [Fact]
    public async Task RbacSyncFailure_Sets_WarningHeader_While_Preserving_DbMutation()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            [ShipitOptions.AllowListVar] = ""
        });
        var client = await factory.LoggedInClientAsync();
        var fakeRbac = GetFakeRbacSync(factory);
        fakeRbac.ConfiguredResult = new RbacSyncResult(
            false,
            "Kubernetes API error: HTTP 403 (Forbidden)\nRole forbidden");

        var createResp = await client.PostAsJsonAsync("/api/deploy-targets", new
        {
            @namespace = "restricted-ns",
            deployment = "web"
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        Assert.True(createResp.Headers.TryGetValues(RbacSyncService.WarningHeaderName, out var warnValues));
        var warning = Assert.Single(warnValues);
        Assert.Contains("restricted-ns", warning);
        Assert.Contains("403", warning);
        Assert.DoesNotContain("\n", warning);
        Assert.DoesNotContain("\r", warning);

        // DB mutation was preserved
        var id = (await createResp.ReadJsonAsync()).GetProperty("id").GetInt64();
        var list = await (await client.GetAsync("/api/deploy-targets")).ReadJsonAsync();
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("id").GetInt64() == id);

        // Delete also succeeds and returns warning header
        var delResp = await client.DeleteAsync($"/api/deploy-targets/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);
        Assert.True(delResp.Headers.Contains(RbacSyncService.WarningHeaderName));
    }

    [Fact]
    public async Task Post_Rbac_Sync_RequiresAuth_And_ReconcilesAllNamespaces()
    {
        using var factory = new ApiFactory();
        var anon = factory.CreateClient();
        var anonResp = await anon.PostAsync("/api/rbac/sync", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

        var client = await factory.LoggedInClientAsync();
        var fakeRbac = GetFakeRbacSync(factory);

        // Add a job namespace entry for avina-workspace (which also has a seeded deploy target)
        var jobAllowResp = await client.PostAsJsonAsync("/api/job-allow-entries", new
        {
            @namespace = "avina-workspace",
            kind = "namespace",
            name = "avina-workspace"
        });
        Assert.Equal(HttpStatusCode.Created, jobAllowResp.StatusCode);

        fakeRbac.Reset();
        var syncResp = await client.PostAsync("/api/rbac/sync", null);
        Assert.Equal(HttpStatusCode.OK, syncResp.StatusCode);

        var json = await syncResp.ReadJsonAsync();
        Assert.True(json.GetProperty("allSucceeded").GetBoolean());
        var results = json.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(2, results.Count);
        Assert.Equal("avina-workspace", results[0].GetProperty("namespace").GetString());
        Assert.True(results[0].GetProperty("succeeded").GetBoolean());
        Assert.Equal("devops", results[1].GetProperty("namespace").GetString());
        Assert.True(results[1].GetProperty("succeeded").GetBoolean());

        // Simulate failure on one namespace
        fakeRbac.ResultFactory = (ns, _, _) =>
            ns == "devops"
                ? new RbacSyncResult(false, "Kubernetes API error: HTTP 404 (Not Found)")
                : new RbacSyncResult(true);

        var partialResp = await client.PostAsync("/api/rbac/sync", null);
        Assert.Equal(HttpStatusCode.OK, partialResp.StatusCode);
        var partialJson = await partialResp.ReadJsonAsync();
        Assert.False(partialJson.GetProperty("allSucceeded").GetBoolean());
        var partialResults = partialJson.GetProperty("results").EnumerateArray().ToList();
        var devopsResult = Assert.Single(partialResults, r => r.GetProperty("namespace").GetString() == "devops");
        Assert.False(devopsResult.GetProperty("succeeded").GetBoolean());
        Assert.Contains("404", devopsResult.GetProperty("error").GetString());
    }
}
