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
}
