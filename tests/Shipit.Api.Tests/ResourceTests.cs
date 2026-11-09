using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shipit.Data;

namespace Shipit.Api.Tests;

public class ResourceTests
{
    private static async Task AssertNoSecretsAsync(HttpClient client, params string[] urls)
    {
        foreach (var url in urls)
        {
            var text = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
            foreach (var secret in ApiFactory.Secrets)
            {
                Assert.DoesNotContain(secret, text);
            }
        }
    }

    [Fact]
    public async Task Fake_mode_never_uses_a_database_url_from_the_environment()
    {
        // A URL that would fail instantly if anything tried to use it. Fake mode must ignore it and use local SQLite.
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            [Shipit.Core.ShipitOptions.DatabaseUrlVar] = "postgres://nobody:not-a-real-password@127.0.0.1:1/never",
        });
        var client = await factory.LoggedInClientAsync();

        var id = await client.CreateAsync("/api/credentials", new { name = "git", kind = "git_token", value = ApiFactory.GitToken });
        Assert.True(id > 0);

        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContextAsync();
        Assert.True(db.Database.IsSqlite());
    }

    [Fact]
    public async Task Credentials_never_expose_their_value()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/credentials", new { name = "git", kind = "git_token", value = ApiFactory.GitToken });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync();
        Assert.True(created.GetProperty("hasValue").GetBoolean());
        Assert.False(created.TryGetProperty("value", out _));
        Assert.DoesNotContain(ApiFactory.GitToken, created.GetRawText());
        var id = created.GetProperty("id").GetInt64();

        await AssertNoSecretsAsync(client, "/api/credentials");

        // Stored encrypted, not as plaintext.
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContextAsync();
        var stored = await db.Credentials.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.DoesNotContain(ApiFactory.GitToken, stored.ProtectedValue);
        Assert.StartsWith("v1:", stored.ProtectedValue);
    }

    [Fact]
    public async Task Credential_validation_reports_field_errors()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        async Task<JsonElement> Errors(object body)
        {
            var response = await client.PostAsJsonAsync("/api/credentials", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("errors");
        }

        Assert.True((await Errors(new { name = "r", kind = "registry", value = "pw" })).TryGetProperty("username", out _));
        Assert.True((await Errors(new { name = "x", kind = "nope", value = "v" })).TryGetProperty("kind", out _));
        Assert.True((await Errors(new { name = "k", kind = "ssh_key", value = "not a key" })).TryGetProperty("value", out _));
        Assert.True((await Errors(new { name = "g", kind = "git_token", value = "line1\nline2" })).TryGetProperty("value", out _));
        Assert.True((await Errors(new { name = "", kind = "git_token", value = "v" })).TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Updating_a_credential_without_a_value_keeps_the_stored_secret()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var id = await client.CreateAsync("/api/credentials", new { name = "git", kind = "git_token", value = ApiFactory.GitToken });

        async Task<string> StoredAsync()
        {
            await using var db = await factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContextAsync();
            return (await db.Credentials.AsNoTracking().SingleAsync(c => c.Id == id)).ProtectedValue;
        }

        var before = await StoredAsync();
        var rename = await client.PutAsJsonAsync($"/api/credentials/{id}", new { name = "git-renamed" });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("git-renamed", (await rename.ReadJsonAsync()).GetProperty("name").GetString());
        Assert.Equal(before, await StoredAsync());

        var replace = await client.PutAsJsonAsync($"/api/credentials/{id}", new { value = "ghp_REPLACED_VALUE_999" });
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        Assert.NotEqual(before, await StoredAsync());
        Assert.DoesNotContain("ghp_REPLACED_VALUE_999", await replace.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_credential_in_use_cannot_be_deleted_until_released()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/credentials/{seeded.SshCred}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/slaves/{seeded.Slave}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pipelines/{seeded.Pipeline}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/slaves/{seeded.Slave}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/credentials/{seeded.SshCred}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/credentials/{seeded.SshCred}")).StatusCode);
    }

    [Fact]
    public async Task Slave_requires_an_ssh_credential_and_valid_fields()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var git = await client.CreateAsync("/api/credentials", new { name = "git", kind = "git_token", value = ApiFactory.GitToken });

        var response = await client.PostAsJsonAsync("/api/slaves", new { name = "s", host = "h", port = 70000, user = "u", sshCredentialId = git });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("port", out _));
        Assert.True(errors.TryGetProperty("sshCredentialId", out _));
    }

    [Fact]
    public async Task Pipeline_validation_rejects_wrong_credential_kinds_unsafe_values_and_unlisted_deployments()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        async Task<JsonElement> Errors(Dictionary<string, object?> overrides)
        {
            var body = new Dictionary<string, object?>
            {
                ["name"] = "other",
                ["gitUrl"] = "https://git.example.com/team/demo.git",
                ["gitBranch"] = "main",
                ["gitCredentialId"] = seeded.GitCred,
                ["slaveId"] = seeded.Slave,
                ["dockerfilePath"] = "Dockerfile",
                ["imageName"] = "shipit-demo",
                ["registryHost"] = "registry.example.com",
                ["registryCredentialId"] = seeded.RegCred,
                ["deployNamespace"] = "devops",
                ["deployDeployment"] = "shipit-demo",
                ["deployContainer"] = "app",
            };
            foreach (var (k, v) in overrides)
            {
                body[k] = v;
            }

            var response = await client.PostAsJsonAsync("/api/pipelines", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("errors");
        }

        Assert.True((await Errors(new() { ["gitCredentialId"] = seeded.RegCred })).TryGetProperty("gitCredentialId", out _));
        Assert.True((await Errors(new() { ["registryCredentialId"] = seeded.GitCred })).TryGetProperty("registryCredentialId", out _));
        Assert.True((await Errors(new() { ["gitUrl"] = "ssh://git@host/repo.git" })).TryGetProperty("gitUrl", out _));
        Assert.True((await Errors(new() { ["gitUrl"] = "https://host/repo.git; rm -rf /" })).TryGetProperty("gitUrl", out _));
        Assert.True((await Errors(new() { ["gitBranch"] = "main; id" })).TryGetProperty("gitBranch", out _));
        Assert.True((await Errors(new() { ["dockerfilePath"] = "../../etc/passwd" })).TryGetProperty("dockerfilePath", out _));
        Assert.True((await Errors(new() { ["buildContext"] = "../../etc" })).TryGetProperty("buildContext", out _));
        Assert.True((await Errors(new() { ["buildContext"] = "/root" })).TryGetProperty("buildContext", out _));
        Assert.True((await Errors(new() { ["imageName"] = "Bad Name" })).TryGetProperty("imageName", out _));
        Assert.True((await Errors(new() { ["slaveId"] = 9999 })).TryGetProperty("slaveId", out _));
        Assert.True((await Errors(new() { ["deployNamespace"] = "Bad_NS" })).TryGetProperty("deployNamespace", out _));
        Assert.True((await Errors(new() { ["deployDeployment"] = "payments" })).TryGetProperty("deployDeployment", out _));
        Assert.True((await Errors(new() { ["name"] = "demo" })).TryGetProperty("name", out _)); // duplicate
    }

    [Fact]
    public async Task Slave_connection_test_uses_the_ssh_executor_and_leaks_no_key()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var response = await client.PostAsync($"/api/slaves/{seeded.Slave}/test", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.ReadJsonAsync();
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/slaves/9999/test", null)).StatusCode);
        await AssertNoSecretsAsync(client, "/api/slaves", "/api/pipelines");
    }

    [Fact]
    public async Task Pipeline_creation_and_update_supports_optional_registry_credential()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        // 1. Create pipeline without sending registryCredentialId -> 201, registryCredentialId is null
        var bodyNoReg = new Dictionary<string, object?>
        {
            ["name"] = "pipe-no-reg-field",
            ["gitUrl"] = "https://git.example.com/team/demo.git",
            ["gitBranch"] = "main",
            ["gitCredentialId"] = seeded.GitCred,
            ["slaveId"] = seeded.Slave,
            ["dockerfilePath"] = "Dockerfile",
            ["imageName"] = "shipit-demo",
            ["registryHost"] = "registry.example.com",
            ["deployNamespace"] = "devops",
            ["deployDeployment"] = "shipit-demo",
            ["deployContainer"] = "app"
        };
        var respNoReg = await client.PostAsJsonAsync("/api/pipelines", bodyNoReg);
        Assert.Equal(HttpStatusCode.Created, respNoReg.StatusCode);
        var jsonNoReg = await respNoReg.ReadJsonAsync();
        Assert.True(jsonNoReg.TryGetProperty("registryCredentialId", out var propNoReg));
        Assert.Equal(JsonValueKind.Null, propNoReg.ValueKind);
        var noRegId = jsonNoReg.GetProperty("id").GetInt64();

        // GET confirms registryCredentialId is null
        var getNoReg = await client.GetAsync($"/api/pipelines/{noRegId}");
        Assert.Equal(HttpStatusCode.OK, getNoReg.StatusCode);
        var getNoRegJson = await getNoReg.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, getNoRegJson.GetProperty("registryCredentialId").ValueKind);

        // 2. Create pipeline with explicit null registryCredentialId -> 201, registryCredentialId is null
        var bodyNullReg = new Dictionary<string, object?>(bodyNoReg)
        {
            ["name"] = "pipe-null-reg",
            ["registryCredentialId"] = null
        };
        var respNullReg = await client.PostAsJsonAsync("/api/pipelines", bodyNullReg);
        Assert.Equal(HttpStatusCode.Created, respNullReg.StatusCode);
        var jsonNullReg = await respNullReg.ReadJsonAsync();
        Assert.True(jsonNullReg.TryGetProperty("registryCredentialId", out var propNullReg));
        Assert.Equal(JsonValueKind.Null, propNullReg.ValueKind);

        // 3. Invalid or non-existent registryCredentialId -> 400 Bad Request with field error
        var bodyBadId = new Dictionary<string, object?>(bodyNoReg)
        {
            ["name"] = "pipe-bad-reg-id",
            ["registryCredentialId"] = 99999
        };
        var respBadId = await client.PostAsJsonAsync("/api/pipelines", bodyBadId);
        Assert.Equal(HttpStatusCode.BadRequest, respBadId.StatusCode);
        var badIdErrors = (await respBadId.ReadJsonAsync()).GetProperty("errors");
        Assert.True(badIdErrors.TryGetProperty("registryCredentialId", out _));

        // Wrong kind (using GitToken instead of Registry) -> 400 Bad Request with field error
        var bodyWrongKind = new Dictionary<string, object?>(bodyNoReg)
        {
            ["name"] = "pipe-wrong-reg-kind",
            ["registryCredentialId"] = seeded.GitCred
        };
        var respWrongKind = await client.PostAsJsonAsync("/api/pipelines", bodyWrongKind);
        Assert.Equal(HttpStatusCode.BadRequest, respWrongKind.StatusCode);
        var wrongKindErrors = (await respWrongKind.ReadJsonAsync()).GetProperty("errors");
        Assert.True(wrongKindErrors.TryGetProperty("registryCredentialId", out _));

        // 4. PUT with null removes credential from existing pipeline that had a credential
        var getSeeded = await client.GetAsync($"/api/pipelines/{seeded.Pipeline}");
        Assert.Equal(HttpStatusCode.OK, getSeeded.StatusCode);
        var seededJson = await getSeeded.ReadJsonAsync();
        Assert.Equal(seeded.RegCred, seededJson.GetProperty("registryCredentialId").GetInt64());

        var updateBody = new Dictionary<string, object?>
        {
            ["name"] = seededJson.GetProperty("name").GetString(),
            ["gitUrl"] = seededJson.GetProperty("gitUrl").GetString(),
            ["gitBranch"] = seededJson.GetProperty("gitBranch").GetString(),
            ["gitCredentialId"] = seededJson.GetProperty("gitCredentialId").GetInt64(),
            ["slaveId"] = seededJson.GetProperty("slaveId").GetInt64(),
            ["dockerfilePath"] = seededJson.GetProperty("dockerfilePath").GetString(),
            ["imageName"] = seededJson.GetProperty("imageName").GetString(),
            ["registryHost"] = seededJson.GetProperty("registryHost").GetString(),
            ["registryCredentialId"] = null,
            ["deployNamespace"] = seededJson.GetProperty("deployNamespace").GetString(),
            ["deployDeployment"] = seededJson.GetProperty("deployDeployment").GetString(),
            ["deployContainer"] = seededJson.GetProperty("deployContainer").GetString()
        };
        var putResp = await client.PutAsJsonAsync($"/api/pipelines/{seeded.Pipeline}", updateBody);
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        var putJson = await putResp.ReadJsonAsync();
        Assert.True(putJson.TryGetProperty("registryCredentialId", out var putRegProp));
        Assert.Equal(JsonValueKind.Null, putRegProp.ValueKind);

        // GET confirms it was updated to null
        var getUpdated = await client.GetAsync($"/api/pipelines/{seeded.Pipeline}");
        var getUpdatedJson = await getUpdated.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, getUpdatedJson.GetProperty("registryCredentialId").ValueKind);
    }

    [Fact]
    public async Task Registry_credential_deletion_blocked_when_in_use_and_allowed_when_no_pipeline_uses_it()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        // Also create a pipeline that does NOT use any registry credential
        var unauthenticatedPipeline = await client.CreateAsync("/api/pipelines", new
        {
            name = "unauth-pipeline",
            gitUrl = "https://git.example.com/team/demo.git",
            gitBranch = "main",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            dockerfilePath = "Dockerfile",
            imageName = "shipit-demo",
            registryHost = "registry.example.com",
            registryCredentialId = (long?)null,
            deployNamespace = "devops",
            deployDeployment = "shipit-demo",
            deployContainer = "app"
        });
        Assert.True(unauthenticatedPipeline > 0);

        // seeded.RegCred is used by seeded.Pipeline -> DELETE returns 409 Conflict
        var deleteInUseResp = await client.DeleteAsync($"/api/credentials/{seeded.RegCred}");
        Assert.Equal(HttpStatusCode.Conflict, deleteInUseResp.StatusCode);

        // Release the credential by deleting the pipeline that uses it
        var deletePipeResp = await client.DeleteAsync($"/api/pipelines/{seeded.Pipeline}");
        Assert.Equal(HttpStatusCode.NoContent, deletePipeResp.StatusCode);

        // Now, even though unauth-pipeline still exists, seeded.RegCred is not used by any pipeline -> DELETE returns 204 NoContent
        var deleteReleasedResp = await client.DeleteAsync($"/api/credentials/{seeded.RegCred}");
        Assert.Equal(HttpStatusCode.NoContent, deleteReleasedResp.StatusCode);

        // Subsequent delete returns 404
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/credentials/{seeded.RegCred}")).StatusCode);
    }

    [Fact]
    public async Task Pipeline_creation_and_update_supports_build_context()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var baseBody = new Dictionary<string, object?>
        {
            ["gitUrl"] = "https://git.example.com/team/demo.git",
            ["gitBranch"] = "main",
            ["gitCredentialId"] = seeded.GitCred,
            ["slaveId"] = seeded.Slave,
            ["dockerfilePath"] = "Dockerfile",
            ["imageName"] = "shipit-demo",
            ["registryHost"] = "registry.example.com",
            ["deployNamespace"] = "devops",
            ["deployDeployment"] = "shipit-demo",
            ["deployContainer"] = "app"
        };

        // 1. Omitted buildContext -> returns null
        var bodyNoBc = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-no-bc" };
        var respNoBc = await client.PostAsJsonAsync("/api/pipelines", bodyNoBc);
        Assert.Equal(HttpStatusCode.Created, respNoBc.StatusCode);
        var jsonNoBc = await respNoBc.ReadJsonAsync();
        Assert.True(jsonNoBc.TryGetProperty("buildContext", out var propNoBc));
        Assert.Equal(JsonValueKind.Null, propNoBc.ValueKind);
        var noBcId = jsonNoBc.GetProperty("id").GetInt64();

        var getNoBc = await client.GetAsync($"/api/pipelines/{noBcId}");
        Assert.Equal(HttpStatusCode.OK, getNoBc.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await getNoBc.ReadJsonAsync()).GetProperty("buildContext").ValueKind);

        // 2. Explicit null -> returns null
        var bodyNullBc = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-null-bc", ["buildContext"] = null };
        var respNullBc = await client.PostAsJsonAsync("/api/pipelines", bodyNullBc);
        Assert.Equal(HttpStatusCode.Created, respNullBc.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await respNullBc.ReadJsonAsync()).GetProperty("buildContext").ValueKind);

        // 3. Whitespace or empty -> normalized to null
        var bodyWsBc = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-ws-bc", ["buildContext"] = "   " };
        var respWsBc = await client.PostAsJsonAsync("/api/pipelines", bodyWsBc);
        Assert.Equal(HttpStatusCode.Created, respWsBc.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await respWsBc.ReadJsonAsync()).GetProperty("buildContext").ValueKind);

        var bodyEmptyBc = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-empty-bc", ["buildContext"] = "" };
        var respEmptyBc = await client.PostAsJsonAsync("/api/pipelines", bodyEmptyBc);
        Assert.Equal(HttpStatusCode.Created, respEmptyBc.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await respEmptyBc.ReadJsonAsync()).GetProperty("buildContext").ValueKind);

        // 4. Valid "." -> returns "."
        var bodyDotBc = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-dot-bc", ["buildContext"] = "." };
        var respDotBc = await client.PostAsJsonAsync("/api/pipelines", bodyDotBc);
        Assert.Equal(HttpStatusCode.Created, respDotBc.StatusCode);
        var jsonDotBc = await respDotBc.ReadJsonAsync();
        Assert.Equal(".", jsonDotBc.GetProperty("buildContext").GetString());
        var dotBcId = jsonDotBc.GetProperty("id").GetInt64();

        var getDotBc = await client.GetAsync($"/api/pipelines/{dotBcId}");
        Assert.Equal(".", (await getDotBc.ReadJsonAsync()).GetProperty("buildContext").GetString());

        // 5. Valid relative path "src/app" -> returns "src/app"
        var bodySubBc = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-sub-bc", ["buildContext"] = "src/app" };
        var respSubBc = await client.PostAsJsonAsync("/api/pipelines", bodySubBc);
        Assert.Equal(HttpStatusCode.Created, respSubBc.StatusCode);
        var jsonSubBc = await respSubBc.ReadJsonAsync();
        Assert.Equal("src/app", jsonSubBc.GetProperty("buildContext").GetString());
        var subBcId = jsonSubBc.GetProperty("id").GetInt64();

        // 6. PUT replaces buildContext: update from "src/app" to "." then to null
        var updateBody = new Dictionary<string, object?>(baseBody)
        {
            ["name"] = "pipe-sub-bc",
            ["buildContext"] = "."
        };
        var putResp = await client.PutAsJsonAsync($"/api/pipelines/{subBcId}", updateBody);
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        Assert.Equal(".", (await putResp.ReadJsonAsync()).GetProperty("buildContext").GetString());

        // PUT with whitespace -> clears to null
        updateBody["buildContext"] = "   ";
        var putClearResp = await client.PutAsJsonAsync($"/api/pipelines/{subBcId}", updateBody);
        Assert.Equal(HttpStatusCode.OK, putClearResp.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await putClearResp.ReadJsonAsync()).GetProperty("buildContext").ValueKind);

        // 7. Validation rejects bad buildContext values
        string[] badValues = ["/abs", "../x", "a/../b", "-x", "a b", "a;b", new string('x', 201)];
        foreach (var bad in badValues)
        {
            var badBody = new Dictionary<string, object?>(baseBody)
            {
                ["name"] = "pipe-bad-" + Guid.NewGuid().ToString("N")[..8],
                ["buildContext"] = bad
            };
            var badResp = await client.PostAsJsonAsync("/api/pipelines", badBody);
            Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);
            var errors = (await badResp.ReadJsonAsync()).GetProperty("errors");
            Assert.True(errors.TryGetProperty("buildContext", out _), $"Expected buildContext error for '{bad}'");
        }
    }

    [Fact]
    public async Task Pipeline_creation_and_update_supports_deploy_registry_host()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var baseBody = new Dictionary<string, object?>
        {
            ["gitUrl"] = "https://git.example.com/team/demo.git",
            ["gitBranch"] = "main",
            ["gitCredentialId"] = seeded.GitCred,
            ["slaveId"] = seeded.Slave,
            ["dockerfilePath"] = "Dockerfile",
            ["imageName"] = "shipit-demo",
            ["registryHost"] = "registry.example.com",
            ["deployNamespace"] = "devops",
            ["deployDeployment"] = "shipit-demo",
            ["deployContainer"] = "app"
        };

        // 1. Omitted deployRegistryHost -> returns null
        var bodyNoDrh = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-no-drh" };
        var respNoDrh = await client.PostAsJsonAsync("/api/pipelines", bodyNoDrh);
        Assert.Equal(HttpStatusCode.Created, respNoDrh.StatusCode);
        var jsonNoDrh = await respNoDrh.ReadJsonAsync();
        Assert.True(jsonNoDrh.TryGetProperty("deployRegistryHost", out var propNoDrh));
        Assert.Equal(JsonValueKind.Null, propNoDrh.ValueKind);
        var noDrhId = jsonNoDrh.GetProperty("id").GetInt64();

        var getNoDrh = await client.GetAsync($"/api/pipelines/{noDrhId}");
        Assert.Equal(HttpStatusCode.OK, getNoDrh.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await getNoDrh.ReadJsonAsync()).GetProperty("deployRegistryHost").ValueKind);

        // 2. Explicit null -> returns null
        var bodyNullDrh = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-null-drh", ["deployRegistryHost"] = null };
        var respNullDrh = await client.PostAsJsonAsync("/api/pipelines", bodyNullDrh);
        Assert.Equal(HttpStatusCode.Created, respNullDrh.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await respNullDrh.ReadJsonAsync()).GetProperty("deployRegistryHost").ValueKind);

        // 3. Whitespace or empty -> normalized to null
        var bodyWsDrh = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-ws-drh", ["deployRegistryHost"] = "   " };
        var respWsDrh = await client.PostAsJsonAsync("/api/pipelines", bodyWsDrh);
        Assert.Equal(HttpStatusCode.Created, respWsDrh.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await respWsDrh.ReadJsonAsync()).GetProperty("deployRegistryHost").ValueKind);

        var bodyEmptyDrh = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-empty-drh", ["deployRegistryHost"] = "" };
        var respEmptyDrh = await client.PostAsJsonAsync("/api/pipelines", bodyEmptyDrh);
        Assert.Equal(HttpStatusCode.Created, respEmptyDrh.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await respEmptyDrh.ReadJsonAsync()).GetProperty("deployRegistryHost").ValueKind);

        // 4. Valid host with port -> returns exact value
        var bodyValidDrh = new Dictionary<string, object?>(baseBody) { ["name"] = "pipe-valid-drh", ["deployRegistryHost"] = "registry.k8s.local:5000" };
        var respValidDrh = await client.PostAsJsonAsync("/api/pipelines", bodyValidDrh);
        Assert.Equal(HttpStatusCode.Created, respValidDrh.StatusCode);
        var jsonValidDrh = await respValidDrh.ReadJsonAsync();
        Assert.Equal("registry.k8s.local:5000", jsonValidDrh.GetProperty("deployRegistryHost").GetString());
        var validDrhId = jsonValidDrh.GetProperty("id").GetInt64();

        var getValidDrh = await client.GetAsync($"/api/pipelines/{validDrhId}");
        Assert.Equal(HttpStatusCode.OK, getValidDrh.StatusCode);
        Assert.Equal("registry.k8s.local:5000", (await getValidDrh.ReadJsonAsync()).GetProperty("deployRegistryHost").GetString());

        // 5. PUT replaces deployRegistryHost: update to another host, then clear to null with whitespace
        var updateBody = new Dictionary<string, object?>(baseBody)
        {
            ["name"] = "pipe-valid-drh",
            ["deployRegistryHost"] = "internal-mirror.local"
        };
        var putResp = await client.PutAsJsonAsync($"/api/pipelines/{validDrhId}", updateBody);
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        Assert.Equal("internal-mirror.local", (await putResp.ReadJsonAsync()).GetProperty("deployRegistryHost").GetString());

        // PUT with whitespace -> clears to null
        updateBody["deployRegistryHost"] = "   ";
        var putClearResp = await client.PutAsJsonAsync($"/api/pipelines/{validDrhId}", updateBody);
        Assert.Equal(HttpStatusCode.OK, putClearResp.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await putClearResp.ReadJsonAsync()).GetProperty("deployRegistryHost").ValueKind);

        // 6. Validation rejects bad deployRegistryHost values
        string[] badValues = ["a b", "x;y", "http://x", "-bad", "host..com"];
        foreach (var bad in badValues)
        {
            var badBody = new Dictionary<string, object?>(baseBody)
            {
                ["name"] = "pipe-bad-" + Guid.NewGuid().ToString("N")[..8],
                ["deployRegistryHost"] = bad
            };
            var badResp = await client.PostAsJsonAsync("/api/pipelines", badBody);
            Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);
            var errors = (await badResp.ReadJsonAsync()).GetProperty("errors");
            Assert.True(errors.TryGetProperty("deployRegistryHost", out _), $"Expected deployRegistryHost error for '{bad}'");
        }
    }
}
