using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Data;

namespace Shipit.Api.Tests;

public class RememberLoginTests
{
    private const string FixedMasterKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE="; // 32 bytes base64

    [Fact]
    public async Task Login_RememberMe_True_Sets_Persistent_Cookie_With_MaxAge_Approx_30_Days()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = ApiFactory.AdminPassword,
            rememberMe = true
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookieHeader = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("shipit_session="));

        // Must have expires and max-age ≈ 30 days (2,592,000 seconds)
        Assert.Contains("expires=", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=", cookieHeader, StringComparison.OrdinalIgnoreCase);

        var match = Regex.Match(cookieHeader, @"max-age=(\d+)", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "max-age must be present in Set-Cookie");
        var maxAgeSeconds = long.Parse(match.Groups[1].Value);

        // 30 days = 2592000 seconds; allow a small tolerance of a few seconds
        var thirtyDays = (long)TimeSpan.FromDays(30).TotalSeconds;
        Assert.InRange(maxAgeSeconds, thirtyDays - 60, thirtyDays + 60);

        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Login_RememberMe_False_Or_Missing_Sets_Session_Cookie_Without_Expires_Or_MaxAge(bool? rememberMe)
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        HttpResponseMessage response;
        if (rememberMe.HasValue)
        {
            response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = ApiFactory.AdminPassword,
                rememberMe = rememberMe.Value
            });
        }
        else
        {
            // Missing rememberMe property in JSON payload
            response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = ApiFactory.AdminPassword
            });
        }

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookieHeader = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("shipit_session="));

        // Session cookies MUST NOT have expires or max-age
        Assert.DoesNotContain("expires=", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookieHeader, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_Survives_Restart_Across_Hosts_On_Same_Db_And_MasterKey()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"shipit-restart-{Guid.NewGuid():N}.db");
        var settings = new Dictionary<string, string>
        {
            ["SHIPIT_MASTER_KEY"] = FixedMasterKey
        };

        try
        {
            string sessionCookie;

            // 1. Host A: Login and get session cookie
            using (var hostA = new ApiFactory(settings, dbPath, deleteDbOnDispose: false))
            {
                var clientA = hostA.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                var loginResponse = await clientA.PostAsJsonAsync("/api/auth/login", new
                {
                    username = "admin",
                    password = ApiFactory.AdminPassword,
                    rememberMe = true
                });
                Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);

                var setCookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"), c => c.StartsWith("shipit_session="));
                sessionCookie = setCookie.Split(';')[0]; // shipit_session=<val>
            }

            // 2. Host B: New instance (simulates pod restart) on SAME DB and SAME master key
            using (var hostB = new ApiFactory(settings, dbPath, deleteDbOnDispose: false))
            {
                var clientB = hostB.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
                request.Headers.Add("Cookie", sessionCookie);

                var meResponse = await clientB.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

                var body = await meResponse.ReadJsonAsync();
                Assert.Equal("admin", body.GetProperty("username").GetString());
            }

            // 3. Counter-test (Đối chứng): Host C on a DIFFERENT database cannot validate Host A's session cookie
            var differentDbPath = Path.Combine(Path.GetTempPath(), $"shipit-diff-{Guid.NewGuid():N}.db");
            try
            {
                using var hostC = new ApiFactory(settings, differentDbPath, deleteDbOnDispose: true);
                var clientC = hostC.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
                request.Headers.Add("Cookie", sessionCookie);

                var meResponse = await clientC.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
            }
            finally
            {
                if (File.Exists(differentDbPath))
                {
                    File.Delete(differentDbPath);
                }
            }
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task DataProtection_Keys_In_Db_Are_Encrypted_At_Rest()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Trigger sign-in so Data Protection generates and stores key in database
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = ApiFactory.AdminPassword
        });
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);

        // Verify keys in the DB
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>();
        using var db = await dbFactory.CreateDbContextAsync();

        var keys = await db.DataProtectionKeys.AsNoTracking().ToListAsync();
        Assert.NotEmpty(keys);

        foreach (var key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(key.Xml));

            // Must be encrypted payload (v1: prefix from AesGcmSecretProtector)
            Assert.StartsWith("v1:", key.Xml);

            // Plaintext XML key tags MUST NOT appear in the database
            Assert.DoesNotContain("<key", key.Xml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<masterKey", key.Xml, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Changing_Admin_Password_Invalidates_Existing_Sessions()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"shipit-admin-change-{Guid.NewGuid():N}.db");
        var originalSettings = new Dictionary<string, string>
        {
            ["SHIPIT_MASTER_KEY"] = FixedMasterKey,
            ["SHIPIT_ADMIN_PASSWORD"] = "original-password-123!"
        };

        try
        {
            string sessionCookie;

            // 1. Host A: Login with original admin password
            using (var hostA = new ApiFactory(originalSettings, dbPath, deleteDbOnDispose: false))
            {
                var clientA = hostA.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                var loginResponse = await clientA.PostAsJsonAsync("/api/auth/login", new
                {
                    username = "admin",
                    password = "original-password-123!"
                });
                Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);

                var setCookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"), c => c.StartsWith("shipit_session="));
                sessionCookie = setCookie.Split(';')[0];
            }

            // 2. Host B: Starts on SAME DB and master key, but DIFFERENT admin password
            var changedSettings = new Dictionary<string, string>
            {
                ["SHIPIT_MASTER_KEY"] = FixedMasterKey,
                ["SHIPIT_ADMIN_PASSWORD"] = "new-changed-password-456!"
            };

            using (var hostB = new ApiFactory(changedSettings, dbPath, deleteDbOnDispose: false))
            {
                var clientB = hostB.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
                request.Headers.Add("Cookie", sessionCookie);

                var meResponse = await clientB.SendAsync(request);

                // Existing session MUST be rejected with 401 Unauthorized
                Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);

                // Password must not leak in response body
                var content = await meResponse.Content.ReadAsStringAsync();
                Assert.DoesNotContain("original-password-123!", content);
                Assert.DoesNotContain("new-changed-password-456!", content);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task Wrong_Credentials_Returns_401_And_Logout_Clears_Session()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        // Wrong credentials -> 401
        var wrongResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = "completely-wrong-password"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);

        // Correct login -> 204
        var correctResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = ApiFactory.AdminPassword
        });
        Assert.Equal(HttpStatusCode.NoContent, correctResponse.StatusCode);

        // /api/auth/me -> 200
        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        // Logout -> 204
        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // After logout /api/auth/me -> 401
        var meAfterLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);
    }
}
