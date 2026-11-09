using Shipit.Core;
using Xunit;

namespace Shipit.K8s.Tests;

public class DeployAllowListTests
{
    [Fact]
    public void IsAllowed_PermitsConfiguredNamespaceAndDeployment()
    {
        var entries = new[]
        {
            new DeployAllowListEntry("devops", ["shipit-demo", "web"]),
            new DeployAllowListEntry("staging", ["backend"]),
        };
        var allowList = new DeployAllowList(entries);

        Assert.True(allowList.IsAllowed("devops", "shipit-demo"));
        Assert.True(allowList.IsAllowed("devops", "web"));
        Assert.True(allowList.IsAllowed("staging", "backend"));
    }

    [Fact]
    public void IsAllowed_RejectsUnconfiguredDeploymentInAllowedNamespace()
    {
        var entries = new[]
        {
            new DeployAllowListEntry("devops", ["shipit-demo"]),
        };
        var allowList = new DeployAllowList(entries);

        Assert.False(allowList.IsAllowed("devops", "unauthorized-deployment"));
    }

    [Fact]
    public void IsAllowed_RejectsUnconfiguredNamespace()
    {
        var entries = new[]
        {
            new DeployAllowListEntry("devops", ["shipit-demo"]),
        };
        var allowList = new DeployAllowList(entries);

        Assert.False(allowList.IsAllowed("production", "shipit-demo"));
    }

    [Fact]
    public void IsAllowed_EmptyAllowList_RejectsEverything()
    {
        var allowList = new DeployAllowList(Array.Empty<DeployAllowListEntry>());

        Assert.False(allowList.IsAllowed("devops", "shipit-demo"));
        Assert.False(allowList.IsAllowed("default", "web"));
    }

    [Fact]
    public void IsAllowed_NullEntries_RejectsEverything()
    {
        var allowList = new DeployAllowList(null);

        Assert.False(allowList.IsAllowed("devops", "shipit-demo"));
    }

    [Fact]
    public void IsAllowed_CaseSensitive_ForNamespaceAndDeployment()
    {
        var entries = new[]
        {
            new DeployAllowListEntry("devops", ["shipit-demo"]),
        };
        var allowList = new DeployAllowList(entries);

        Assert.True(allowList.IsAllowed("devops", "shipit-demo"));
        Assert.False(allowList.IsAllowed("Devops", "shipit-demo"));
        Assert.False(allowList.IsAllowed("DEVOPS", "shipit-demo"));
        Assert.False(allowList.IsAllowed("devops", "Shipit-Demo"));
        Assert.False(allowList.IsAllowed("devops", "SHIPIT-DEMO"));
    }

    [Theory]
    [InlineData("", "dep")]
    [InlineData("ns", "")]
    [InlineData("", "")]
    public void IsAllowed_EmptyOrNullInputs_ReturnsFalse(string ns, string dep)
    {
        var entries = new[]
        {
            new DeployAllowListEntry("ns", ["dep"]),
        };
        var allowList = new DeployAllowList(entries);

        Assert.False(allowList.IsAllowed(ns, dep));
    }
}
