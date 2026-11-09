using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// In-memory fake implementation of <see cref="IK8sDeployer"/> for testing and local simulation.
/// </summary>
public sealed class FakeK8sDeployer : IK8sDeployer
{
    private readonly List<DeployRequest> _requests = new();

    public DeployAllowList AllowList { get; set; }

    /// <summary>
    /// Gets all requests received by this fake deployer.
    /// </summary>
    public IReadOnlyList<DeployRequest> Requests => _requests;

    /// <summary>
    /// Fixed result returned on valid deploy requests if <see cref="ResultFactory"/> is null.
    /// Default is success.
    /// </summary>
    public DeployResult? ConfiguredResult { get; set; }

    /// <summary>
    /// Dynamic factory for customizing the returned <see cref="DeployResult"/> per request.
    /// </summary>
    public Func<DeployRequest, DeployResult>? ResultFactory { get; set; }

    /// <summary>
    /// Simulated delay before returning.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Optional <see cref="TimeProvider"/> for delay scheduling in tests.
    /// </summary>
    public TimeProvider? TimeProvider { get; set; }

    /// <summary>
    /// When true, simulates a rollout failure followed by auto-rollback to <see cref="PreviousImage"/>.
    /// </summary>
    public bool SimulateRolloutFailureWithRollback { get; set; }

    /// <summary>
    /// The previous image to simulate rolling back to. Defaults to "app:previous".
    /// </summary>
    public string PreviousImage { get; set; } = "app:previous";

    public FakeK8sDeployer(DeployAllowList allowList)
    {
        AllowList = allowList ?? throw new ArgumentNullException(nameof(allowList));
    }

    public async Task<DeployResult> SetImageAsync(DeployRequest request, ILogSink log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(log);

        _requests.Add(request);

        if (!AllowList.IsAllowed(request.Namespace, request.Deployment))
        {
            return new DeployResult(false, $"Deployment '{request.Deployment}' in namespace '{request.Namespace}' is not allowed.");
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, TimeProvider ?? TimeProvider.System, cancellationToken);
        }

        await log.WriteAsync($"[fake-k8s] Setting image of {request.Namespace}/{request.Deployment}:{request.Container} to {request.Image}", LogStream.System, cancellationToken);
        await log.WriteAsync($"[fake-k8s] Waiting for rollout of {request.Deployment}...", LogStream.System, cancellationToken);

        if (SimulateRolloutFailureWithRollback)
        {
            await log.WriteAsync($"Rollout failed, reverting to {PreviousImage}", LogStream.System, cancellationToken);
            _requests.Add(request with { Image = PreviousImage });
            await log.WriteAsync($"[fake-k8s] Rollout complete for {request.Deployment}", LogStream.System, cancellationToken);
            return new DeployResult(false, $"rollout timed out waiting for deployment '{request.Deployment}' in namespace '{request.Namespace}'; đã hoàn tác về {PreviousImage}", RolledBack: true);
        }

        await log.WriteAsync($"[fake-k8s] Rollout complete for {request.Deployment}", LogStream.System, cancellationToken);

        if (ResultFactory != null)
        {
            return ResultFactory(request);
        }

        return ConfiguredResult ?? new DeployResult(true, $"deployed {request.Image} to {request.Namespace}/{request.Deployment}");
    }
}
