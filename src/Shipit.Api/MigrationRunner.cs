using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shipit.Core;
using Shipit.Data;

namespace Shipit.Api;

/// <summary>
/// Executes EF Core database migrations in standalone <c>--migrate</c> mode without starting Kestrel
/// or requiring web-app secrets (<c>SHIPIT_MASTER_KEY</c>, <c>SHIPIT_ADMIN_PASSWORD</c>).
/// </summary>
public static class MigrationRunner
{
    public const int MaxAttempts = 5;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Builds a minimal configuration/logging host from <paramref name="args"/> and runs database migrations.
    /// Returns 0 on success or a non-zero exit code on failure.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

        using var host = builder.Build();
        var config = host.Services.GetRequiredService<IConfiguration>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shipit.Migrator");

        return await RunAsync(config, logger, migrateAction: null, retryDelay: null, ct);
    }

    internal static async Task<int> RunAsync(
        IConfiguration config,
        ILogger logger,
        Func<string, CancellationToken, Task>? migrateAction = null,
        TimeSpan? retryDelay = null,
        CancellationToken ct = default)
    {
        var options = ShipitOptions.FromLookup(name => config[name]);
        var migrationDbUrl = options.MigrationDatabaseUrl;
        if (string.IsNullOrWhiteSpace(migrationDbUrl))
        {
            logger.LogError(
                "Missing migration database URL. Set {MigrationVar} (or fallback {FallbackVar}).",
                ShipitOptions.MigrationDatabaseUrlVar,
                ShipitOptions.DatabaseUrlVar);
            return 1;
        }

        string connectionString;
        try
        {
            connectionString = DbUrlParser.ToNpgsqlConnectionString(migrationDbUrl);
        }
        catch (ArgumentException ex)
        {
            logger.LogError("Invalid migration database URL configuration: {Message}", ex.Message);
            return 1;
        }

        var executeMigration = migrateAction ?? DefaultMigrateAsync;
        var delay = retryDelay ?? DefaultRetryDelay;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                logger.LogInformation("Applying EF Core migrations (attempt {Attempt}/{MaxAttempts})...", attempt, MaxAttempts);
                await executeMigration(connectionString, ct);
                logger.LogInformation("EF Core migrations completed successfully.");
                return 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                logger.LogWarning("Database migration was cancelled.");
                return 1;
            }
            catch (Exception ex)
            {
                var (sqlState, detail) = DescribeDatabaseException(ex);
                var nonTransient = IsNonTransientPostgresError(ex);

                if (nonTransient || attempt >= MaxAttempts)
                {
                    if (sqlState is not null)
                    {
                        logger.LogError(
                            "Database migration failed ({Type}, SqlState={SqlState}): {Detail}",
                            ex.GetType().Name,
                            sqlState,
                            detail);
                    }
                    else
                    {
                        logger.LogError(
                            "Database migration failed ({Type}): {Detail}",
                            ex.GetType().Name,
                            detail);
                    }
                    return 1;
                }

                if (sqlState is not null)
                {
                    logger.LogWarning(
                        "Database migration attempt {Attempt}/{MaxAttempts} failed ({Type}, SqlState={SqlState}): {Detail}. Retrying in {DelaySeconds}s.",
                        attempt,
                        MaxAttempts,
                        ex.GetType().Name,
                        sqlState,
                        detail,
                        delay.TotalSeconds);
                }
                else
                {
                    logger.LogWarning(
                        "Database migration attempt {Attempt}/{MaxAttempts} failed ({Type}): {Detail}. Retrying in {DelaySeconds}s.",
                        attempt,
                        MaxAttempts,
                        ex.GetType().Name,
                        detail,
                        delay.TotalSeconds);
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct);
                }
            }
        }

        return 1;
    }

    private static async Task DefaultMigrateAsync(string connectionString, CancellationToken ct)
    {
        var dbOptions = new DbContextOptionsBuilder<ShipitDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new ShipitDbContext(dbOptions);
        await db.Database.MigrateAsync(ct);
    }

    internal static PostgresException? FindPostgresException(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is PostgresException pgEx)
            {
                return pgEx;
            }

            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    var found = FindPostgresException(inner);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }

            ex = ex.InnerException;
        }

        return null;
    }

    /// <summary>
    /// Returns true for deterministic SQL/permission errors (e.g. Postgres SqlState 42xxx such as 42501 insufficient_privilege)
    /// that will not succeed on retry.
    /// </summary>
    internal static bool IsNonTransientPostgresError(Exception ex)
    {
        var pgEx = FindPostgresException(ex);
        return pgEx is not null &&
               !string.IsNullOrEmpty(pgEx.SqlState) &&
               pgEx.SqlState.StartsWith("42", StringComparison.Ordinal);
    }

    internal static (string? SqlState, string Detail) DescribeDatabaseException(Exception ex)
    {
        var pgEx = FindPostgresException(ex);
        if (pgEx is not null)
        {
            var msg = string.IsNullOrWhiteSpace(pgEx.MessageText) ? pgEx.Message : pgEx.MessageText;
            return (pgEx.SqlState, SanitizeMessage(msg));
        }

        return (null, SanitizeMessage(ex.Message));
    }

    private static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "No additional error details.";
        }

        // Guard against any accidental credential/URL echo in exception messages.
        var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length > 500 ? singleLine[..500] + "..." : singleLine;
    }
}
