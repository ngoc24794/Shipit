using System.Net;
using System.Net.Http.Json;

namespace Shipit.Api.Tests;

public class HealthAndAuthTests
{
    [Fact]
    public async Task Healthz_is_public_and_returns_ok()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readyz_is_public_and_reports_ready()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.True(body.GetProperty("ready").GetBoolean());
        Assert.Equal("ok", body.GetProperty("database").GetString());
        Assert.Equal(0, body.GetProperty("pendingMigrations").GetInt32());
    }

    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/slaves")]
    [InlineData("POST", "/api/slaves")]
    [InlineData("GET", "/api/slaves/1")]
    [InlineData("POST", "/api/slaves/1/test")]
    [InlineData("GET", "/api/credentials")]
    [InlineData("POST", "/api/credentials")]
    [InlineData("DELETE", "/api/credentials/1")]
    [InlineData("GET", "/api/pipelines")]
    [InlineData("PUT", "/api/pipelines/1")]
    [InlineData("POST", "/api/pipelines/1/runs")]
    [InlineData("GET", "/api/runs")]
    [InlineData("GET", "/api/runs/1")]
    [InlineData("GET", "/api/runs/1/logs")]
    [InlineData("GET", "/api/does-not-exist")]
    public async Task Protected_routes_return_401_without_a_session(string method, string url)
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("root", ApiFactory.AdminPassword)]
    [InlineData("", "")]
    public async Task Wrong_credentials_are_rejected(string user, string password)
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = user, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_sets_a_http_only_session_cookie_and_logout_ends_it()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = ApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("shipit_session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("admin", (await me.ReadJsonAsync()).GetProperty("username").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Unknown_api_paths_are_404_for_a_logged_in_user_not_the_spa_shell()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/does-not-exist")).StatusCode);
    }
}
