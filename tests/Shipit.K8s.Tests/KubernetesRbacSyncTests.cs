using System.Net;
using System.Text.Json;
using k8s.Models;
using Shipit.Core;
using Xunit;

namespace Shipit.K8s.Tests;

public class KubernetesRbacSyncTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static KubernetesRbacSync CreateSync(MockHttpMessageHandler handler, string saName = "shipit", string saNs = "cicd")
    {
        var client = MockHttpMessageHandler.CreateClient(handler);
        return new KubernetesRbacSync(client, saName, saNs);
    }

    [Fact]
    public async Task CreatesRoleAndRoleBinding_ForDeployTargetsOnly()
    {
        V1Role? createdRole = null;
        V1RoleBinding? createdBinding = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/roles/shipit-deployer", StringComparison.Ordinal))
            {
                return MockHttpMessageHandler.ErrorResponse(HttpStatusCode.NotFound, "Not Found", "roles.rbac.authorization.k8s.io \"shipit-deployer\" not found");
            }
            if (req.Method == HttpMethod.Post && path.EndsWith("/roles", StringComparison.Ordinal))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                createdRole = JsonSerializer.Deserialize<V1Role>(body, JsonOptions)!;
                return MockHttpMessageHandler.JsonResponse(createdRole, HttpStatusCode.Created);
            }
            if (req.Method == HttpMethod.Get && path.EndsWith("/rolebindings/shipit-deployer", StringComparison.Ordinal))
            {
                return MockHttpMessageHandler.ErrorResponse(HttpStatusCode.NotFound, "Not Found", "rolebindings.rbac.authorization.k8s.io \"shipit-deployer\" not found");
            }
            if (req.Method == HttpMethod.Post && path.EndsWith("/rolebindings", StringComparison.Ordinal))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                createdBinding = JsonSerializer.Deserialize<V1RoleBinding>(body, JsonOptions)!;
                return MockHttpMessageHandler.JsonResponse(createdBinding, HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {path}");
        });

        var sync = CreateSync(handler);
        var result = await sync.SyncNamespaceAsync("app-ns", ["web", "api", "web", " "], allowJobs: false);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Null(result.ErrorMessage);

        Assert.NotNull(createdRole);
        Assert.Equal("shipit-deployer", createdRole!.Metadata.Name);
        Assert.Equal("shipit", createdRole.Metadata.Labels["app.kubernetes.io/managed-by"]);
        Assert.Equal(5, createdRole.Rules.Count);
        Assert.Contains(createdRole.Rules, r => r.ApiGroups.SequenceEqual([""]) && r.Resources.SequenceEqual(["pods"]) && r.Verbs.SequenceEqual(["get", "list", "watch"]));
        Assert.Contains(createdRole.Rules, r => r.ApiGroups.SequenceEqual([""]) && r.Resources.SequenceEqual(["pods/log"]) && r.Verbs.SequenceEqual(["get"]));
        Assert.Contains(createdRole.Rules, r => r.ApiGroups.SequenceEqual(["apps"]) && r.Resources.SequenceEqual(["deployments"]) && r.Verbs.SequenceEqual(["get", "list", "watch"]) && r.ResourceNames == null);
        Assert.Contains(createdRole.Rules, r => r.ApiGroups.SequenceEqual(["apps"]) && r.Resources.SequenceEqual(["replicasets"]) && r.Verbs.SequenceEqual(["get", "list", "watch"]));
        Assert.Contains(createdRole.Rules, r => r.ApiGroups.SequenceEqual(["apps"]) && r.Resources.SequenceEqual(["deployments"]) && r.Verbs.SequenceEqual(["patch"]) && r.ResourceNames != null && r.ResourceNames.SequenceEqual(["api", "web"]));
        Assert.DoesNotContain(createdRole.Rules, r => r.ApiGroups.Contains("batch"));

        Assert.NotNull(createdBinding);
        Assert.Equal("shipit-deployer", createdBinding!.Metadata.Name);
        Assert.Equal("shipit", createdBinding.Metadata.Labels["app.kubernetes.io/managed-by"]);
        Assert.Equal("Role", createdBinding.RoleRef.Kind);
        Assert.Equal("shipit-deployer", createdBinding.RoleRef.Name);
        Assert.Equal("rbac.authorization.k8s.io", createdBinding.RoleRef.ApiGroup);
        var subject = Assert.Single(createdBinding.Subjects);
        Assert.Equal("ServiceAccount", subject.Kind);
        Assert.Equal("shipit", subject.Name);
        Assert.Equal("cicd", subject.NamespaceProperty);
    }

    [Fact]
    public async Task CreatesRoleAndRoleBinding_ForJobNamespaceOnly()
    {
        V1Role? createdRole = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get)
            {
                return MockHttpMessageHandler.ErrorResponse(HttpStatusCode.NotFound, "Not Found", "not found");
            }
            if (req.Method == HttpMethod.Post && path.EndsWith("/roles", StringComparison.Ordinal))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                createdRole = JsonSerializer.Deserialize<V1Role>(body, JsonOptions)!;
                return MockHttpMessageHandler.JsonResponse(createdRole, HttpStatusCode.Created);
            }
            if (req.Method == HttpMethod.Post && path.EndsWith("/rolebindings", StringComparison.Ordinal))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var binding = JsonSerializer.Deserialize<V1RoleBinding>(body, JsonOptions)!;
                return MockHttpMessageHandler.JsonResponse(binding, HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {path}");
        });

        var sync = CreateSync(handler);
        var result = await sync.SyncNamespaceAsync("jobs-ns", [], allowJobs: true);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(createdRole);
        Assert.Equal(3, createdRole!.Rules.Count);
        Assert.Contains(createdRole.Rules, r => r.ApiGroups.SequenceEqual(["batch"]) && r.Resources.SequenceEqual(["jobs"]) && r.Verbs.SequenceEqual(["create", "get", "list", "watch", "delete"]));
        Assert.DoesNotContain(createdRole.Rules, r => r.ApiGroups.Contains("apps"));
    }

    [Fact]
    public async Task CreatesCombinedRoleAndRoleBinding_ForDeployTargetsAndJobs()
    {
        V1Role? createdRole = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get)
            {
                return MockHttpMessageHandler.ErrorResponse(HttpStatusCode.NotFound, "Not Found", "not found");
            }
            if (req.Method == HttpMethod.Post && path.EndsWith("/roles", StringComparison.Ordinal))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                createdRole = JsonSerializer.Deserialize<V1Role>(body, JsonOptions)!;
                return MockHttpMessageHandler.JsonResponse(createdRole, HttpStatusCode.Created);
            }
            if (req.Method == HttpMethod.Post && path.EndsWith("/rolebindings", StringComparison.Ordinal))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var binding = JsonSerializer.Deserialize<V1RoleBinding>(body, JsonOptions)!;
                return MockHttpMessageHandler.JsonResponse(binding, HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {path}");
        });

        var sync = CreateSync(handler);
        var result = await sync.SyncNamespaceAsync("avina-workspace", ["avina-catalog"], allowJobs: true);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(createdRole);
        Assert.Equal(6, createdRole!.Rules.Count);
        Assert.Contains(createdRole.Rules, r => r.ApiGroups.Contains("batch") && r.Resources.Contains("jobs"));
        Assert.Contains(createdRole.Rules, r => r.ApiGroups.Contains("apps") && r.ResourceNames != null && r.ResourceNames.SequenceEqual(["avina-catalog"]));
    }

    [Fact]
    public async Task UpdatesExistingRole_WhenDeploymentsAddedOrRemoved()
    {
        V1Role? replacedRole = null;

        var existingRole = new V1Role
        {
            Metadata = new V1ObjectMeta { Name = "shipit-deployer", NamespaceProperty = "app-ns", ResourceVersion = "42" },
            Rules = []
        };
        var existingBinding = new V1RoleBinding
        {
            Metadata = new V1ObjectMeta { Name = "shipit-deployer", NamespaceProperty = "app-ns", ResourceVersion = "99" },
            RoleRef = new V1RoleRef { ApiGroup = "rbac.authorization.k8s.io", Kind = "Role", Name = "shipit-deployer" },
            Subjects = []
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/roles/shipit-deployer", StringComparison.Ordinal))
            {
                return MockHttpMessageHandler.JsonResponse(existingRole);
            }
            if (req.Method == HttpMethod.Put && path.EndsWith("/roles/shipit-deployer", StringComparison.Ordinal))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                replacedRole = JsonSerializer.Deserialize<V1Role>(body, JsonOptions)!;
                return MockHttpMessageHandler.JsonResponse(replacedRole);
            }
            if (req.Method == HttpMethod.Get && path.EndsWith("/rolebindings/shipit-deployer", StringComparison.Ordinal))
            {
                return MockHttpMessageHandler.JsonResponse(existingBinding);
            }
            if (req.Method == HttpMethod.Put && path.EndsWith("/rolebindings/shipit-deployer", StringComparison.Ordinal))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var replaced = JsonSerializer.Deserialize<V1RoleBinding>(body, JsonOptions)!;
                Assert.Equal("99", replaced.Metadata.ResourceVersion);
                return MockHttpMessageHandler.JsonResponse(replaced);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {path}");
        });

        var sync = CreateSync(handler);
        var result = await sync.SyncNamespaceAsync("app-ns", ["svc-b", "svc-a"], allowJobs: false);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(replacedRole);
        Assert.Equal("42", replacedRole!.Metadata.ResourceVersion);
        var patchRule = Assert.Single(replacedRole.Rules, r => r.Verbs.Contains("patch"));
        Assert.Equal(["svc-a", "svc-b"], patchRule.ResourceNames);
    }

    [Fact]
    public async Task RecreatesRoleBinding_WhenRoleRefDiffers()
    {
        var deletedBinding = false;
        var createdBinding = false;

        var existingRole = new V1Role
        {
            Metadata = new V1ObjectMeta { Name = "shipit-deployer", NamespaceProperty = "app-ns", ResourceVersion = "1" }
        };
        var existingBinding = new V1RoleBinding
        {
            Metadata = new V1ObjectMeta { Name = "shipit-deployer", NamespaceProperty = "app-ns", ResourceVersion = "2" },
            RoleRef = new V1RoleRef { ApiGroup = "rbac.authorization.k8s.io", Kind = "Role", Name = "legacy-role" },
            Subjects = []
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/roles/shipit-deployer", StringComparison.Ordinal))
            {
                return MockHttpMessageHandler.JsonResponse(existingRole);
            }
            if (req.Method == HttpMethod.Put && path.EndsWith("/roles/shipit-deployer", StringComparison.Ordinal))
            {
                return MockHttpMessageHandler.JsonResponse(existingRole);
            }
            if (req.Method == HttpMethod.Get && path.EndsWith("/rolebindings/shipit-deployer", StringComparison.Ordinal))
            {
                return MockHttpMessageHandler.JsonResponse(existingBinding);
            }
            if (req.Method == HttpMethod.Delete && path.EndsWith("/rolebindings/shipit-deployer", StringComparison.Ordinal))
            {
                deletedBinding = true;
                return MockHttpMessageHandler.JsonResponse(new V1Status { Status = "Success" });
            }
            if (req.Method == HttpMethod.Post && path.EndsWith("/rolebindings", StringComparison.Ordinal))
            {
                createdBinding = true;
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return MockHttpMessageHandler.JsonResponse(JsonSerializer.Deserialize<V1RoleBinding>(body, JsonOptions)!, HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {path}");
        });

        var sync = CreateSync(handler);
        var result = await sync.SyncNamespaceAsync("app-ns", ["svc-a"], allowJobs: false);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(deletedBinding);
        Assert.True(createdBinding);
    }

    [Fact]
    public async Task DeletesRoleBindingAndRole_WhenEmptyAndNoJobs_AndIgnores404()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Delete)
            {
                return MockHttpMessageHandler.ErrorResponse(HttpStatusCode.NotFound, "Not Found", "not found");
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var sync = CreateSync(handler);
        var result = await sync.SyncNamespaceAsync("app-ns", [], allowJobs: false);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Delete && r.RequestUri.AbsolutePath.EndsWith("/rolebindings/shipit-deployer", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Delete && r.RequestUri.AbsolutePath.EndsWith("/roles/shipit-deployer", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden", "roles.rbac.authorization.k8s.io is forbidden")]
    [InlineData(HttpStatusCode.NotFound, "Not Found", "namespaces \"missing-ns\" not found")]
    public async Task HandlesK8sApiError_ReturnsFailedResultWithErrorMessage(
        HttpStatusCode statusCode,
        string reasonPhrase,
        string k8sMessage)
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return MockHttpMessageHandler.ErrorResponse(HttpStatusCode.NotFound, "Not Found", "role not found");
            }
            return MockHttpMessageHandler.ErrorResponse(statusCode, reasonPhrase, k8sMessage);
        });

        var sync = CreateSync(handler);
        var result = await sync.SyncNamespaceAsync("missing-ns", ["web"], allowJobs: false);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains(((int)statusCode).ToString(), result.ErrorMessage);
        Assert.Contains(k8sMessage, result.ErrorMessage);
    }

    [Fact]
    public void ResolveServiceAccountIdentity_UsesEnvVarsAndNamespaceFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "  in-cluster-ns \n");

            var (name1, ns1) = KubernetesRbacSync.ResolveServiceAccountIdentity(
                _ => null,
                tempFile);
            Assert.Equal("shipit", name1);
            Assert.Equal("in-cluster-ns", ns1);

            var (name2, ns2) = KubernetesRbacSync.ResolveServiceAccountIdentity(
                key => key switch
                {
                    KubernetesRbacSync.ServiceAccountNameEnvVar => "custom-sa",
                    KubernetesRbacSync.ServiceAccountNamespaceEnvVar => "custom-ns",
                    _ => null
                },
                tempFile);
            Assert.Equal("custom-sa", name2);
            Assert.Equal("custom-ns", ns2);

            var (name3, ns3) = KubernetesRbacSync.ResolveServiceAccountIdentity(
                _ => null,
                Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));
            Assert.Equal("shipit", name3);
            Assert.Equal("default", ns3);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
