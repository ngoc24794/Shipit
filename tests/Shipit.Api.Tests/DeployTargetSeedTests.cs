using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Data;

namespace Shipit.Api.Tests;

public class DeployTargetSeedTests
{
    [Fact]
    public async Task Seed_Lifecycle_And_Idempotency()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"shipit-seed-test-{Guid.NewGuid():N}.db");
        try
        {
            // 1. Initial startup: empty table + valid env var -> seeds targets
            using (var factory1 = new ApiFactory(new Dictionary<string, string>
            {
                [ShipitHost.SqlitePathKey] = dbPath,
                ["SHIPIT_DEPLOY_ALLOWLIST"] = "{\"team-a\":[\"service-1\",\"service-2\"]}"
            }))
            {
                var client = await factory1.LoggedInClientAsync();
                var listResp = await client.GetAsync("/api/deploy-targets");
                Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
                var list = await listResp.ReadJsonAsync();
                Assert.Equal(2, list.GetArrayLength());
                Assert.Contains(list.EnumerateArray(), t => t.GetProperty("namespace").GetString() == "team-a" && t.GetProperty("deployment").GetString() == "service-1");
                Assert.Contains(list.EnumerateArray(), t => t.GetProperty("namespace").GetString() == "team-a" && t.GetProperty("deployment").GetString() == "service-2");
            }

            // 2. Restart: with different allowlist in env -> does NOT reseed because already seeded
            using (var factory2 = new ApiFactory(new Dictionary<string, string>
            {
                [ShipitHost.SqlitePathKey] = dbPath,
                ["SHIPIT_DEPLOY_ALLOWLIST"] = "{\"team-b\":[\"service-3\"]}"
            }))
            {
                var client = await factory2.LoggedInClientAsync();
                var listResp = await client.GetAsync("/api/deploy-targets");
                var list = await listResp.ReadJsonAsync();
                Assert.Equal(2, list.GetArrayLength()); // Still the original 2 items
                Assert.DoesNotContain(list.EnumerateArray(), t => t.GetProperty("namespace").GetString() == "team-b");

                // Admin deletes all targets
                foreach (var item in list.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetInt64();
                    var delResp = await client.DeleteAsync($"/api/deploy-targets/{id}");
                    Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);
                }

                var emptyList = await (await client.GetAsync("/api/deploy-targets")).ReadJsonAsync();
                Assert.Equal(0, emptyList.GetArrayLength());
            }

            // 3. Restart after admin deleted everything -> remains empty (does NOT re-seed)
            using (var factory3 = new ApiFactory(new Dictionary<string, string>
            {
                [ShipitHost.SqlitePathKey] = dbPath,
                ["SHIPIT_DEPLOY_ALLOWLIST"] = "{\"team-a\":[\"service-1\"]}"
            }))
            {
                var client = await factory3.LoggedInClientAsync();
                var listResp = await client.GetAsync("/api/deploy-targets");
                var list = await listResp.ReadJsonAsync();
                Assert.Equal(0, list.GetArrayLength());
            }
        }
        finally
        {
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            {
                try { File.Delete(p); } catch { }
            }
        }
    }

    [Fact]
    public async Task Invalid_Or_Missing_AllowList_Does_Not_Fail_Startup()
    {
        // 1. Invalid JSON syntax
        using (var factoryBadJson = new ApiFactory(new Dictionary<string, string>
        {
            ["SHIPIT_DEPLOY_ALLOWLIST"] = "{not valid json}"
        }))
        {
            var client = await factoryBadJson.LoggedInClientAsync();
            var listResp = await client.GetAsync("/api/deploy-targets");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            var list = await listResp.ReadJsonAsync();
            Assert.Equal(0, list.GetArrayLength());
        }

        // 2. Missing (empty string)
        using (var factoryEmpty = new ApiFactory(new Dictionary<string, string>
        {
            ["SHIPIT_DEPLOY_ALLOWLIST"] = ""
        }))
        {
            var client = await factoryEmpty.LoggedInClientAsync();
            var listResp = await client.GetAsync("/api/deploy-targets");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            var list = await listResp.ReadJsonAsync();
            Assert.Equal(0, list.GetArrayLength());
        }
    }
}
