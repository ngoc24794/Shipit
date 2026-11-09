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
            [ShipitOptions.MasterKeyVar] = "k",
            [ShipitOptions.AdminUserVar] = "root",
            [ShipitOptions.AdminPasswordVar] = "pw",
            [ShipitOptions.AllowListVar] = "{\"avina-workspace\":[\"avina-catalog\",\"avina-gateway\"],\"devops\":[\"shipit-demo\"]}",
            [ShipitOptions.UseFakesKey] = "true",
        }));
        Assert.Equal("postgres://u:p@h/db", o.DatabaseUrl);
        Assert.Equal("root", o.AdminUser);
        Assert.True(o.UseFakes);
        Assert.Equal(2, o.DeployAllowList.Count);
        Assert.Contains(o.DeployAllowList, e => e.Namespace == "devops" && e.Deployments.SequenceEqual(["shipit-demo"]));
    }

    [Fact]
    public void Invalid_allowlist_json_throws()
    {
        Assert.ThrowsAny<Exception>(() => ShipitOptions.ParseAllowList("[1,2]"));
    }
}
