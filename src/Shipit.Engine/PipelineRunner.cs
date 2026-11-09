using System.Security.Cryptography;
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
/// Executes one run: checkout, build, push, deploy. Stops at the first failing step, always cleans up
/// registry login and the clone on the slave, masks secrets in every log line, and persists state as it goes.
/// </summary>
public sealed partial class PipelineRunner(
    IDbContextFactory<ShipitDbContext> dbFactory,
    ISshExecutor ssh,
    IK8sDeployer deployer,
    ISecretProtector protector,
    EngineOptions options,
    TimeProvider time,
    ILogger<PipelineRunner> logger)
{
    /// <summary>Reason stored when a run is cut short by a restart or shutdown.</summary>
    public const string InterruptedReason = "interrupted";

    private static readonly StepName[] Steps = [StepName.Checkout, StepName.Build, StepName.Push, StepName.Deploy];

    [GeneratedRegex("^[0-9a-f]{7,40}$")]
    private static partial Regex ShaRegex();

    /// <summary>Runs a previously queued run to completion. Never throws for run failures; they are recorded.</summary>
    public async Task ExecuteAsync(long runId, CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(runId).ConfigureAwait(false);
        if (plan is null)
        {
            logger.LogWarning("Run {RunId} was not found or is not queued; skipping.", runId);
            return;
        }

        var masker = new LogMasker();
        var writer = new RunLogWriter(dbFactory, runId, masker, options, time);
        var ctx = new RunContext(plan, writer, masker);

        try
        {
            await UpdateRunAsync(runId, r =>
            {
                r.Status = RunStatus.Running;
                r.StartedAt = Now();
            }).ConfigureAwait(false);

            if (!TryDecryptSecrets(ctx, out var secretError))
            {
                await FailAsync(ctx, secretError, StepName.Checkout, stepRan: false).ConfigureAwait(false);
                return;
            }

            foreach (var step in Steps)
            {
                writer.CurrentStep = step;
                await SetStepAsync(runId, step, StepStatus.Running).ConfigureAwait(false);
                await writer.WriteAsync($"== {step} ==", LogStream.System, CancellationToken.None).ConfigureAwait(false);

                var (ok, failure) = await RunStepSafelyAsync(ctx, step, cancellationToken).ConfigureAwait(false);
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
        var deployHost = string.IsNullOrWhiteSpace(p.DeployRegistryHost) ? p.RegistryHost : p.DeployRegistryHost;
        var deployImage = RemoteCommandBuilder.BuildImageRef(deployHost, p.ImageName, c.Tag!);
        await c.Writer.WriteAsync($"Deploying image {deployImage} to {p.DeployNamespace}/{p.DeployDeployment}", LogStream.System, ct).ConfigureAwait(false);
        var request = new DeployRequest(p.DeployNamespace, p.DeployDeployment, p.DeployContainer, deployImage, options.RolloutTimeout, options.RollbackTimeout);
        var result = await deployer.SetImageAsync(request, c.Writer, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return (false, c.Masker.Mask(result.Message));
        }

        await c.Writer.WriteAsync(result.Message, LogStream.System, ct).ConfigureAwait(false);
        return (true, null);
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
            .FirstOrDefaultAsync(r => r.Id == runId && r.Status == RunStatus.Queued)
            .ConfigureAwait(false);
        var p = run?.Pipeline;
        if (run is null || p?.Slave?.SshCredential is null || p.GitCredential is null)
        {
            return null;
        }

        if (p.RegistryCredentialId is not null && p.RegistryCredential is null)
        {
            return null;
        }

        return new RunPlan(
            runId,
            p.GitUrl, p.GitBranch, p.DockerfilePath, p.BuildContext, p.ImageName, p.RegistryHost,
            p.DeployRegistryHost,
            p.DeployNamespace, p.DeployDeployment, p.DeployContainer,
            p.Slave.Host, p.Slave.Port, p.Slave.User,
            p.Slave.SshCredential.ProtectedValue,
            p.GitCredential.ProtectedValue,
            p.RegistryCredential?.ProtectedValue,
            p.RegistryCredential?.Username);
    }

    private async Task UpdateRunAsync(long runId, Action<Run> change)
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var run = await db.Runs.FirstAsync(r => r.Id == runId).ConfigureAwait(false);
        change(run);
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
        var run = await db.Runs.Include(r => r.Steps).FirstAsync(r => r.Id == runId).ConfigureAwait(false);
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

        // Steps are visible immediately; the run itself turns final last (see ExecuteAsync).
        c.FinalStatus = RunStatus.Failed;
        c.FinalReason = $"{failedStep.ToString().ToLowerInvariant()} failed: {masked}";
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task InterruptAsync(long runId)
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var run = await db.Runs.Include(r => r.Steps).FirstAsync(r => r.Id == runId).ConfigureAwait(false);
        MarkStepsInterrupted(run.Steps, Now());
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

    /// <summary>Marks a leftover run (and its steps) as interrupted. Used when recovering after a restart.</summary>
    internal static void MarkInterrupted(Run run, DateTime now)
    {
        MarkStepsInterrupted(run.Steps, now);
        run.Status = RunStatus.Failed;
        run.FailureReason = InterruptedReason;
        run.FinishedAt = now;
    }

    // ------------------------------------------------------------------ helpers

    private sealed record RunPlan(
        long RunId,
        string GitUrl,
        string GitBranch,
        string DockerfilePath,
        string? BuildContext,
        string ImageName,
        string RegistryHost,
        string? DeployRegistryHost,
        string DeployNamespace,
        string DeployDeployment,
        string DeployContainer,
        string SlaveHost,
        int SlavePort,
        string SlaveUser,
        string SshKeyProtected,
        string GitTokenProtected,
        string? RegistryPasswordProtected,
        string? RegistryUsername);

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
}
