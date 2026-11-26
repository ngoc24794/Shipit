using System.Net;
using System.Text.Json;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// Reconciles per-namespace Kubernetes RBAC (<c>Role</c> and <c>RoleBinding</c> named <c>shipit-deployer</c>)
/// so Shipit's ServiceAccount has least-privilege permissions matching the allowlist.
/// </summary>
public sealed class KubernetesRbacSync : IK8sRbacSync
{
    public const string ManagedResourceName = "shipit-deployer";
    public const string ManagedByLabelKey = "app.kubernetes.io/managed-by";
    public const string ManagedByLabelValue = "shipit";
    public const string DefaultServiceAccountName = "shipit";
    public const string DefaultServiceAccountNamespace = "default";
    public const string ServiceAccountNamespaceEnvVar = "SHIPIT_SERVICE_ACCOUNT_NAMESPACE";
    public const string ServiceAccountNameEnvVar = "SHIPIT_SERVICE_ACCOUNT_NAME";
    public const string InClusterNamespacePath = "/var/run/secrets/kubernetes.io/serviceaccount/namespace";
    private const string RbacApiGroup = "rbac.authorization.k8s.io";

    private readonly IKubernetes _client;
    private readonly string _serviceAccountName;
    private readonly string _serviceAccountNamespace;

    public KubernetesRbacSync(IKubernetes client, string serviceAccountName, string serviceAccountNamespace)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceAccountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceAccountNamespace);
        _serviceAccountName = serviceAccountName.Trim();
        _serviceAccountNamespace = serviceAccountNamespace.Trim();
    }

    public string ServiceAccountName => _serviceAccountName;
    public string ServiceAccountNamespace => _serviceAccountNamespace;

    /// <summary>
    /// Creates an RBAC syncer configured for running inside a Kubernetes cluster.
    /// Only call when running in-cluster; do not call in offline unit tests.
    /// </summary>
    public static KubernetesRbacSync CreateInCluster(
        Func<string, string?>? envLookup = null,
        string? serviceAccountNamespaceFile = null)
    {
        var config = KubernetesClientConfiguration.InClusterConfig();
        var client = new Kubernetes(config);
        var (saName, saNamespace) = ResolveServiceAccountIdentity(envLookup, serviceAccountNamespaceFile);
        return new KubernetesRbacSync(client, saName, saNamespace);
    }

    /// <summary>
    /// Resolves Shipit's own ServiceAccount name and namespace from environment variables
    /// and the standard in-cluster namespace file.
    /// </summary>
    public static (string Name, string Namespace) ResolveServiceAccountIdentity(
        Func<string, string?>? envLookup = null,
        string? serviceAccountNamespaceFile = null)
    {
        var getEnv = envLookup ?? Environment.GetEnvironmentVariable;
        var nsFile = serviceAccountNamespaceFile ?? InClusterNamespacePath;

        var saName = getEnv(ServiceAccountNameEnvVar);
        if (string.IsNullOrWhiteSpace(saName))
        {
            saName = DefaultServiceAccountName;
        }
        else
        {
            saName = saName.Trim();
        }

        var saNamespace = getEnv(ServiceAccountNamespaceEnvVar);
        if (!string.IsNullOrWhiteSpace(saNamespace))
        {
            saNamespace = saNamespace.Trim();
        }
        else if (File.Exists(nsFile))
        {
            var fileContent = File.ReadAllText(nsFile).Trim();
            saNamespace = string.IsNullOrWhiteSpace(fileContent) ? DefaultServiceAccountNamespace : fileContent;
        }
        else
        {
            saNamespace = DefaultServiceAccountNamespace;
        }

        return (saName, saNamespace);
    }

    public async Task<RbacSyncResult> SyncNamespaceAsync(
        string targetNamespace,
        IReadOnlyCollection<string> allowedDeployments,
        bool allowJobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNamespace);
        var ns = targetNamespace.Trim();

        var normalizedDeployments = (allowedDeployments ?? Array.Empty<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        try
        {
            if (normalizedDeployments.Count == 0 && !allowJobs)
            {
                await DeleteIfExistsAsync(
                    ct => _client.RbacAuthorizationV1.DeleteNamespacedRoleBindingAsync(
                        ManagedResourceName, ns, cancellationToken: ct),
                    cancellationToken);

                await DeleteIfExistsAsync(
                    ct => _client.RbacAuthorizationV1.DeleteNamespacedRoleAsync(
                        ManagedResourceName, ns, cancellationToken: ct),
                    cancellationToken);

                return new RbacSyncResult(true);
            }

            var desiredRole = BuildRole(ns, normalizedDeployments, allowJobs);
            await UpsertRoleAsync(ns, desiredRole, cancellationToken);

            var desiredBinding = BuildRoleBinding(ns);
            await UpsertRoleBindingAsync(ns, desiredBinding, cancellationToken);

            return new RbacSyncResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpOperationException ex)
        {
            return new RbacSyncResult(false, FormatHttpError(ex));
        }
        catch (HttpRequestException ex)
        {
            return new RbacSyncResult(false, $"Kubernetes request failed: {ex.Message}");
        }
    }

    private static V1Role BuildRole(string ns, List<string> sortedDeployments, bool allowJobs)
    {
        var rules = new List<V1PolicyRule>
        {
            new()
            {
                ApiGroups = [""],
                Resources = ["pods"],
                Verbs = ["get", "list", "watch"]
            },
            new()
            {
                ApiGroups = [""],
                Resources = ["pods/log"],
                Verbs = ["get"]
            }
        };

        if (sortedDeployments.Count > 0)
        {
            rules.Add(new V1PolicyRule
            {
                ApiGroups = ["apps"],
                Resources = ["deployments"],
                Verbs = ["get", "list", "watch"]
            });
            rules.Add(new V1PolicyRule
            {
                ApiGroups = ["apps"],
                Resources = ["replicasets"],
                Verbs = ["get", "list", "watch"]
            });
            rules.Add(new V1PolicyRule
            {
                ApiGroups = ["apps"],
                Resources = ["deployments"],
                ResourceNames = sortedDeployments,
                Verbs = ["patch"]
            });
        }

        if (allowJobs)
        {
            rules.Add(new V1PolicyRule
            {
                ApiGroups = ["batch"],
                Resources = ["jobs"],
                Verbs = ["create", "get", "list", "watch", "delete"]
            });
        }

        return new V1Role
        {
            ApiVersion = $"{RbacApiGroup}/v1",
            Kind = "Role",
            Metadata = new V1ObjectMeta
            {
                Name = ManagedResourceName,
                NamespaceProperty = ns,
                Labels = new Dictionary<string, string>
                {
                    [ManagedByLabelKey] = ManagedByLabelValue
                }
            },
            Rules = rules
        };
    }

    private V1RoleBinding BuildRoleBinding(string ns) =>
        new()
        {
            ApiVersion = $"{RbacApiGroup}/v1",
            Kind = "RoleBinding",
            Metadata = new V1ObjectMeta
            {
                Name = ManagedResourceName,
                NamespaceProperty = ns,
                Labels = new Dictionary<string, string>
                {
                    [ManagedByLabelKey] = ManagedByLabelValue
                }
            },
            RoleRef = new V1RoleRef
            {
                ApiGroup = RbacApiGroup,
                Kind = "Role",
                Name = ManagedResourceName
            },
            Subjects =
            [
                new Rbacv1Subject
                {
                    ApiGroup = "",
                    Kind = "ServiceAccount",
                    Name = _serviceAccountName,
                    NamespaceProperty = _serviceAccountNamespace
                }
            ]
        };

    private async Task UpsertRoleAsync(string ns, V1Role desiredRole, CancellationToken cancellationToken)
    {
        var existing = await TryReadAsync(
            ct => _client.RbacAuthorizationV1.ReadNamespacedRoleAsync(ManagedResourceName, ns, cancellationToken: ct),
            cancellationToken);

        if (existing is null)
        {
            try
            {
                await _client.RbacAuthorizationV1.CreateNamespacedRoleAsync(desiredRole, ns, cancellationToken: cancellationToken);
                return;
            }
            catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.Conflict)
            {
                existing = await _client.RbacAuthorizationV1.ReadNamespacedRoleAsync(
                    ManagedResourceName, ns, cancellationToken: cancellationToken);
            }
        }

        desiredRole.Metadata.ResourceVersion = existing.Metadata?.ResourceVersion;
        await _client.RbacAuthorizationV1.ReplaceNamespacedRoleAsync(
            desiredRole, ManagedResourceName, ns, cancellationToken: cancellationToken);
    }

    private async Task UpsertRoleBindingAsync(string ns, V1RoleBinding desiredBinding, CancellationToken cancellationToken)
    {
        var existing = await TryReadAsync(
            ct => _client.RbacAuthorizationV1.ReadNamespacedRoleBindingAsync(ManagedResourceName, ns, cancellationToken: ct),
            cancellationToken);

        if (existing is null)
        {
            try
            {
                await _client.RbacAuthorizationV1.CreateNamespacedRoleBindingAsync(
                    desiredBinding, ns, cancellationToken: cancellationToken);
                return;
            }
            catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.Conflict)
            {
                existing = await _client.RbacAuthorizationV1.ReadNamespacedRoleBindingAsync(
                    ManagedResourceName, ns, cancellationToken: cancellationToken);
            }
        }

        var roleRefMatches =
            string.Equals(existing.RoleRef?.ApiGroup, desiredBinding.RoleRef.ApiGroup, StringComparison.Ordinal) &&
            string.Equals(existing.RoleRef?.Kind, desiredBinding.RoleRef.Kind, StringComparison.Ordinal) &&
            string.Equals(existing.RoleRef?.Name, desiredBinding.RoleRef.Name, StringComparison.Ordinal);

        if (!roleRefMatches)
        {
            // roleRef is immutable in Kubernetes; delete and recreate the RoleBinding.
            await DeleteIfExistsAsync(
                ct => _client.RbacAuthorizationV1.DeleteNamespacedRoleBindingAsync(
                    ManagedResourceName, ns, cancellationToken: ct),
                cancellationToken);

            await _client.RbacAuthorizationV1.CreateNamespacedRoleBindingAsync(
                desiredBinding, ns, cancellationToken: cancellationToken);
            return;
        }

        desiredBinding.Metadata.ResourceVersion = existing.Metadata?.ResourceVersion;
        await _client.RbacAuthorizationV1.ReplaceNamespacedRoleBindingAsync(
            desiredBinding, ManagedResourceName, ns, cancellationToken: cancellationToken);
    }

    private static async Task<T?> TryReadAsync<T>(Func<CancellationToken, Task<T>> readOp, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await readOp(cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task DeleteIfExistsAsync(Func<CancellationToken, Task> deleteOp, CancellationToken cancellationToken)
    {
        try
        {
            await deleteOp(cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.NotFound)
        {
            // Already absent.
        }
    }

    private static string FormatHttpError(HttpOperationException ex)
    {
        var statusCode = (int)(ex.Response?.StatusCode ?? 0);
        var phrase = string.IsNullOrWhiteSpace(ex.Response?.ReasonPhrase) ? null : ex.Response.ReasonPhrase.Trim();
        var k8sMessage = TryExtractStatusMessage(ex.Response?.Content);

        if (k8sMessage != null)
        {
            return phrase != null
                ? $"Kubernetes API error: HTTP {statusCode} ({phrase}): {k8sMessage}"
                : $"Kubernetes API error: HTTP {statusCode}: {k8sMessage}";
        }

        return phrase != null
            ? $"Kubernetes API error: HTTP {statusCode} ({phrase})"
            : $"Kubernetes API error: HTTP {statusCode}";
    }

    private static string? TryExtractStatusMessage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var msgProp) &&
                msgProp.ValueKind == JsonValueKind.String)
            {
                var msg = msgProp.GetString();
                return string.IsNullOrWhiteSpace(msg) ? null : msg.Trim();
            }
        }
        catch
        {
            // Ignore non-JSON error bodies.
        }

        return null;
    }
}
