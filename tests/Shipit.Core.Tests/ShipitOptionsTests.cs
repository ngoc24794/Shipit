using Shipit.Core;

namespace Shipit.Core.Tests;

public class ShipitOptionsTests
{
    private static Func<string, string?> Lookup(Dictionary<string, string?> d) => k => d.GetValueOrDefault(k);

    [Fact]
    public void Empty_environment_gives_safe_defaults()
    {
        var o = ShipitOptions.FromLookup(Lookup([]));
        Assert.Null(o.DatabaseUrl);
        Assert.Null(o.MigrationDatabaseUrl);
        Assert.False(o.AutoMigrate);
        Assert.Equal("admin", o.AdminUser);
        Assert.Empty(o.DeployAllowList);
        Assert.False(o.UseFakes);
    }

    [Fact]
    public void Reads_all_variables()
    {
        var o = ShipitOptions.FromLookup(Lookup(new()
        {
            [ShipitOptions.DatabaseUrlVar] = "postgres://u:p@h/db",
            [ShipitOptions.MigrationDatabaseUrlVar] = "postgres://migrator:p@h/db",
            [ShipitOptions.AutoMigrateVar] = "true",
            [ShipitOptions.MasterKeyVar] = "k",
            [ShipitOptions.AdminUserVar] = "root",
            [ShipitOptions.AdminPasswordVar] = "pw",
            [ShipitOptions.AllowListVar] = "{\"avina-workspace\":[\"avina-catalog\",\"avina-gateway\"],\"devops\":[\"shipit-demo\"]}",
            [ShipitOptions.UseFakesKey] = "true",
        }));
        Assert.Equal("postgres://u:p@h/db", o.DatabaseUrl);
        Assert.Equal("postgres://migrator:p@h/db", o.MigrationDatabaseUrl);
        Assert.True(o.AutoMigrate);
        Assert.Equal("root", o.AdminUser);
        Assert.True(o.UseFakes);
        Assert.Equal(2, o.DeployAllowList.Count);
        Assert.Contains(o.DeployAllowList, e => e.Namespace == "devops" && e.Deployments.SequenceEqual(["shipit-demo"]));
    }

    [Fact]
    public void Migration_url_falls_back_to_database_url_when_unset_or_blank()
    {
        var fallback = ShipitOptions.FromLookup(Lookup(new()
        {
            [ShipitOptions.DatabaseUrlVar] = "postgres://app:p@h/db",
        }));
        Assert.Equal("postgres://app:p@h/db", fallback.MigrationDatabaseUrl);

        var blankFallback = ShipitOptions.FromLookup(Lookup(new()
        {
            [ShipitOptions.DatabaseUrlVar] = "postgres://app:p@h/db",
            [ShipitOptions.MigrationDatabaseUrlVar] = "   ",
        }));
        Assert.Equal("postgres://app:p@h/db", blankFallback.MigrationDatabaseUrl);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("false", null, false)]
    [InlineData("FALSE", null, false)]
    [InlineData("true", null, true)]
    [InlineData("True", null, true)]
    [InlineData(null, "true", true)]
    [InlineData("false", "true", false)]
    public void AutoMigrate_parses_env_and_config_key_with_false_default(string? envVal, string? keyVal, bool expected)
    {
        var dict = new Dictionary<string, string?>();
        if (envVal is not null) dict[ShipitOptions.AutoMigrateVar] = envVal;
        if (keyVal is not null) dict[ShipitOptions.AutoMigrateKey] = keyVal;

        var o = ShipitOptions.FromLookup(Lookup(dict));
        Assert.Equal(expected, o.AutoMigrate);
    }

    [Fact]
    public void Invalid_allowlist_json_throws()
    {
        Assert.ThrowsAny<Exception>(() => ShipitOptions.ParseAllowList("[1,2]"));
    }
}
