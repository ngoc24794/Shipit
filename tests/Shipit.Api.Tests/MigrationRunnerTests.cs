using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shipit.Core;
using Shipit.Data;

namespace Shipit.Api.Tests;

public class MigrationRunnerTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task Missing_migration_and_app_db_urls_fails_fast_without_app_secrets()
    {
        var config = BuildConfig(new());
        var logger = new CapturingLogger();
        var calls = 0;

        var exitCode = await MigrationRunner.RunAsync(
            config,
            logger,
            migrateAction: (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            retryDelay: TimeSpan.Zero);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, calls);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains(ShipitOptions.MigrationDatabaseUrlVar, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Uses_migration_db_url_without_requiring_master_key_or_admin_password()
    {
        var config = BuildConfig(new()
        {
            [ShipitOptions.MigrationDatabaseUrlVar] = "postgres://migrator:secret-pw@db.internal:5432/shipit",
            [ShipitOptions.DatabaseUrlVar] = "postgres://appuser:other-pw@db.internal:5432/shipit",
        });
        var logger = new CapturingLogger();
        string? capturedConnStr = null;

        var exitCode = await MigrationRunner.RunAsync(
            config,
            logger,
            migrateAction: (connStr, _) =>
            {
                capturedConnStr = connStr;
                return Task.CompletedTask;
            },
            retryDelay: TimeSpan.Zero);

        Assert.Equal(0, exitCode);
        Assert.NotNull(capturedConnStr);
        Assert.Contains("Username=migrator", capturedConnStr, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-pw", string.Join("\n", logger.Entries.Select(e => e.Message)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Falls_back_to_app_db_url_when_migration_db_url_is_unset()
    {
        var config = BuildConfig(new()
        {
            [ShipitOptions.DatabaseUrlVar] = "postgres://fallbackuser:pw@db.internal:5432/shipit",
        });
        var logger = new CapturingLogger();
        string? capturedConnStr = null;

        var exitCode = await MigrationRunner.RunAsync(
            config,
            logger,
            migrateAction: (connStr, _) =>
            {
                capturedConnStr = connStr;
                return Task.CompletedTask;
            },
            retryDelay: TimeSpan.Zero);

        Assert.Equal(0, exitCode);
        Assert.NotNull(capturedConnStr);
        Assert.Contains("Username=fallbackuser", capturedConnStr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_transient_postgres_permission_error_42501_does_not_retry()
    {
        var config = BuildConfig(new()
        {
            [ShipitOptions.MigrationDatabaseUrlVar] = "postgres://appuser:pw@db.internal:5432/shipit",
        });
        var logger = new CapturingLogger();
        var calls = 0;

        var exitCode = await MigrationRunner.RunAsync(
            config,
            logger,
            migrateAction: (_, _) =>
            {
                calls++;
                throw new PostgresException("must be owner of table Runs", "ERROR", "ERROR", "42501");
            },
            retryDelay: TimeSpan.Zero);

        Assert.Equal(1, exitCode);
        Assert.Equal(1, calls);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("42501", StringComparison.Ordinal) &&
            e.Message.Contains("must be owner of table Runs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transient_connection_error_retries_and_succeeds_on_later_attempt()
    {
        var config = BuildConfig(new()
        {
            [ShipitOptions.MigrationDatabaseUrlVar] = "postgres://migrator:pw@db.internal:5432/shipit",
        });
        var logger = new CapturingLogger();
        var calls = 0;

        var exitCode = await MigrationRunner.RunAsync(
            config,
            logger,
            migrateAction: (_, _) =>
            {
                calls++;
                if (calls < 3)
                {
                    throw new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused));
                }
                return Task.CompletedTask;
            },
            retryDelay: TimeSpan.Zero);

        Assert.Equal(0, exitCode);
        Assert.Equal(3, calls);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task PrepareDatabaseAsync_in_fake_sqlite_mode_creates_schema_when_auto_migrate_is_false()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"shipit-prepare-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            var config = BuildConfig(new()
            {
                [ShipitOptions.UseFakesKey] = "true",
                [ShipitOptions.AutoMigrateVar] = "false",
            });
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton(ShipitOptions.FromLookup(k => config[k]));
            services.AddDbContextFactory<ShipitDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
            services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContext());

            await using var sp = services.BuildServiceProvider();
            var logger = new CapturingLogger();

            await ShipitHost.PrepareDatabaseAsync(sp, logger, retryDelay: TimeSpan.Zero);

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShipitDbContext>();
            Assert.True(await db.Database.CanConnectAsync());
            Assert.False(await db.Pipelines.AnyAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task PrepareDatabaseAsync_npgsql_with_auto_migrate_false_skips_migrate_and_fails_on_unreachable_db()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new()
        {
            [ShipitOptions.DatabaseUrlVar] = "postgres://app:pw@127.0.0.1:1/shipit",
            [ShipitOptions.AutoMigrateVar] = "false",
        });
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(ShipitOptions.FromLookup(k => config[k]));
        services.AddDbContextFactory<ShipitDbContext>(o =>
            o.UseNpgsql(DbUrlParser.ToNpgsqlConnectionString(config[ShipitOptions.DatabaseUrlVar]!)));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContext());

        await using var sp = services.BuildServiceProvider();
        var logger = new CapturingLogger();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            ShipitHost.PrepareDatabaseAsync(sp, logger, retryDelay: TimeSpan.Zero));

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("Skipping EF Core migrations on startup (SHIPIT_AUTO_MIGRATE=false)", StringComparison.Ordinal));
        Assert.Equal(4, logger.Entries.Count(e => e.Level == LogLevel.Warning));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
