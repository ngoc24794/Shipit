using Shipit.Core;
using Xunit;

namespace Shipit.K8s.Tests;

public class JobAllowListTests
{
    [Fact]
    public async Task EmptyAllowList_RejectsEverything()
    {
        var list = new JobAllowList();

        Assert.False(await list.IsNamespaceAllowedAsync("devops"));
        Assert.False(list.IsNamespaceAllowed("devops"));

        Assert.False(await list.IsConfigMapAllowedAsync("devops", "my-config"));
        Assert.False(list.IsConfigMapAllowed("devops", "my-config"));

        Assert.False(await list.IsSecretAllowedAsync("devops", "my-secret"));
        Assert.False(list.IsSecretAllowed("devops", "my-secret"));

        Assert.False(await list.IsPullSecretAllowedAsync("devops", "my-pull"));
        Assert.False(list.IsPullSecretAllowed("devops", "my-pull"));
    }

    [Fact]
    public async Task AllowedResources_ReturnTrue_WhenMatchingExactCase()
    {
        var list = new JobAllowList(
            allowedNamespaces: ["devops", "avina-workspace"],
            allowedConfigMaps: [("devops", "app-config"), ("avina-workspace", "app-config")],
            allowedSecrets: [("devops", "app-secret"), ("avina-workspace", "db-pass")],
            allowedPullSecrets: [("devops", "regcred")]);

        // Allowed
        Assert.True(await list.IsNamespaceAllowedAsync("devops"));
        Assert.True(list.IsNamespaceAllowed("avina-workspace"));

        Assert.True(await list.IsConfigMapAllowedAsync("devops", "app-config"));
        Assert.True(list.IsConfigMapAllowed("avina-workspace", "app-config"));

        Assert.True(await list.IsSecretAllowedAsync("devops", "app-secret"));
        Assert.True(list.IsSecretAllowed("avina-workspace", "db-pass"));

        Assert.True(await list.IsPullSecretAllowedAsync("devops", "regcred"));
        Assert.True(list.IsPullSecretAllowed("devops", "regcred"));

        // Case sensitivity: exact matching
        Assert.False(await list.IsNamespaceAllowedAsync("Devops"));
        Assert.False(await list.IsConfigMapAllowedAsync("devops", "APP-CONFIG"));
        Assert.False(await list.IsSecretAllowedAsync("devops", "App-Secret"));
        Assert.False(await list.IsPullSecretAllowedAsync("devops", "Regcred"));

        // Cross-namespace isolation
        Assert.False(await list.IsSecretAllowedAsync("devops", "db-pass"));
        Assert.False(await list.IsPullSecretAllowedAsync("avina-workspace", "regcred"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NullOrEmptyInputs_ReturnFalse(string? empty)
    {
        var list = new JobAllowList(
            allowedNamespaces: ["devops"],
            allowedConfigMaps: [("devops", "cm")]);

        Assert.False(await list.IsNamespaceAllowedAsync(empty!));
        Assert.False(list.IsNamespaceAllowed(empty!));

        Assert.False(await list.IsConfigMapAllowedAsync("devops", empty!));
        Assert.False(await list.IsConfigMapAllowedAsync(empty!, "cm"));
    }

    [Fact]
    public async Task BackedByStore_DelegatesToStoreProperly()
    {
        var store = new MockJobAllowStore(
            allowedNs: ["devops"],
            allowedRefs: [("devops", "configmap", "cm1"), ("devops", "secret", "sec1"), ("devops", "pullsecret", "reg1")]);

        var list = new JobAllowList(store);

        Assert.True(await list.IsNamespaceAllowedAsync("devops"));
        Assert.False(await list.IsNamespaceAllowedAsync("other"));

        Assert.True(await list.IsConfigMapAllowedAsync("devops", "cm1"));
        Assert.False(await list.IsConfigMapAllowedAsync("devops", "cm2"));

        Assert.True(await list.IsSecretAllowedAsync("devops", "sec1"));
        Assert.False(await list.IsSecretAllowedAsync("devops", "sec2"));

        Assert.True(await list.IsPullSecretAllowedAsync("devops", "reg1"));
        Assert.False(await list.IsPullSecretAllowedAsync("devops", "reg2"));
    }

    private sealed class MockJobAllowStore : IJobAllowStore
    {
        private readonly HashSet<string> _allowedNs;
        private readonly HashSet<(string Ns, string Kind, string Name)> _allowedRefs;

        public MockJobAllowStore(IEnumerable<string> allowedNs, IEnumerable<(string Ns, string Kind, string Name)> allowedRefs)
        {
            _allowedNs = new HashSet<string>(allowedNs, StringComparer.Ordinal);
            _allowedRefs = new HashSet<(string Ns, string Kind, string Name)>(allowedRefs);
        }

        public Task<bool> IsNamespaceAllowedAsync(string ns, CancellationToken cancellationToken = default) =>
            Task.FromResult(_allowedNs.Contains(ns));

        public Task<bool> IsReferenceAllowedAsync(string ns, string kind, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_allowedRefs.Contains((ns, kind, name)));
    }
}
