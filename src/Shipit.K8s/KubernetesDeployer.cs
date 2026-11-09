using System.Text.RegularExpressions;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// Deploys new container images to Kubernetes deployments and waits for rollouts.
/// </summary>
public sealed partial class KubernetesDeployer : IK8sDeployer
{
    private static readonly Regex Dns1123Regex = CreateDns1123Regex();

    private readonly IKubernetes _client;
    private readonly DeployAllowList _allowList;
    private readonly TimeSpan _pollInterval;
    private readonly TimeProvider _timeProvider;

    public KubernetesDeployer(
        IKubernetes client,
        DeployAllowList allowList,
        TimeSpan? pollInterval = null,
        TimeProvider? timeProvider = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _allowList = allowList ?? throw new ArgumentNullException(nameof(allowList));
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Creates a deployer configured for running inside a Kubernetes cluster.
    /// Only call when running in-cluster; do not call in offline unit tests.
    /// </summary>
    public static KubernetesDeployer CreateInCluster(DeployAllowList allowList)
    {
        var config = KubernetesClientConfiguration.InClusterConfig();
        var client = new Kubernetes(config);
        return new KubernetesDeployer(client, allowList);
    }

    public async Task<DeployResult> SetImageAsync(DeployRequest request, ILogSink log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(log);

        // 1. Validation
        var validationError = ValidateRequest(request);
        if (validationError != null)
        {
            return new DeployResult(false, validationError);
        }

        // 2. Allowlist check (reject immediately without calling cluster)
        if (!await _allowList.IsAllowedAsync(request.Namespace, request.Deployment, cancellationToken))
        {
            return new DeployResult(false, $"Deployment '{request.Deployment}' in namespace '{request.Namespace}' is not allowed.");
        }

        // 3. GET Deployment
        V1Deployment deployment;
        try
        {
            deployment = await _client.AppsV1.ReadNamespacedDeploymentAsync(
                request.Deployment,
                request.Namespace,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpOperationException ex)
        {
            return FormatHttpError(ex);
        }
        catch (HttpRequestException ex)
        {
            return new DeployResult(false, $"Kubernetes request failed: {ex.Message}");
        }

        var containers = deployment.Spec?.Template?.Spec?.Containers;
        var targetContainer = containers?.FirstOrDefault(c => string.Equals(c.Name, request.Container, StringComparison.Ordinal));
        if (targetContainer == null)
        {
            var availableNames = containers != null && containers.Count > 0
                ? string.Join(", ", containers.Select(c => $"'{c.Name}'"))
                : "none";
            return new DeployResult(false, $"Container '{request.Container}' not found in deployment '{request.Deployment}'. Available containers: [{availableNames}].");
        }

        // 4. Idempotency check: if image is already identical, skip patch and wait for rollout to stabilize
        if (string.Equals(targetContainer.Image, request.Image, StringComparison.Ordinal))
        {
            await log.WriteAsync(
                $"Container '{request.Container}' in deployment '{request.Deployment}' already uses image '{request.Image}'. Waiting for rollout to stabilize.",
                LogStream.System,
                cancellationToken);
        }
        else
        {
            // 5. Strategic merge patch
            var patchJson = $"{{\"spec\":{{\"template\":{{\"spec\":{{\"containers\":[{{\"name\":\"{request.Container}\",\"image\":\"{request.Image}\"}}]}}}}}}}}";
            var patch = new V1Patch(patchJson, V1Patch.PatchType.StrategicMergePatch);
            try
            {
                await _client.AppsV1.PatchNamespacedDeploymentAsync(
                    patch,
                    request.Deployment,
                    request.Namespace,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpOperationException ex)
            {
                return FormatHttpError(ex);
            }
            catch (HttpRequestException ex)
            {
                return new DeployResult(false, $"Kubernetes request failed: {ex.Message}");
            }
        }

        // 6. Poll rollout status
        var startTime = _timeProvider.GetTimestamp();
        string? lastStatus = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            V1Deployment current;
            try
            {
                current = await _client.AppsV1.ReadNamespacedDeploymentAsync(
                    request.Deployment,
                    request.Namespace,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpOperationException ex)
            {
                return FormatHttpError(ex);
            }
            catch (HttpRequestException ex)
            {
                return new DeployResult(false, $"Kubernetes request failed: {ex.Message}");
            }

            var targetReplicas = current.Spec?.Replicas ?? 1;
            var generation = current.Metadata?.Generation ?? 0;
            var observedGeneration = current.Status?.ObservedGeneration ?? 0;
            var updatedReplicas = current.Status?.UpdatedReplicas ?? 0;
            var availableReplicas = current.Status?.AvailableReplicas ?? 0;
            var unavailableReplicas = current.Status?.UnavailableReplicas;

            var statusLine = $"{updatedReplicas}/{targetReplicas} replicas updated, {availableReplicas}/{targetReplicas} available";
            if (statusLine != lastStatus)
            {
                lastStatus = statusLine;
                await log.WriteAsync(statusLine, LogStream.System, cancellationToken);
            }

            var isRolledOut = observedGeneration >= generation &&
                              updatedReplicas == targetReplicas &&
                              availableReplicas == targetReplicas &&
                              (unavailableReplicas == null || unavailableReplicas.Value == 0);

            if (isRolledOut)
            {
                return new DeployResult(true, $"deployed {request.Image} to {request.Namespace}/{request.Deployment}");
            }

            var elapsed = _timeProvider.GetElapsedTime(startTime);
            if (elapsed >= request.RolloutTimeout)
            {
                return new DeployResult(false, $"rollout timed out waiting for deployment '{request.Deployment}' in namespace '{request.Namespace}'");
            }

            var remaining = request.RolloutTimeout - elapsed;
            var delay = _pollInterval < remaining ? _pollInterval : remaining;
            await Task.Delay(delay, _timeProvider, cancellationToken);
        }
    }

    private static string? ValidateRequest(DeployRequest r)
    {
        if (string.IsNullOrEmpty(r.Namespace) || r.Namespace.Length > 63 || !Dns1123Regex.IsMatch(r.Namespace))
        {
            return $"Invalid namespace '{r.Namespace}'. Must match DNS-1123 regex '^[a-z0-9]([-a-z0-9]*[a-z0-9])?$' and be 1-63 characters.";
        }

        if (string.IsNullOrEmpty(r.Deployment) || r.Deployment.Length > 253 || !Dns1123Regex.IsMatch(r.Deployment))
        {
            return $"Invalid deployment '{r.Deployment}'. Must match DNS-1123 regex '^[a-z0-9]([-a-z0-9]*[a-z0-9])?$' and be 1-253 characters.";
        }

        if (string.IsNullOrEmpty(r.Container) || r.Container.Length > 63 || !Dns1123Regex.IsMatch(r.Container))
        {
            return $"Invalid container '{r.Container}'. Must match DNS-1123 regex '^[a-z0-9]([-a-z0-9]*[a-z0-9])?$' and be 1-63 characters.";
        }

        if (string.IsNullOrEmpty(r.Image) || r.Image.Length > 512 || r.Image.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return $"Invalid image '{r.Image}'. Must be 1-512 characters and contain no whitespace or control characters.";
        }

        return null;
    }

    private static DeployResult FormatHttpError(HttpOperationException ex)
    {
        var statusCode = (int)ex.Response.StatusCode;
        var phrase = string.IsNullOrWhiteSpace(ex.Response.ReasonPhrase) ? null : ex.Response.ReasonPhrase.Trim();
        var message = phrase != null ? $"Kubernetes API error: HTTP {statusCode} ({phrase})" : $"Kubernetes API error: HTTP {statusCode}";
        return new DeployResult(false, message);
    }

    [GeneratedRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$", RegexOptions.Compiled)]
    private static partial Regex CreateDns1123Regex();
}
