using System.Text.Json;

namespace Shipit.Core;

/// <summary>namespace -> deployments Shipit is allowed to change.</summary>
public sealed record DeployAllowListEntry(string Namespace, IReadOnlyList<string> Deployments);

/// <summary>
/// Runtime configuration, read from environment variables (see <see cref="FromLookup"/>).
/// Values are secrets: never log an instance of this type.
/// </summary>
public sealed class ShipitOptions
{
    public const string DatabaseUrlVar = "AVINA_STAGING_SHIPIT_DB_URL";
    public const string MasterKeyVar = "SHIPIT_MASTER_KEY";
    public const string AdminUserVar = "SHIPIT_ADMIN_USER";
    public const string AdminPasswordVar = "SHIPIT_ADMIN_PASSWORD";
    public const string AllowListVar = "SHIPIT_DEPLOY_ALLOWLIST";
    public const string UseFakesKey = "Shipit:UseFakes";

    public string? DatabaseUrl { get; init; }
    public string? MasterKey { get; init; }
    public string AdminUser { get; init; } = "admin";
    public string? AdminPassword { get; init; }
    public IReadOnlyList<DeployAllowListEntry> DeployAllowList { get; init; } = [];
    public bool UseFakes { get; init; }

    /// <summary>Builds options from a key lookup (e.g. <c>name =&gt; configuration[name]</c>).</summary>
    public static ShipitOptions FromLookup(Func<string, string?> get)
    {
        var useFakes = bool.TryParse(get(UseFakesKey), out var fakes) && fakes;
        var user = get(AdminUserVar);
        return new ShipitOptions
        {
            DatabaseUrl = Blank(get(DatabaseUrlVar)),
            MasterKey = Blank(get(MasterKeyVar)),
            AdminUser = string.IsNullOrWhiteSpace(user) ? "admin" : user,
            AdminPassword = Blank(get(AdminPasswordVar)),
            DeployAllowList = ParseAllowList(get(AllowListVar)),
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

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
