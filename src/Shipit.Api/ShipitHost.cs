using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data;
using Shipit.Engine;
using Shipit.K8s;
using Shipit.Security;
using Shipit.Ssh;

namespace Shipit.Api;

public static class ShipitHost
{
    public const string SqlitePathKey = "Shipit:SqlitePath";
    public const string SessionCookie = "shipit_session";

    /// <summary>
    /// Registers everything Shipit needs. Settings are read lazily when services are first resolved, and
    /// <c>Shipit:UseFakes</c> swaps in fakes so the app runs without Postgres, SSH or a cluster.
    /// </summary>
    public static void AddShipit(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var options = ShipitOptions.FromLookup(name => config[name]);
            var missing = MissingSettings(options);
            if (missing.Count > 0)
            {
                throw new InvalidOperationException("Missing required settings: " + string.Join(", ", missing));
            }

            return options;
        });
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<ShipitOptions>();
            return new AdminLogin(o.AdminUser, o.AdminPassword ?? "admin");
        });
        services.AddSingleton<ISecretProtector>(sp =>
            new AesGcmSecretProtector(sp.GetRequiredService<ShipitOptions>().MasterKey ?? AesGcmSecretProtector.GenerateKey()));
        services.AddSingleton(sp => new DeployAllowList(sp.GetRequiredService<ShipitOptions>().DeployAllowList));

        services.AddDbContextFactory<ShipitDbContext>((sp, o) =>
        {
            var options = sp.GetRequiredService<ShipitOptions>();
            if (options.UseFakes || options.DatabaseUrl is null)
            {
                // Fake mode NEVER touches a real database, even if a database URL is present in the environment:
                // it always uses a local throw-away SQLite file (MissingSettings rejects a missing URL otherwise).
                var path = sp.GetRequiredService<IConfiguration>()[SqlitePathKey] ?? "shipit-dev.db";
                o.UseSqlite($"Data Source={path}");
            }
            else
            {
                o.UseNpgsql(DbUrlParser.ToNpgsqlConnectionString(options.DatabaseUrl));
            }
        });
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContext());

        services.AddSingleton<ISshExecutor>(sp =>
            sp.GetRequiredService<ShipitOptions>().UseFakes ? FakeSshExecutor.HappyPath() : new SshNetExecutor());
        services.AddSingleton<IK8sDeployer>(sp =>
        {
            var allowList = sp.GetRequiredService<DeployAllowList>();
            return sp.GetRequiredService<ShipitOptions>().UseFakes
                ? new FakeK8sDeployer(allowList)
                : KubernetesDeployer.CreateInCluster(allowList);
        });

        services.AddShipitEngine();

        // Session cookies are encrypted with a per-process key: logging in again after a restart is fine for one admin,
        // and it avoids writing a key ring to the (read-only) container file system.
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = SessionCookie;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // UI is reached through port-forward over plain HTTP
                o.ExpireTimeSpan = TimeSpan.FromHours(8);
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());
    }

    /// <summary>Applies database migrations (Postgres) or creates the schema (dev SQLite), retrying while the DB starts up.</summary>
    public static async Task PrepareDatabaseAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shipit.Startup");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ShipitDbContext>();
                if (db.Database.IsNpgsql())
                {
                    await db.Database.MigrateAsync();
                }
                else
                {
                    await db.Database.EnsureCreatedAsync();
                }

                return;
            }
            catch (Exception ex) when (attempt < 5)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}/5): {Type}. Retrying in 3s.", attempt, ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    /// <summary>Maps all endpoints plus the single-page UI.</summary>
    public static void MapShipit(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealth();
        app.MapAuth();
        app.MapSlaves();
        app.MapCredentials();
        app.MapPipelines();
        app.MapRuns();

        // Unknown API paths must be a plain 404, never the SPA shell.
        app.Map("/api/{**path}", () => Results.NotFound());
        app.MapFallbackToFile("index.html").AllowAnonymous();
    }

    private static List<string> MissingSettings(ShipitOptions options)
    {
        var missing = new List<string>();
        if (options.UseFakes)
        {
            return missing;
        }

        if (options.DatabaseUrl is null)
        {
            missing.Add(ShipitOptions.DatabaseUrlVar);
        }

        if (options.MasterKey is null)
        {
            missing.Add(ShipitOptions.MasterKeyVar);
        }

        if (options.AdminPassword is null)
        {
            missing.Add(ShipitOptions.AdminPasswordVar);
        }

        return missing;
    }
}

/// <summary>The single admin account. Compared in constant time.</summary>
public sealed class AdminLogin(string username, string password)
{
    private readonly byte[] _userHash = SHA256.HashData(Encoding.UTF8.GetBytes(username));
    private readonly byte[] _passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));

    public string Username { get; } = username;

    public bool Verify(string? candidateUser, string? candidatePassword)
    {
        var userOk = CryptographicOperations.FixedTimeEquals(_userHash, SHA256.HashData(Encoding.UTF8.GetBytes(candidateUser ?? string.Empty)));
        var passwordOk = CryptographicOperations.FixedTimeEquals(_passwordHash, SHA256.HashData(Encoding.UTF8.GetBytes(candidatePassword ?? string.Empty)));
        return userOk & passwordOk;
    }
}
