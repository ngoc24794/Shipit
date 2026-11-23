using System.Text.Json;

namespace Shipit.Core;

/// <summary>namespace -> deployments Shipit is allowed to change.</summary>
public sealed record DeployAllowListEntry(string Namespace, IReadOnlyList<string> Deployments);

/// <summary>An entry for the Job allowlist seed configuration.</summary>
public sealed record JobAllowListEntry(string Namespace, string Kind, string Name);

/// <summary>
/// Runtime configuration, read from environment variables (see <see cref="FromLookup"/>).
/// Values are secrets: never log an instance of this type.
/// </summary>
public sealed class ShipitOptions
{
    public const string DatabaseUrlVar = "AVINA_STAGING_SHIPIT_DB_URL";
    public const string MigrationDatabaseUrlVar = "SHIPIT_MIGRATION_DB_URL";
    public const string AutoMigrateVar = "SHIPIT_AUTO_MIGRATE";
    public const string AutoMigrateKey = "Shipit:AutoMigrate";
    public const string MasterKeyVar = "SHIPIT_MASTER_KEY";
    public const string AdminUserVar = "SHIPIT_ADMIN_USER";
    public const string AdminPasswordVar = "SHIPIT_ADMIN_PASSWORD";
    public const string AllowListVar = "SHIPIT_DEPLOY_ALLOWLIST";
    public const string JobAllowListVar = "SHIPIT_JOB_ALLOWLIST";
    public const string JobAllowListKey = "Shipit:JobAllowList";
    public const string UseFakesKey = "Shipit:UseFakes";

    public string? DatabaseUrl { get; init; }
    public string? MigrationDatabaseUrl { get; init; }
    public bool AutoMigrate { get; init; }
    public string? MasterKey { get; init; }
    public string AdminUser { get; init; } = "admin";
    public string? AdminPassword { get; init; }
    public IReadOnlyList<DeployAllowListEntry> DeployAllowList { get; init; } = [];
    public IReadOnlyList<JobAllowListEntry> JobAllowList { get; init; } = [];
    public bool UseFakes { get; init; }

    /// <summary>Builds options from a key lookup (e.g. <c>name =&gt; configuration[name]</c>).</summary>
    public static ShipitOptions FromLookup(Func<string, string?> get)
    {
        var useFakes = bool.TryParse(get(UseFakesKey), out var fakes) && fakes;
        var autoMigrate = bool.TryParse(Blank(get(AutoMigrateVar)) ?? Blank(get(AutoMigrateKey)), out var migrate) && migrate;
        var user = get(AdminUserVar);
        var databaseUrl = Blank(get(DatabaseUrlVar));
        return new ShipitOptions
        {
            DatabaseUrl = databaseUrl,
            MigrationDatabaseUrl = Blank(get(MigrationDatabaseUrlVar)) ?? databaseUrl,
            AutoMigrate = autoMigrate,
            MasterKey = Blank(get(MasterKeyVar)),
            AdminUser = string.IsNullOrWhiteSpace(user) ? "admin" : user,
            AdminPassword = Blank(get(AdminPasswordVar)),
            DeployAllowList = ParseAllowList(get(AllowListVar)),
            JobAllowList = ParseJobAllowList(get(JobAllowListKey) ?? get(JobAllowListVar)),
            UseFakes = useFakes,
        };
    }

    /// <summary>Parses {"ns":["deploy1","deploy2"]}. Blank input yields an empty list (deny everything).</summary>
    public static IReadOnlyList<DeployAllowListEntry> ParseAllowList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var map = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
                  ?? throw new FormatException($"{AllowListVar} must be a JSON object of namespace -> deployments.");
        return map.Select(kv => new DeployAllowListEntry(kv.Key, kv.Value)).ToList();
    }

    /// <summary>Parses [{"namespace":"...","kind":"...","name":"..."}]. Blank input yields an empty list.</summary>
    public static IReadOnlyList<JobAllowListEntry> ParseJobAllowList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = JsonSerializer.Deserialize<List<JobAllowListEntry>>(json, options)
                  ?? throw new FormatException($"{JobAllowListVar} must be a JSON array of allowlist entries.");
        return list;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
