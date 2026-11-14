using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// Configurable execution scenario for <see cref="FakeK8sJobRunner"/>.
/// </summary>
public sealed class FakeJobScenario
{
    public int? ExitCode { get; set; } = 0;
    public IReadOnlyList<string>? LogLines { get; set; }
    public bool HangUntilCancelled { get; set; }
    public bool SimulateTimeout { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public string? FailureMessage { get; set; }
}

/// <summary>
/// In-memory fake implementation of <see cref="IK8sJobRunner"/> for testing and local simulation.
/// </summary>
public sealed class FakeK8sJobRunner : IK8sJobRunner
{
    private readonly List<JobTaskRequest> _requests = new();
    private readonly List<(string Namespace, long RunId)> _deletedRuns = new();
    private readonly TimeProvider _timeProvider;

    public IJobAllowList AllowList { get; set; }

    public IReadOnlyList<JobTaskRequest> Requests => _requests;

    public IReadOnlyList<(string Namespace, long RunId)> DeletedRuns => _deletedRuns;

    public FakeJobScenario? DefaultScenario { get; set; }

    public Dictionary<string, FakeJobScenario> ScenariosByJobName { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, FakeJobScenario> ScenariosByPosition { get; } = new(StringComparer.Ordinal);

    public Func<JobTaskRequest, FakeJobScenario?>? ScenarioFactory { get; set; }

    public Func<JobTaskRequest, JobTaskResult>? ResultFactory { get; set; }

    public FakeK8sJobRunner(IJobAllowList allowList, TimeProvider? timeProvider = null)
    {
        AllowList = allowList ?? throw new ArgumentNullException(nameof(allowList));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<JobTaskResult> RunAsync(JobTaskRequest request, ILogSink log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(log);

        _requests.Add(request);

        // 1. Allowlist checks (reject before executing scenario)
        if (!await AllowList.IsNamespaceAllowedAsync(request.Namespace, cancellationToken))
        {
            return new JobTaskResult(false, null, $"Namespace '{request.Namespace}' is not allowed.");
        }

        if (request.EnvFromConfigMaps != null)
        {
            foreach (var cm in request.EnvFromConfigMaps)
            {
                if (!string.IsNullOrEmpty(cm) && !await AllowList.IsConfigMapAllowedAsync(request.Namespace, cm, cancellationToken))
                {
                    return new JobTaskResult(false, null, $"ConfigMap '{cm}' in namespace '{request.Namespace}' is not allowed.");
                }
            }
        }

        if (request.EnvFromSecrets != null)
        {
            foreach (var sec in request.EnvFromSecrets)
            {
                if (!string.IsNullOrEmpty(sec) && !await AllowList.IsSecretAllowedAsync(request.Namespace, sec, cancellationToken))
                {
                    return new JobTaskResult(false, null, $"Secret '{sec}' in namespace '{request.Namespace}' is not allowed.");
                }
            }
        }

        if (request.ImagePullSecrets != null)
        {
            foreach (var ips in request.ImagePullSecrets)
            {
                if (!string.IsNullOrEmpty(ips) && !await AllowList.IsPullSecretAllowedAsync(request.Namespace, ips, cancellationToken))
                {
                    return new JobTaskResult(false, null, $"Image pull secret '{ips}' in namespace '{request.Namespace}' is not allowed.");
                }
            }
        }

        if (request.Env != null)
        {
            foreach (var ev in request.Env)
            {
                if (!string.IsNullOrEmpty(ev.SecretName) && !await AllowList.IsSecretAllowedAsync(request.Namespace, ev.SecretName, cancellationToken))
                {
                    return new JobTaskResult(false, null, $"Secret '{ev.SecretName}' referenced by env var '{ev.Name}' in namespace '{request.Namespace}' is not allowed.");
                }
            }
        }

        if (ResultFactory != null)
        {
            return ResultFactory(request);
        }

        var scenario = ResolveScenario(request);

        if (scenario.Delay > TimeSpan.Zero)
        {
            await Task.Delay(scenario.Delay, _timeProvider, cancellationToken);
        }

        if (scenario.HangUntilCancelled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _timeProvider, cancellationToken);
        }

        if (scenario.SimulateTimeout)
        {
            return new JobTaskResult(false, 124, "job timed out (DeadlineExceeded)", TimedOut: true);
        }

        var logLines = scenario.LogLines ?? new[]
        {
            $"[fake-k8s] Starting job {request.JobName} in {request.Namespace}...",
            $"[fake-k8s] Running container {request.ContainerName} with image {request.Image}...",
            $"[fake-k8s] Job {request.JobName} finished."
        };

        foreach (var line in logLines)
        {
            await log.WriteAsync(line, LogStream.Stdout, cancellationToken);
        }

        var exitCode = scenario.ExitCode ?? 0;
        if (exitCode == 0)
        {
            return new JobTaskResult(true, 0, "Job completed successfully.");
        }

        return new JobTaskResult(false, exitCode, scenario.FailureMessage ?? $"job exited with code {exitCode}");
    }

    public Task DeleteByRunAsync(string ns, long runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ns);
        cancellationToken.ThrowIfCancellationRequested();
        _deletedRuns.Add((ns, runId));
        return Task.CompletedTask;
    }

    private FakeJobScenario ResolveScenario(JobTaskRequest request)
    {
        if (ScenarioFactory != null)
        {
            var created = ScenarioFactory(request);
            if (created != null)
            {
                return created;
            }
        }

        if (ScenariosByJobName.TryGetValue(request.JobName, out var byName))
        {
            return byName;
        }

        if (request.Labels != null)
        {
            foreach (var key in new[] { "shipit/task", "shipit/position", "position" })
            {
                if (request.Labels.TryGetValue(key, out var pos) &&
                    ScenariosByPosition.TryGetValue(pos, out var byPos))
                {
                    return byPos;
                }
            }
        }

        return DefaultScenario ?? new FakeJobScenario();
    }
}
