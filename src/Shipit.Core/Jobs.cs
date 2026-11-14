namespace Shipit.Core;

/// <summary>An env var for a Job container: a plain value, or a reference to a key of a Secret (resolved by Kubernetes, never by Shipit).</summary>
public sealed record JobEnvVar(string Name, string? Value = null, string? SecretName = null, string? SecretKey = null);

/// <summary>
/// One Kubernetes Job to create. Everything here is a name or a plain setting; there is no field for a secret value.
/// The pod spec is built from a fixed template (non-root, /tmp emptyDir), so callers cannot inject arbitrary YAML.
/// </summary>
public sealed record JobTaskRequest(
    string Namespace,
    string JobName,
    string ContainerName,
    string Image,
    IReadOnlyList<string> Args,
    IReadOnlyList<string> EnvFromConfigMaps,
    IReadOnlyList<string> EnvFromSecrets,
    IReadOnlyList<JobEnvVar> Env,
    IReadOnlyList<string> ImagePullSecrets,
    string? ServiceAccount,
    IReadOnlyDictionary<string, string> Labels,
    TimeSpan Timeout,
    int BackoffLimit,
    TimeSpan Ttl);

/// <summary>Outcome of one task. <see cref="ExitCode"/> is null when the pod never produced one (not scheduled, deleted, API error).</summary>
public sealed record JobTaskResult(bool Success, int? ExitCode, string Message, bool TimedOut = false);

/// <summary>Runs a task as a Kubernetes Job, streams the pod log, and waits for completion.</summary>
public interface IK8sJobRunner
{
    /// <summary>
    /// Must reject requests outside the allowlist (<see cref="IJobAllowList"/>) without calling the cluster.
    /// Deletes the Job when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<JobTaskResult> RunAsync(JobTaskRequest request, ILogSink log, CancellationToken cancellationToken = default);

    /// <summary>Deletes Jobs labelled <c>shipit/run-id=&lt;runId&gt;</c> in <paramref name="ns"/>; used to clear orphans after a restart.</summary>
    Task DeleteByRunAsync(string ns, long runId, CancellationToken cancellationToken = default);
}

/// <summary>What a Job may reference. Names only; the lookups are case-sensitive and exact.</summary>
public interface IJobAllowList
{
    bool IsNamespaceAllowed(string ns);

    bool IsConfigMapAllowed(string ns, string name);

    bool IsSecretAllowed(string ns, string name);

    bool IsPullSecretAllowed(string ns, string name);
}
