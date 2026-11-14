using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// Executes Kubernetes batch/v1 Jobs, streams pod logs, and reports execution results.
/// </summary>
public sealed partial class KubernetesJobRunner : IK8sJobRunner
{
    private static readonly Regex Dns1123Regex = CreateDns1123Regex();

    private readonly IKubernetes _client;
    private readonly IJobAllowList _allowList;
    private readonly TimeSpan _pollInterval;
    private readonly TimeProvider _timeProvider;

    public KubernetesJobRunner(
        IKubernetes client,
        IJobAllowList allowList,
        TimeSpan? pollInterval = null,
        TimeProvider? timeProvider = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _allowList = allowList ?? throw new ArgumentNullException(nameof(allowList));
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Creates a job runner configured for running inside a Kubernetes cluster.
    /// Only call when running in-cluster; do not call in offline unit tests.
    /// </summary>
    public static KubernetesJobRunner CreateInCluster(IJobAllowList allowList)
    {
        var config = KubernetesClientConfiguration.InClusterConfig();
        var client = new Kubernetes(config);
        return new KubernetesJobRunner(client, allowList);
    }

    public async Task<JobTaskResult> RunAsync(JobTaskRequest request, ILogSink log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(log);

        // 1. Validation
        var validationError = ValidateRequest(request);
        if (validationError != null)
        {
            return new JobTaskResult(false, null, validationError);
        }

        // 2. Allowlist checks (reject immediately without calling cluster)
        if (!await _allowList.IsNamespaceAllowedAsync(request.Namespace, cancellationToken))
        {
            return new JobTaskResult(false, null, $"Namespace '{request.Namespace}' is not allowed.");
        }

        if (request.EnvFromConfigMaps != null)
        {
            foreach (var cm in request.EnvFromConfigMaps)
            {
                if (!string.IsNullOrEmpty(cm) && !await _allowList.IsConfigMapAllowedAsync(request.Namespace, cm, cancellationToken))
                {
                    return new JobTaskResult(false, null, $"ConfigMap '{cm}' in namespace '{request.Namespace}' is not allowed.");
                }
            }
        }

        if (request.EnvFromSecrets != null)
        {
            foreach (var sec in request.EnvFromSecrets)
            {
                if (!string.IsNullOrEmpty(sec) && !await _allowList.IsSecretAllowedAsync(request.Namespace, sec, cancellationToken))
                {
                    return new JobTaskResult(false, null, $"Secret '{sec}' in namespace '{request.Namespace}' is not allowed.");
                }
            }
        }

        if (request.ImagePullSecrets != null)
        {
            foreach (var ips in request.ImagePullSecrets)
            {
                if (!string.IsNullOrEmpty(ips) && !await _allowList.IsPullSecretAllowedAsync(request.Namespace, ips, cancellationToken))
                {
                    return new JobTaskResult(false, null, $"Image pull secret '{ips}' in namespace '{request.Namespace}' is not allowed.");
                }
            }
        }

        if (request.Env != null)
        {
            foreach (var ev in request.Env)
            {
                if (!string.IsNullOrEmpty(ev.SecretName) && !await _allowList.IsSecretAllowedAsync(request.Namespace, ev.SecretName, cancellationToken))
                {
                    return new JobTaskResult(false, null, $"Secret '{ev.SecretName}' referenced by env var '{ev.Name}' in namespace '{request.Namespace}' is not allowed.");
                }
            }
        }

        // 3. Create Job
        var job = JobSpecBuilder.Build(request);
        try
        {
            await _client.BatchV1.CreateNamespacedJobAsync(job, request.Namespace, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await DeleteJobQuietlyAsync(request.Namespace, request.JobName);
            throw;
        }
        catch (HttpOperationException ex)
        {
            return FormatHttpError(ex);
        }
        catch (HttpRequestException ex)
        {
            return new JobTaskResult(false, null, $"Kubernetes request failed: {ex.Message}");
        }

        // 4. Follow execution, stream logs, and await completion
        try
        {
            return await WatchAndStreamJobAsync(request, log, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await DeleteJobQuietlyAsync(request.Namespace, request.JobName);
            throw;
        }
    }

    public async Task DeleteByRunAsync(string ns, long runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ns);

        var labelSelector = $"shipit/run-id={runId}";
        V1JobList jobList;
        try
        {
            jobList = await _client.BatchV1.ListNamespacedJobAsync(
                ns,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return;
        }

        if (jobList?.Items != null)
        {
            foreach (var job in jobList.Items)
            {
                var jobName = job.Metadata?.Name;
                if (!string.IsNullOrEmpty(jobName))
                {
                    try
                    {
                        await _client.BatchV1.DeleteNamespacedJobAsync(
                            jobName,
                            ns,
                            propagationPolicy: "Foreground",
                            cancellationToken: cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Best-effort deletion
                    }
                }
            }
        }
    }

    private async Task<JobTaskResult> WatchAndStreamJobAsync(JobTaskRequest request, ILogSink log, CancellationToken cancellationToken)
    {
        var startTime = _timeProvider.GetTimestamp();
        string? podName = null;
        var logStreamAttempted = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elapsed = _timeProvider.GetElapsedTime(startTime);
            if (elapsed >= request.Timeout)
            {
                return new JobTaskResult(false, 124, "job timed out (DeadlineExceeded)", TimedOut: true);
            }

            // 1. Locate Pod if not yet found
            if (podName == null)
            {
                V1PodList? podList;
                try
                {
                    podList = await _client.CoreV1.ListNamespacedPodAsync(
                        request.Namespace,
                        labelSelector: $"job-name={request.JobName}",
                        cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpOperationException hex)
                {
                    return FormatHttpError(hex);
                }
                catch (HttpRequestException ex)
                {
                    return new JobTaskResult(false, null, $"Kubernetes request failed: {ex.Message}");
                }

                var firstPod = podList?.Items?.FirstOrDefault();
                if (firstPod != null)
                {
                    podName = firstPod.Metadata?.Name;
                }
            }

            // If pod is not found yet, check if the Job itself failed (e.g. DeadlineExceeded)
            if (podName == null)
            {
                var jobCheck = await CheckJobStatusAsync(request.Namespace, request.JobName, cancellationToken);
                if (jobCheck != null)
                {
                    return jobCheck;
                }

                var remaining = request.Timeout - _timeProvider.GetElapsedTime(startTime);
                var delay = _pollInterval < remaining ? _pollInterval : remaining;
                await Task.Delay(delay, _timeProvider, cancellationToken);
                continue;
            }

            // 2. Fetch current Pod status
            V1Pod currentPod;
            try
            {
                currentPod = await _client.CoreV1.ReadNamespacedPodAsync(
                    podName,
                    request.Namespace,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpOperationException hex) when (hex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                // Pod might have been deleted or not ready, check Job
                var jobCheck = await CheckJobStatusAsync(request.Namespace, request.JobName, cancellationToken);
                if (jobCheck != null)
                {
                    return jobCheck;
                }

                podName = null; // Re-discover pod
                continue;
            }
            catch (HttpOperationException hex)
            {
                return FormatHttpError(hex);
            }
            catch (HttpRequestException ex)
            {
                return new JobTaskResult(false, null, $"Kubernetes request failed: {ex.Message}");
            }

            var containerStatus = currentPod.Status?.ContainerStatuses?.FirstOrDefault(c => string.Equals(c.Name, request.ContainerName, StringComparison.Ordinal))
                                  ?? currentPod.Status?.ContainerStatuses?.FirstOrDefault();

            // 3. Check unschedulable condition
            var scheduledCond = currentPod.Status?.Conditions?.FirstOrDefault(c => string.Equals(c.Type, "PodScheduled", StringComparison.OrdinalIgnoreCase));
            if (scheduledCond != null &&
                string.Equals(scheduledCond.Status, "False", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(scheduledCond.Reason, "Unschedulable", StringComparison.OrdinalIgnoreCase))
            {
                var reason = scheduledCond.Message ?? scheduledCond.Reason;
                return new JobTaskResult(false, null, $"pod scheduling failed: {reason}");
            }

            // 4. Check image pull failure / CrashLoopBackOff in waiting state
            var waiting = containerStatus?.State?.Waiting;
            if (waiting != null &&
                (string.Equals(waiting.Reason, "ImagePullBackOff", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(waiting.Reason, "ErrImagePull", StringComparison.OrdinalIgnoreCase)))
            {
                var shortReason = !string.IsNullOrEmpty(waiting.Message)
                    ? $"{waiting.Reason}: {waiting.Message}"
                    : waiting.Reason;
                return new JobTaskResult(false, null, shortReason);
            }

            // 5. Stream logs if container has started (running, terminated, or pod is running/succeeded/failed)
            var hasStarted = containerStatus?.State?.Running != null ||
                             containerStatus?.State?.Terminated != null ||
                             currentPod.Status?.Phase is "Running" or "Succeeded" or "Failed";

            if (hasStarted && !logStreamAttempted)
            {
                logStreamAttempted = true;
                await StreamPodLogsAsync(podName, request, log, cancellationToken);
            }

            // 6. Check termination and exit code
            if (containerStatus?.State?.Terminated != null)
            {
                var exitCode = containerStatus.State.Terminated.ExitCode;
                if (exitCode == 0)
                {
                    return new JobTaskResult(true, 0, "Job completed successfully.");
                }

                return new JobTaskResult(false, exitCode, $"job exited with code {exitCode}");
            }

            // Check Pod Phase if container terminated was not reported
            if (string.Equals(currentPod.Status?.Phase, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return new JobTaskResult(true, 0, "Job completed successfully.");
            }

            if (string.Equals(currentPod.Status?.Phase, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                var exitCode = containerStatus?.State?.Terminated?.ExitCode;
                if (exitCode != null)
                {
                    return new JobTaskResult(false, exitCode, $"job exited with code {exitCode}");
                }

                var msg = currentPod.Status?.Message ?? "pod failed";
                return new JobTaskResult(false, null, msg);
            }

            // 7. Check Job for DeadlineExceeded
            var jobStatus = await CheckJobStatusAsync(request.Namespace, request.JobName, cancellationToken);
            if (jobStatus != null)
            {
                return jobStatus;
            }

            var rem = request.Timeout - _timeProvider.GetElapsedTime(startTime);
            var pollDelay = _pollInterval < rem ? _pollInterval : rem;
            await Task.Delay(pollDelay, _timeProvider, cancellationToken);
        }
    }

    private async Task StreamPodLogsAsync(string podName, JobTaskRequest request, ILogSink log, CancellationToken cancellationToken)
    {
        try
        {
            using var logStream = await _client.CoreV1.ReadNamespacedPodLogAsync(
                podName,
                request.Namespace,
                container: request.ContainerName,
                follow: true,
                cancellationToken: cancellationToken);

            if (logStream != null)
            {
                using var reader = new StreamReader(logStream, Encoding.UTF8);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    await log.WriteAsync(line, LogStream.Stdout, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpOperationException hex) when (hex.Response.StatusCode == HttpStatusCode.BadRequest || hex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            // Logs not ready yet; ignore
        }
        catch (Exception ex)
        {
            await log.WriteAsync($"[job-runner] Log stream ended: {ex.Message}", LogStream.System, cancellationToken);
        }
    }

    private async Task<JobTaskResult?> CheckJobStatusAsync(string ns, string jobName, CancellationToken cancellationToken)
    {
        try
        {
            var job = await _client.BatchV1.ReadNamespacedJobAsync(jobName, ns, cancellationToken: cancellationToken);
            var failedCond = job.Status?.Conditions?.FirstOrDefault(c => string.Equals(c.Type, "Failed", StringComparison.OrdinalIgnoreCase));
            if (failedCond != null && string.Equals(failedCond.Reason, "DeadlineExceeded", StringComparison.OrdinalIgnoreCase))
            {
                return new JobTaskResult(false, 124, "job timed out (DeadlineExceeded)", TimedOut: true);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Ignore temporary errors reading job status
        }

        return null;
    }

    private async Task DeleteJobQuietlyAsync(string ns, string jobName)
    {
        try
        {
            await _client.BatchV1.DeleteNamespacedJobAsync(
                jobName,
                ns,
                propagationPolicy: "Foreground",
                cancellationToken: CancellationToken.None);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private static string? ValidateRequest(JobTaskRequest r)
    {
        if (string.IsNullOrEmpty(r.Namespace) || r.Namespace.Length > 63 || !Dns1123Regex.IsMatch(r.Namespace))
        {
            return $"Invalid namespace '{r.Namespace}'. Must match DNS-1123 regex '^[a-z0-9]([-a-z0-9]*[a-z0-9])?$' and be 1-63 characters.";
        }

        if (string.IsNullOrEmpty(r.JobName) || r.JobName.Length > 63 || !Dns1123Regex.IsMatch(r.JobName))
        {
            return $"Invalid job name '{r.JobName}'. Must match DNS-1123 regex '^[a-z0-9]([-a-z0-9]*[a-z0-9])?$' and be 1-63 characters.";
        }

        if (string.IsNullOrEmpty(r.ContainerName) || r.ContainerName.Length > 63 || !Dns1123Regex.IsMatch(r.ContainerName))
        {
            return $"Invalid container name '{r.ContainerName}'. Must match DNS-1123 regex '^[a-z0-9]([-a-z0-9]*[a-z0-9])?$' and be 1-63 characters.";
        }

        if (string.IsNullOrEmpty(r.Image) || r.Image.Length > 512 || r.Image.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return $"Invalid image '{r.Image}'. Must be 1-512 characters and contain no whitespace or control characters.";
        }

        if (r.Timeout <= TimeSpan.Zero)
        {
            return "Timeout must be greater than zero.";
        }

        return null;
    }

    private static JobTaskResult FormatHttpError(HttpOperationException ex)
    {
        var statusCode = (int)ex.Response.StatusCode;
        var phrase = string.IsNullOrWhiteSpace(ex.Response.ReasonPhrase) ? null : ex.Response.ReasonPhrase.Trim();
        var message = phrase != null ? $"Kubernetes API error: HTTP {statusCode} ({phrase})" : $"Kubernetes API error: HTTP {statusCode}";
        return new JobTaskResult(false, null, message);
    }

    [GeneratedRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$", RegexOptions.Compiled)]
    private static partial Regex CreateDns1123Regex();
}
