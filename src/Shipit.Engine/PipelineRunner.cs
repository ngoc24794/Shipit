using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;
using Shipit.Security;
using Shipit.Ssh;

namespace Shipit.Engine;

/// <summary>
/// Executes one run: checkout, build, push, deploy (for pipelines) or checkout, build, push, run-job (for jobs).
/// Stops at the first failing step, always cleans up registry login and the clone on the slave, masks secrets
/// in every log line, and persists state as it goes.
/// </summary>
public sealed partial class PipelineRunner(
    IDbContextFactory<ShipitDbContext> dbFactory,
    ISshExecutor ssh,
    IK8sDeployer deployer,
    ISecretProtector protector,
    EngineOptions options,
    TimeProvider time,
    ILogger<PipelineRunner> logger,
    IK8sJobRunner? jobRunner = null,
    IJobAllowList? allowList = null)
{
    /// <summary>Reason stored when a run is cut short by a restart or shutdown.</summary>
    public const string InterruptedReason = "interrupted";

    private static readonly StepName[] PipelineSteps = [StepName.Checkout, StepName.Build, StepName.Push, StepName.Deploy];
    private static readonly StepName[] JobSteps = [StepName.Checkout, StepName.Build, StepName.Push, StepName.RunJob];

    [GeneratedRegex("^[0-9a-f]{7,40}$")]
    private static partial Regex ShaRegex();

    internal static string BuildK8sJobName(string jobName, long runId, int position)
    {
        var suffix = $"-{runId}-{position}";
        var maxPrefixLen = Math.Max(1, 63 - suffix.Length);

        var raw = (jobName ?? string.Empty).ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var ch in raw)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('-');
            }
        }
        var cleaned = sb.ToString();
        while (cleaned.Contains("--"))
        {
            cleaned = cleaned.Replace("--", "-");
        }
        cleaned = cleaned.Trim('-');
        if (string.IsNullOrEmpty(cleaned))
        {
            cleaned = "job";
        }

        if (cleaned.Length > maxPrefixLen)
        {
            cleaned = cleaned[..maxPrefixLen].TrimEnd('-');
            if (string.IsNullOrEmpty(cleaned))
            {
                cleaned = "j";
            }
        }

        var result = $"{cleaned}{suffix}".Trim('-');
        if (result.Length > 63)
        {
            result = result[..63].TrimEnd('-');
        }
        return result;
    }

    /// <summary>Runs a previously queued run to completion. Never throws for run failures; they are recorded.</summary>
    public async Task ExecuteAsync(long runId, CancellationToken cancellationToken, CancellationToken userCancellationToken = default)
    {
        var plan = await LoadPlanAsync(runId).ConfigureAwait(false);
        if (plan is null)
        {
            logger.LogWarning("Run {RunId} was not found or is not queued; skipping.", runId);
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, userCancellationToken);
        var combinedToken = linkedCts.Token;

        var masker = new LogMasker();
        foreach (var s in options.AdditionalSecretsToMask)
        {
            masker.Add(s);
        }
        var writer = new RunLogWriter(dbFactory, runId, masker, options, time);
        var ctx = new RunContext(plan, writer, masker);

        try
        {
            await UpdateRunAsync(runId, r =>
            {
                r.Status = RunStatus.Running;
                r.StartedAt = Now();
            }).ConfigureAwait(false);

            combinedToken.ThrowIfCancellationRequested();

            if (!TryDecryptSecrets(ctx, out var secretError))
            {
                await FailAsync(ctx, secretError, StepName.Checkout, stepRan: false).ConfigureAwait(false);
                return;
            }

            var steps = plan.IsJob ? JobSteps : PipelineSteps;
            foreach (var step in steps)
            {
                combinedToken.ThrowIfCancellationRequested();
                writer.CurrentStep = step;
                writer.CurrentTaskPosition = null;
                await SetStepAsync(runId, step, StepStatus.Running).ConfigureAwait(false);
                await writer.WriteAsync($"== {step} ==", LogStream.System, CancellationToken.None).ConfigureAwait(false);

                var (ok, failure) = await RunStepSafelyAsync(ctx, step, combinedToken).ConfigureAwait(false);
                if (!ok)
                {
                    await FailAsync(ctx, failure!, step, stepRan: true).ConfigureAwait(false);
                    return;
                }

                await SetStepAsync(runId, step, StepStatus.Success).ConfigureAwait(false);
            }

            await writer.WriteAsync("Run completed successfully.", LogStream.System, CancellationToken.None).ConfigureAwait(false);
            ctx.FinalStatus = RunStatus.Success;
            ctx.FinalReason = null;
        }
        catch (OperationCanceledException) when (userCancellationToken.IsCancellationRequested)
        {
            if (ctx.DeployRolloutStarted)
            {
                await writer.WriteAsync("Deploy was cancelled while the rollout was in progress; the Deployment may still be rolling out the new image.", LogStream.System, CancellationToken.None).ConfigureAwait(false);
            }

            await writer.WriteAsync("Run cancelled by user.", LogStream.System, CancellationToken.None).ConfigureAwait(false);
            await CancelStepsAndTasksAsync(runId).ConfigureAwait(false);
            ctx.FinalStatus = RunStatus.Cancelled;
            ctx.FinalReason = "cancelled by user";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await writer.WriteAsync("Run interrupted (shutdown requested).", LogStream.System, CancellationToken.None).ConfigureAwait(false);
            await InterruptAsync(runId).ConfigureAwait(false);
            ctx.FinalStatus = RunStatus.Failed;
            ctx.FinalReason = InterruptedReason;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} failed unexpectedly.", runId);
            await FailAsync(ctx, "internal error: " + ex.GetType().Name, ctx.CurrentStepOr(StepName.Checkout), stepRan: true)
                .ConfigureAwait(false);
        }
        finally
        {
            await CleanupAsync(ctx).ConfigureAwait(false);
            try
            {
                await writer.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Flushing logs for run {RunId} failed.", runId);
            }

            // The run only becomes final once every log line is stored, so a client that sees the final
            // status can safely stop following the log.
            await UpdateRunAsync(runId, r =>
            {
                r.Status = ctx.FinalStatus;
                r.FailureReason = ctx.FinalReason;
                r.FinishedAt = Now();
            }).ConfigureAwait(false);
        }
    }

    private async Task<(bool Ok, string? Failure)> RunStepSafelyAsync(RunContext c, StepName step, CancellationToken ct)
    {
        c.CurrentStep = step;
        try
        {
            return step switch
            {
                StepName.Checkout => await CheckoutAsync(c, ct).ConfigureAwait(false),
                StepName.Build => await BuildAsync(c, ct).ConfigureAwait(false),
                StepName.Push => await PushAsync(c, ct).ConfigureAwait(false),
                StepName.Deploy => await DeployAsync(c, ct).ConfigureAwait(false),
                StepName.RunJob => await RunJobStepAsync(c, ct).ConfigureAwait(false),
                _ => (false, "unknown step"),
            };
        }
        catch (ArgumentException ex)
        {
            // Raised by RemoteCommandBuilder when a pipeline setting is not safe to put in a command.
            return (false, c.Masker.Mask("invalid pipeline setting: " + ex.Message));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, "operation was cancelled");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Step {Step} of run {RunId} threw.", step, c.Plan.RunId);
            return (false, c.Masker.Mask($"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    // ------------------------------------------------------------------ steps

    private async Task<(bool, string?)> CheckoutAsync(RunContext c, CancellationToken ct)
    {
        var p = c.Plan;
        c.WorkDir = RemoteCommandBuilder.WorkDirFor(p.RunId);
        await c.Writer.WriteAsync($"Cloning {p.GitUrl} (branch {p.GitBranch}) into {c.WorkDir}", LogStream.System, ct).ConfigureAwait(false);

        var clone = RemoteCommandBuilder.Clone(p.GitUrl, p.GitBranch, c.WorkDir, c.GitToken!);
        var code = await RunRemoteAsync(c, clone, options.CloneTimeout, c.Writer, ct).ConfigureAwait(false);
        if (code != 0)
        {
            return (false, ExitFailure("git clone", code));
        }

        var capture = new CapturingSink(c.Writer);
        code = await RunRemoteAsync(c, RemoteCommandBuilder.ShortSha(c.WorkDir), options.CloneTimeout, capture, ct).ConfigureAwait(false);
        if (code != 0)
        {
            return (false, ExitFailure("git rev-parse", code));
        }

        var sha = capture.StdoutLines.LastOrDefault(l => ShaRegex().IsMatch(l.Trim()))?.Trim();
        if (sha is null)
        {
            return (false, "could not determine the commit sha after cloning");
        }

        c.Tag = sha;
        c.Image = RemoteCommandBuilder.BuildImageRef(p.RegistryHost, p.ImageName, sha);
        await UpdateRunAsync(p.RunId, r =>
        {
            r.Tag = sha;
            r.Image = c.Image;
        }).ConfigureAwait(false);
        await c.Writer.WriteAsync($"Commit {sha}; image will be {c.Image}", LogStream.System, ct).ConfigureAwait(false);
        return (true, null);
    }

    private async Task<(bool, string?)> BuildAsync(RunContext c, CancellationToken ct)
    {
        var p = c.Plan;
        if (p.RegistryPasswordProtected is null)
        {
            await c.Writer.WriteAsync("Registry login skipped (no credential)", LogStream.System, ct).ConfigureAwait(false);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(p.RegistryUsername))
            {
                return (false, "the registry credential has no username");
            }

            c.LoginAttempted = true;
            var login = RemoteCommandBuilder.DockerLogin(p.RegistryHost, p.RegistryUsername, c.RegistryPassword!);
            var loginCode = await RunRemoteAsync(c, login, options.CloneTimeout, c.Writer, ct).ConfigureAwait(false);
            if (loginCode != 0)
            {
                return (false, ExitFailure("docker login", loginCode));
            }
        }

        var build = RemoteCommandBuilder.DockerBuild(c.WorkDir!, p.DockerfilePath, c.Image!, p.BuildContext);
        var code = await RunRemoteAsync(c, build, options.BuildTimeout, c.Writer, ct).ConfigureAwait(false);
        return code == 0 ? (true, null) : (false, ExitFailure("docker build", code));
    }

    private async Task<(bool, string?)> PushAsync(RunContext c, CancellationToken ct)
    {
        var code = await RunRemoteAsync(c, RemoteCommandBuilder.DockerPush(c.Image!), options.PushTimeout, c.Writer, ct).ConfigureAwait(false);
        return code == 0 ? (true, null) : (false, ExitFailure("docker push", code));
    }

    private async Task<(bool, string?)> DeployAsync(RunContext c, CancellationToken ct)
    {
        var p = c.Plan;
        if (p.DeployNamespace is null || p.DeployDeployment is null || p.DeployContainer is null)
        {
            return (false, "pipeline deploy configuration is incomplete");
        }

        var deployHost = string.IsNullOrWhiteSpace(p.DeployRegistryHost) ? p.RegistryHost : p.DeployRegistryHost;
        var deployImage = RemoteCommandBuilder.BuildImageRef(deployHost, p.ImageName, c.Tag!);
        await c.Writer.WriteAsync($"Deploying image {deployImage} to {p.DeployNamespace}/{p.DeployDeployment}", LogStream.System, ct).ConfigureAwait(false);
        var request = new DeployRequest(p.DeployNamespace, p.DeployDeployment, p.DeployContainer, deployImage, options.RolloutTimeout, options.RollbackTimeout);
        var trackingSink = new DeployTrackingSink(c.Writer, () => c.DeployRolloutStarted = true);
        var result = await deployer.SetImageAsync(request, trackingSink, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return (false, c.Masker.Mask(result.Message));
        }

        await c.Writer.WriteAsync(result.Message, LogStream.System, ct).ConfigureAwait(false);
        return (true, null);
    }

    private async Task<(bool, string?)> RunJobStepAsync(RunContext c, CancellationToken ct)
    {
        if (jobRunner is null)
        {
            return (false, "no Kubernetes job runner configured");
        }

        var p = c.Plan;
        var deployHost = string.IsNullOrWhiteSpace(p.DeployRegistryHost) ? p.RegistryHost : p.DeployRegistryHost;
        var clusterImage = RemoteCommandBuilder.BuildImageRef(deployHost, p.ImageName, c.Tag!);

        IReadOnlyList<string> imagePullSecrets;
        try
        {
            imagePullSecrets = string.IsNullOrWhiteSpace(p.JobImagePullSecretsJson)
                ? []
                : JsonSerializer.Deserialize<List<string>>(p.JobImagePullSecretsJson) ?? [];
        }
        catch (Exception ex)
        {
            return (false, $"invalid imagePullSecrets JSON: {ex.Message}");
        }

        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var jobTasks = await db.JobTasks
            .Where(t => t.JobId == p.JobId)
            .OrderBy(t => t.Position)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var task in jobTasks)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<string> args;
            IReadOnlyList<string> envFromConfigMaps;
            IReadOnlyList<string> envFromSecrets;
            IReadOnlyList<JobEnvVar> env;

            try
            {
                args = string.IsNullOrWhiteSpace(task.ArgsJson)
                    ? []
                    : JsonSerializer.Deserialize<List<string>>(task.ArgsJson, jsonOptions) ?? [];
                envFromConfigMaps = string.IsNullOrWhiteSpace(task.EnvFromConfigMapsJson)
                    ? []
                    : JsonSerializer.Deserialize<List<string>>(task.EnvFromConfigMapsJson, jsonOptions) ?? [];
                envFromSecrets = string.IsNullOrWhiteSpace(task.EnvFromSecretsJson)
                    ? []
                    : JsonSerializer.Deserialize<List<string>>(task.EnvFromSecretsJson, jsonOptions) ?? [];
                env = string.IsNullOrWhiteSpace(task.EnvJson)
                    ? []
                    : JsonSerializer.Deserialize<List<JobEnvVar>>(task.EnvJson, jsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                var parseError = $"invalid JSON: {ex.Message}";
                await UpdateRunTaskAsync(p.RunId, task.Position, t =>
                {
                    t.Status = StepStatus.Failed;
                    t.FinishedAt = Now();
                }).ConfigureAwait(false);
                await SkipRemainingRunTasksAsync(p.RunId, task.Position).ConfigureAwait(false);
                await c.Writer.WriteAsync($"Task {task.Position} ({task.Label}) failed: {parseError}", LogStream.System, CancellationToken.None).ConfigureAwait(false);
                return (false, $"task {task.Position} ({task.Label}) failed: {parseError}");
            }

            var k8sJobName = BuildK8sJobName(p.JobName!, p.RunId, task.Position);

            var request = new JobTaskRequest(
                Namespace: p.JobNamespace!,
                JobName: k8sJobName,
                ContainerName: string.IsNullOrWhiteSpace(p.JobContainerName) ? "app" : p.JobContainerName,
                Image: clusterImage,
                Args: args,
                EnvFromConfigMaps: envFromConfigMaps,
                EnvFromSecrets: envFromSecrets,
                Env: env,
                ImagePullSecrets: imagePullSecrets,
                ServiceAccount: p.JobServiceAccount,
                Labels: new Dictionary<string, string>
                {
                    ["shipit/run-id"] = p.RunId.ToString(),
                    ["shipit/job-id"] = p.JobId!.Value.ToString(),
                    ["shipit/task"] = task.Position.ToString(),
                    ["app.kubernetes.io/managed-by"] = "shipit",
                },
                Timeout: task.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(task.TimeoutSeconds) : TimeSpan.FromSeconds(1800),
                BackoffLimit: task.BackoffLimit,
                Ttl: task.TtlSeconds > 0 ? TimeSpan.FromSeconds(task.TtlSeconds) : TimeSpan.FromSeconds(3600));

            if (allowList != null)
            {
                var (allowed, reason) = await CheckAllowListAsync(allowList, request, ct).ConfigureAwait(false);
                if (!allowed)
                {
                    var allowReason = $"not allowed: {reason}";
                    await UpdateRunTaskAsync(p.RunId, task.Position, t =>
                    {
                        t.Status = StepStatus.Failed;
                        t.K8sJobName = k8sJobName;
                        t.FinishedAt = Now();
                    }).ConfigureAwait(false);
                    await SkipRemainingRunTasksAsync(p.RunId, task.Position).ConfigureAwait(false);
                    await c.Writer.WriteAsync(allowReason, LogStream.System, CancellationToken.None).ConfigureAwait(false);
                    return (false, $"task {task.Position} ({task.Label}) failed: {allowReason}");
                }
            }

            await UpdateRunTaskAsync(p.RunId, task.Position, t =>
            {
                t.Status = StepStatus.Running;
                t.K8sJobName = k8sJobName;
                t.StartedAt = Now();
            }).ConfigureAwait(false);

            c.Writer.CurrentStep = StepName.RunJob;
            c.Writer.CurrentTaskPosition = task.Position;
            await c.Writer.WriteAsync($"== Task {task.Position}: {task.Label} ({k8sJobName}) ==", LogStream.System, ct).ConfigureAwait(false);

            var result = await jobRunner.RunAsync(request, c.Writer, ct).ConfigureAwait(false);

            await UpdateRunTaskAsync(p.RunId, task.Position, t =>
            {
                t.Status = result.Success ? StepStatus.Success : StepStatus.Failed;
                t.ExitCode = result.ExitCode;
                t.FinishedAt = Now();
            }).ConfigureAwait(false);

            if (!result.Success)
            {
                string taskFailure;
                if (result.TimedOut)
                {
                    taskFailure = "job timed out";
                }
                else if (result.ExitCode != null)
                {
                    taskFailure = $"job exited with code {result.ExitCode}";
                }
                else
                {
                    taskFailure = result.Message;
                }

                await SkipRemainingRunTasksAsync(p.RunId, task.Position).ConfigureAwait(false);
                return (false, $"task {task.Position} ({task.Label}) failed: {taskFailure}");
            }
        }

        c.Writer.CurrentTaskPosition = null;
        return (true, null);
    }

    private static async Task<(bool Allowed, string Reason)> CheckAllowListAsync(
        IJobAllowList allow,
        JobTaskRequest request,
        CancellationToken ct)
    {
        if (!await allow.IsNamespaceAllowedAsync(request.Namespace, ct).ConfigureAwait(false))
        {
            return (false, $"namespace '{request.Namespace}' is not allowed");
        }

        foreach (var cm in request.EnvFromConfigMaps)
        {
            if (!string.IsNullOrEmpty(cm) && !await allow.IsConfigMapAllowedAsync(request.Namespace, cm, ct).ConfigureAwait(false))
            {
                return (false, $"ConfigMap '{cm}' in namespace '{request.Namespace}' is not allowed");
            }
        }

        foreach (var sec in request.EnvFromSecrets)
        {
            if (!string.IsNullOrEmpty(sec) && !await allow.IsSecretAllowedAsync(request.Namespace, sec, ct).ConfigureAwait(false))
            {
                return (false, $"Secret '{sec}' in namespace '{request.Namespace}' is not allowed");
            }
        }

        foreach (var ips in request.ImagePullSecrets)
        {
            if (!string.IsNullOrEmpty(ips) && !await allow.IsPullSecretAllowedAsync(request.Namespace, ips, ct).ConfigureAwait(false))
            {
                return (false, $"image pull secret '{ips}' in namespace '{request.Namespace}' is not allowed");
            }
        }

        foreach (var ev in request.Env)
        {
            if (!string.IsNullOrEmpty(ev.SecretName) && !await allow.IsSecretAllowedAsync(request.Namespace, ev.SecretName, ct).ConfigureAwait(false))
            {
                return (false, $"Secret '{ev.SecretName}' referenced by env var '{ev.Name}' in namespace '{request.Namespace}' is not allowed");
            }
        }

        return (true, string.Empty);
    }

    private static string ExitFailure(string what, int code) =>
        code == 124 ? $"{what} timed out" : $"{what} exited with code {code}";

    private Task<int> RunRemoteAsync(RunContext c, RemoteCommand command, TimeSpan timeout, ILogSink sink, CancellationToken ct) =>
        ssh.ExecuteAsync(c.SshTarget!, command with { Timeout = timeout }, sink, ct);

    // ------------------------------------------------------------------ secrets, cleanup

    private bool TryDecryptSecrets(RunContext c, out string error)
    {
        try
        {
            var p = c.Plan;
            c.GitToken = protector.Unprotect(p.GitTokenProtected);
            if (p.RegistryPasswordProtected is not null)
            {
                c.RegistryPassword = protector.Unprotect(p.RegistryPasswordProtected);
                c.Masker.Add(c.RegistryPassword);
            }
            var sshKey = protector.Unprotect(p.SshKeyProtected);
            c.SshTarget = new SshTarget(p.SlaveHost, p.SlavePort, p.SlaveUser, sshKey);
            c.Masker.Add(c.GitToken);
            c.Masker.Add(sshKey);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            error = "credentials could not be decrypted (is the master key correct?)";
            return false;
        }
    }

    private async Task CleanupAsync(RunContext c)
    {
        if (c.SshTarget is null)
        {
            return;
        }

        using var cts = new CancellationTokenSource(options.CleanupTimeout);
        if (c.LoginAttempted)
        {
            await TryCleanupCommandAsync(c, RemoteCommandBuilder.DockerLogout(c.Plan.RegistryHost), "docker logout", cts.Token).ConfigureAwait(false);
        }

        if (c.WorkDir is not null)
        {
            await TryCleanupCommandAsync(c, RemoteCommandBuilder.Cleanup(c.WorkDir), "remove working directory", cts.Token).ConfigureAwait(false);
        }
    }

    private async Task TryCleanupCommandAsync(RunContext c, RemoteCommand command, string what, CancellationToken ct)
    {
        try
        {
            var code = await ssh.ExecuteAsync(c.SshTarget!, command, c.Writer, ct).ConfigureAwait(false);
            if (code != 0)
            {
                await c.Writer.WriteAsync($"Cleanup warning: {what} exited with code {code}", LogStream.System, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cleanup step '{What}' failed for run {RunId}.", what, c.Plan.RunId);
            await c.Writer.WriteAsync($"Cleanup warning: {what} failed ({ex.GetType().Name})", LogStream.System, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ persistence

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private async Task<RunPlan?> LoadPlanAsync(long runId)
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var run = await db.Runs.AsNoTracking()
            .Include(r => r.Pipeline).ThenInclude(p => p!.Slave).ThenInclude(s => s!.SshCredential)
            .Include(r => r.Pipeline).ThenInclude(p => p!.GitCredential)
            .Include(r => r.Pipeline).ThenInclude(p => p!.RegistryCredential)
            .Include(r => r.Job).ThenInclude(j => j!.Slave).ThenInclude(s => s!.SshCredential)
            .Include(r => r.Job).ThenInclude(j => j!.GitCredential)
            .Include(r => r.Job).ThenInclude(j => j!.RegistryCredential)
            .FirstOrDefaultAsync(r => r.Id == runId && r.Status == RunStatus.Queued)
            .ConfigureAwait(false);

        if (run is null)
        {
            return null;
        }

        if (run.PipelineId != null)
        {
            var p = run.Pipeline;
            if (p?.Slave?.SshCredential is null || p.GitCredential is null)
            {
                return null;
            }

            if (p.RegistryCredentialId is not null && p.RegistryCredential is null)
            {
                return null;
            }

            return new RunPlan(
                RunId: runId,
                IsJob: false,
                GitUrl: p.GitUrl,
                GitBranch: p.GitBranch,
                DockerfilePath: p.DockerfilePath,
                BuildContext: p.BuildContext,
                ImageName: p.ImageName,
                RegistryHost: p.RegistryHost,
                DeployRegistryHost: p.DeployRegistryHost,
                SlaveHost: p.Slave.Host,
                SlavePort: p.Slave.Port,
                SlaveUser: p.Slave.User,
                SshKeyProtected: p.Slave.SshCredential.ProtectedValue,
                GitTokenProtected: p.GitCredential.ProtectedValue,
                RegistryPasswordProtected: p.RegistryCredential?.ProtectedValue,
                RegistryUsername: p.RegistryCredential?.Username,
                DeployNamespace: p.DeployNamespace,
                DeployDeployment: p.DeployDeployment,
                DeployContainer: p.DeployContainer);
        }

        if (run.JobId != null)
        {
            var j = run.Job;
            if (j?.Slave?.SshCredential is null || j.GitCredential is null)
            {
                return null;
            }

            if (j.RegistryCredentialId is not null && j.RegistryCredential is null)
            {
                return null;
            }

            return new RunPlan(
                RunId: runId,
                IsJob: true,
                GitUrl: j.GitUrl,
                GitBranch: j.GitBranch,
                DockerfilePath: j.DockerfilePath,
                BuildContext: j.BuildContext,
                ImageName: j.ImageName,
                RegistryHost: j.RegistryHost,
                DeployRegistryHost: j.DeployRegistryHost,
                SlaveHost: j.Slave.Host,
                SlavePort: j.Slave.Port,
                SlaveUser: j.Slave.User,
                SshKeyProtected: j.Slave.SshCredential.ProtectedValue,
                GitTokenProtected: j.GitCredential.ProtectedValue,
                RegistryPasswordProtected: j.RegistryCredential?.ProtectedValue,
                RegistryUsername: j.RegistryCredential?.Username,
                JobId: j.Id,
                JobName: j.Name,
                JobNamespace: j.Namespace,
                JobContainerName: j.ContainerName,
                JobServiceAccount: j.ServiceAccount,
                JobImagePullSecretsJson: j.ImagePullSecretsJson);
        }

        return null;
    }

    private async Task UpdateRunAsync(long runId, Action<Run> change)
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var run = await db.Runs.FirstAsync(r => r.Id == runId).ConfigureAwait(false);
        change(run);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task UpdateRunTaskAsync(long runId, int position, Action<RunTask> change)
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var task = await db.RunTasks.FirstOrDefaultAsync(t => t.RunId == runId && t.Position == position).ConfigureAwait(false);
        if (task != null)
        {
            change(task);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    private async Task SkipRemainingRunTasksAsync(long runId, int currentPosition)
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var remaining = await db.RunTasks
            .Where(t => t.RunId == runId && t.Position > currentPosition && t.Status == StepStatus.Pending)
            .ToListAsync().ConfigureAwait(false);
        foreach (var t in remaining)
        {
            t.Status = StepStatus.Skipped;
        }
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task SetStepAsync(long runId, StepName step, StepStatus status)
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var row = await db.RunSteps.FirstAsync(s => s.RunId == runId && s.Name == step).ConfigureAwait(false);
        row.Status = status;
        if (status == StepStatus.Running)
        {
            row.StartedAt = Now();
        }
        else if (status is StepStatus.Success or StepStatus.Failed)
        {
            row.FinishedAt = Now();
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>Marks <paramref name="failedStep"/> failed, later steps skipped, and the run failed.</summary>
    private async Task FailAsync(RunContext c, string detail, StepName failedStep, bool stepRan)
    {
        var masked = c.Masker.Mask(detail);
        var runId = c.Plan.RunId;
        await c.Writer.WriteAsync($"FAILED: {masked}", LogStream.System, CancellationToken.None).ConfigureAwait(false);

        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var run = await db.Runs.Include(r => r.Steps).Include(r => r.Tasks).FirstAsync(r => r.Id == runId).ConfigureAwait(false);
        var now = Now();
        foreach (var s in run.Steps)
        {
            if (s.Name == failedStep && stepRan)
            {
                s.Status = StepStatus.Failed;
                s.StartedAt ??= now;
                s.FinishedAt = now;
            }
            else if (s.Status == StepStatus.Pending || (!stepRan && s.Status == StepStatus.Running))
            {
                s.Status = StepStatus.Skipped;
            }
        }

        foreach (var t in run.Tasks)
        {
            if (t.Status == StepStatus.Pending)
            {
                t.Status = StepStatus.Skipped;
            }
        }

        // Steps are visible immediately; the run itself turns final last (see ExecuteAsync).
        c.FinalStatus = RunStatus.Failed;
        c.FinalReason = failedStep == StepName.RunJob ? masked : $"{failedStep.ToString().ToLowerInvariant()} failed: {masked}";
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task CancelStepsAndTasksAsync(long runId)
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var run = await db.Runs.Include(r => r.Steps).Include(r => r.Tasks).FirstAsync(r => r.Id == runId).ConfigureAwait(false);
        var now = Now();
        MarkStepsCancelled(run.Steps, now);
        MarkTasksCancelled(run.Tasks, now);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static void MarkStepsCancelled(IEnumerable<RunStep> steps, DateTime now)
    {
        foreach (var s in steps)
        {
            if (s.Status == StepStatus.Running)
            {
                s.Status = StepStatus.Cancelled;
                s.FinishedAt = now;
            }
            else if (s.Status == StepStatus.Pending)
            {
                s.Status = StepStatus.Skipped;
            }
        }
    }

    private static void MarkTasksCancelled(IEnumerable<RunTask> tasks, DateTime now)
    {
        foreach (var t in tasks)
        {
            if (t.Status == StepStatus.Running)
            {
                t.Status = StepStatus.Cancelled;
                t.FinishedAt = now;
            }
            else if (t.Status == StepStatus.Pending)
            {
                t.Status = StepStatus.Cancelled;
            }
        }
    }

    private async Task InterruptAsync(long runId)
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var run = await db.Runs.Include(r => r.Steps).Include(r => r.Tasks).FirstAsync(r => r.Id == runId).ConfigureAwait(false);
        var now = Now();
        MarkStepsInterrupted(run.Steps, now);
        MarkTasksInterrupted(run.Tasks, now);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static void MarkStepsInterrupted(IEnumerable<RunStep> steps, DateTime now)
    {
        foreach (var s in steps)
        {
            if (s.Status == StepStatus.Running)
            {
                s.Status = StepStatus.Failed;
                s.FinishedAt = now;
            }
            else if (s.Status == StepStatus.Pending)
            {
                s.Status = StepStatus.Skipped;
            }
        }
    }

    private static void MarkTasksInterrupted(IEnumerable<RunTask> tasks, DateTime now)
    {
        foreach (var t in tasks)
        {
            if (t.Status == StepStatus.Running)
            {
                t.Status = StepStatus.Failed;
                t.FinishedAt = now;
            }
            else if (t.Status == StepStatus.Pending)
            {
                t.Status = StepStatus.Skipped;
            }
        }
    }

    /// <summary>Marks a leftover run (and its steps/tasks) as interrupted. Used when recovering after a restart.</summary>
    internal static void MarkInterrupted(Run run, DateTime now)
    {
        MarkStepsInterrupted(run.Steps, now);
        if (run.Tasks != null)
        {
            MarkTasksInterrupted(run.Tasks, now);
        }
        run.Status = RunStatus.Failed;
        run.FailureReason = InterruptedReason;
        run.FinishedAt = now;
    }

    // ------------------------------------------------------------------ helpers

    private sealed record RunPlan(
        long RunId,
        bool IsJob,
        string GitUrl,
        string GitBranch,
        string DockerfilePath,
        string? BuildContext,
        string ImageName,
        string RegistryHost,
        string? DeployRegistryHost,
        string SlaveHost,
        int SlavePort,
        string SlaveUser,
        string SshKeyProtected,
        string GitTokenProtected,
        string? RegistryPasswordProtected,
        string? RegistryUsername,
        string? DeployNamespace = null,
        string? DeployDeployment = null,
        string? DeployContainer = null,
        long? JobId = null,
        string? JobName = null,
        string? JobNamespace = null,
        string? JobContainerName = null,
        string? JobServiceAccount = null,
        string? JobImagePullSecretsJson = null);

    private sealed class RunContext(RunPlan plan, RunLogWriter writer, LogMasker masker)
    {
        public RunPlan Plan { get; } = plan;

        public RunLogWriter Writer { get; } = writer;

        public LogMasker Masker { get; } = masker;

        public SshTarget? SshTarget { get; set; }

        public string? GitToken { get; set; }

        public string? RegistryPassword { get; set; }

        public string? WorkDir { get; set; }

        public string? Tag { get; set; }

        public string? Image { get; set; }

        public bool LoginAttempted { get; set; }

        public bool DeployRolloutStarted { get; set; }

        /// <summary>Applied to the run row last, after cleanup and the final log flush. Defaults to a safe failure.</summary>
        public RunStatus FinalStatus { get; set; } = RunStatus.Failed;

        public string? FinalReason { get; set; } = "internal error";

        public StepName? CurrentStep { get; set; }

        public StepName CurrentStepOr(StepName fallback) => CurrentStep ?? fallback;
    }

    /// <summary>Forwards everything to the real sink and remembers stdout lines (used to read the commit sha).</summary>
    private sealed class CapturingSink(ILogSink inner) : ILogSink
    {
        private readonly List<string> _stdout = [];

        public IReadOnlyList<string> StdoutLines
        {
            get
            {
                lock (_stdout)
                {
                    return _stdout.ToList();
                }
            }
        }

        public ValueTask WriteAsync(string line, LogStream stream, CancellationToken cancellationToken = default)
        {
            if (stream == LogStream.Stdout)
            {
                lock (_stdout)
                {
                    _stdout.Add(line);
                }
            }

            return inner.WriteAsync(line, stream, cancellationToken);
        }
    }

    private sealed class DeployTrackingSink(ILogSink inner, Action onLogWritten) : ILogSink
    {
        public ValueTask WriteAsync(string line, LogStream stream, CancellationToken cancellationToken = default)
        {
            onLogWritten();
            return inner.WriteAsync(line, stream, cancellationToken);
        }
    }
}
