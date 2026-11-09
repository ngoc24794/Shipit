using Shipit.Core;
using Shipit.Data.Entities;
using Shipit.K8s;
using Shipit.Ssh;

namespace Shipit.Engine.Tests;

public class PipelineRunnerTests
{
    private static string[] Order(FakeSshExecutor ssh) =>
        ssh.ExecutedCommands.Select(c =>
            c.CommandText.Contains("git clone") ? "clone" :
            c.CommandText.Contains("rev-parse") ? "sha" :
            c.CommandText.Contains("docker login") ? "login" :
            c.CommandText.Contains("docker build") ? "build" :
            c.CommandText.Contains("docker push") ? "push" :
            c.CommandText.Contains("docker logout") ? "logout" :
            c.CommandText.Contains("rm -rf") ? "cleanup" : "other").ToArray();

    [Fact]
    public async Task Happy_path_runs_all_four_steps_and_deploys_the_new_image()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal("abc1234", run.Tag);
        Assert.Equal("registry.example.com/shipit-demo:abc1234", run.Image);
        Assert.NotNull(run.StartedAt);
        Assert.NotNull(run.FinishedAt);
        Assert.All(Enum.GetValues<StepName>(), n => Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(run, n)));

        Assert.Equal(["clone", "sha", "login", "build", "push", "logout", "cleanup"], Order(ssh));

        var request = Assert.Single(k8s.Requests);
        Assert.Equal(new DeployRequest("devops", "shipit-demo", "app", "registry.example.com/shipit-demo:abc1234", env.Options.RolloutTimeout), request);

        var logs = await env.GetLogsAsync(runId);
        Assert.Contains(logs, l => l.Step == StepName.Checkout && l.Text == "Cloning into repository...");
        Assert.Contains(logs, l => l.Step == StepName.Build && l.Text.StartsWith("Step 1/3"));
        Assert.Contains(logs, l => l.Step == StepName.Deploy && l.Text.Contains("[fake-k8s]"));
        // ids are the sequence numbers: strictly increasing
        Assert.Equal(logs.Select(l => l.Id).OrderBy(i => i), logs.Select(l => l.Id));
    }

    [Fact]
    public async Task Pipeline_without_registry_credential_skips_login_and_logout_and_succeeds()
    {
        using var env = new EngineTestEnv();
        await env.ClearRegistryCredentialAsync();

        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal("abc1234", run.Tag);
        Assert.Equal("registry.example.com/shipit-demo:abc1234", run.Image);
        Assert.NotNull(run.StartedAt);
        Assert.NotNull(run.FinishedAt);
        Assert.All(Enum.GetValues<StepName>(), n => Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(run, n)));

        var commands = Order(ssh);
        Assert.Equal(["clone", "sha", "build", "push", "cleanup"], commands);
        Assert.DoesNotContain("login", commands);
        Assert.DoesNotContain("logout", commands);
        Assert.DoesNotContain(ssh.ExecutedCommands, c => c.CommandText.Contains("docker login") || c.CommandText.Contains("docker logout"));

        var request = Assert.Single(k8s.Requests);
        Assert.Equal(new DeployRequest("devops", "shipit-demo", "app", "registry.example.com/shipit-demo:abc1234", env.Options.RolloutTimeout), request);

        var logs = await env.GetLogsAsync(runId);
        Assert.Contains(logs, l => l.Step == StepName.Build && l.Stream == LogStream.System && l.Text == "Registry login skipped (no credential)");
        Assert.Contains(logs, l => l.Step == StepName.Build && l.Text.StartsWith("Step 1/3"));
        Assert.Contains(logs, l => l.Step == StepName.Deploy && l.Text.Contains("[fake-k8s]"));
    }

    [Fact]
    public async Task Build_failure_skips_push_and_deploy_but_still_logs_out_and_cleans_up()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave(f => f.AddRule("docker build", 1, stderrLines: ["error: failed to solve"]));
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("build failed: docker build exited with code 1", run.FailureReason);
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(run, StepName.Checkout));
        Assert.Equal(StepStatus.Failed, EngineTestEnv.StepStatusOf(run, StepName.Build));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(run, StepName.Push));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(run, StepName.Deploy));

        Assert.Empty(k8s.Requests);
        var order = Order(ssh);
        Assert.DoesNotContain("push", order);
        Assert.Equal(["logout", "cleanup"], order.TakeLast(2));
        Assert.Contains(await env.GetLogsAsync(runId), l => l.Stream == LogStream.Stderr && l.Text == "error: failed to solve");
    }

    [Fact]
    public async Task Clone_failure_never_logs_in_and_still_cleans_up()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave(f => f.AddRule("git clone", 128, stderrLines: ["fatal: repository not found"]));
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.StartsWith("checkout failed:", run.FailureReason);
        Assert.Equal(StepStatus.Failed, EngineTestEnv.StepStatusOf(run, StepName.Checkout));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(run, StepName.Build));
        Assert.Equal(["clone", "cleanup"], Order(ssh));
        Assert.Empty(k8s.Requests);
    }

    [Fact]
    public async Task Unreadable_commit_sha_fails_checkout()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave(f => f.AddRule("rev-parse --short=7 HEAD", 0, ["not-a-sha"]));
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, new FakeK8sDeployer(EngineTestEnv.AllowDemo())).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("commit sha", run.FailureReason);
        Assert.Null(run.Tag);
    }

    [Fact]
    public async Task Deploy_outside_the_allowlist_fails_the_run()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(new DeployAllowList([new DeployAllowListEntry("devops", ["something-else"])]));
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.StartsWith("deploy failed:", run.FailureReason);
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(run, StepName.Push));
        Assert.Equal(StepStatus.Failed, EngineTestEnv.StepStatusOf(run, StepName.Deploy));
    }

    [Fact]
    public async Task Secrets_never_reach_commands_logs_or_failure_reason()
    {
        using var env = new EngineTestEnv();
        // The slave misbehaves and echoes every secret back, as a buggy docker or git might.
        var ssh = EngineTestEnv.Slave(f =>
        {
            f.AddRule("docker login", 1,
                stdoutLines: [$"login failed for password {EngineTestEnv.RegistryPassword}", EngineTestEnv.SshKeyLine1, EngineTestEnv.SshKeyLine2],
                stderrLines: [$"token was {EngineTestEnv.GitToken}"]);
        });
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, new FakeK8sDeployer(EngineTestEnv.AllowDemo())).ExecuteAsync(runId, CancellationToken.None);

        var secrets = new[] { EngineTestEnv.GitToken, EngineTestEnv.RegistryPassword, EngineTestEnv.SshKeyLine1, EngineTestEnv.SshKeyLine2 };
        var logs = await env.GetLogsAsync(runId);
        Assert.Contains(logs, l => l.Text.Contains("login failed for password ***"));
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(logs, l => l.Text.Contains(secret));
            Assert.DoesNotContain(ssh.ExecutedCommands, c => c.CommandText.Contains(secret));
            Assert.DoesNotContain(secret, (await env.GetRunAsync(runId)).FailureReason ?? string.Empty);
        }

        // The clone and login commands carry their secret on stdin only.
        Assert.True(ssh.ExecutedCommands.First(c => c.CommandText.Contains("git clone")).HasStdin);
        Assert.True(ssh.ExecutedCommands.First(c => c.CommandText.Contains("docker login")).HasStdin);
    }

    [Fact]
    public async Task Cancellation_marks_the_run_interrupted_and_cleans_up()
    {
        using var env = new EngineTestEnv();
        // The build hangs; cleanup commands (logout/rm) are fast.
        var ssh = EngineTestEnv.Slave(f => f.AddRule("docker build", 0, delay: TimeSpan.FromSeconds(30)));
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();
        using var cts = new CancellationTokenSource();

        var execution = env.Runner(ssh, k8s).ExecuteAsync(runId, cts.Token);
        await WaitForAsync(async () => (await env.GetRunAsync(runId)).Steps.Any(s => s.Name == StepName.Build && s.Status == StepStatus.Running));
        await cts.CancelAsync();
        await execution.WaitAsync(TimeSpan.FromSeconds(20));

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(PipelineRunner.InterruptedReason, run.FailureReason);
        Assert.Equal(StepStatus.Failed, EngineTestEnv.StepStatusOf(run, StepName.Build));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(run, StepName.Push));
        Assert.Equal(["logout", "cleanup"], Order(ssh).TakeLast(2));
        Assert.Empty(k8s.Requests);
    }

    [Fact]
    public async Task Wrong_master_key_fails_the_run_without_touching_the_slave()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave();
        var other = new Shipit.Security.AesGcmSecretProtector(Shipit.Security.AesGcmSecretProtector.GenerateKey());
        var runner = new PipelineRunner(env.Factory, ssh, new FakeK8sDeployer(EngineTestEnv.AllowDemo()), other, env.Options,
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<PipelineRunner>.Instance);
        var runId = await env.QueueRunAsync();

        await runner.ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("could not be decrypted", run.FailureReason);
        Assert.Empty(ssh.ExecutedCommands);
        Assert.All(run.Steps, s => Assert.Equal(StepStatus.Skipped, s.Status));
    }

    [Fact]
    public async Task Log_output_is_truncated_after_the_configured_limit()
    {
        using var env = new EngineTestEnv();
        env.Options.MaxLogLines = 20;
        env.Options.MaxLineLength = 50;
        var many = Enumerable.Range(1, 200).Select(i => $"line {i} " + new string('x', 100)).ToList();
        var ssh = EngineTestEnv.Slave(f => f.AddRule("docker build", 0, many));
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, new FakeK8sDeployer(EngineTestEnv.AllowDemo())).ExecuteAsync(runId, CancellationToken.None);

        var logs = await env.GetLogsAsync(runId);
        Assert.True(logs.Count <= 21, $"expected at most 21 stored lines, got {logs.Count}");
        Assert.Contains(logs, l => l.Text.Contains("log truncated"));
        Assert.All(logs.Where(l => l.Stream == LogStream.Stdout), l => Assert.True(l.Text.Length <= 50 + "... [line truncated]".Length));
    }

    [Fact]
    public async Task A_run_only_becomes_final_after_every_log_line_is_stored()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave();
        var runId = await env.QueueRunAsync();

        var execution = env.Runner(ssh, new FakeK8sDeployer(EngineTestEnv.AllowDemo())).ExecuteAsync(runId, CancellationToken.None);

        // A client following the log stops as soon as it sees a final status. At that instant the closing
        // line and the cleanup output (docker logout runs in the finally block) must already be stored.
        await PipelineRunnerTests.WaitForAsync(async () => (await env.GetRunAsync(runId)).Status is RunStatus.Success or RunStatus.Failed);
        var logs = await env.GetLogsAsync(runId);
        await execution;

        Assert.Contains(logs, l => l.Text == "Run completed successfully.");
        Assert.Contains(logs, l => l.Text == "Removing login credentials");
    }

    [Fact]
    public async Task A_run_that_is_not_queued_is_ignored()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave();
        var runId = await env.QueueRunAsync(RunStatus.Success, StepStatus.Success);

        await env.Runner(ssh, new FakeK8sDeployer(EngineTestEnv.AllowDemo())).ExecuteAsync(runId, CancellationToken.None);

        Assert.Empty(ssh.ExecutedCommands);
    }

    [Fact]
    public async Task Build_Command_With_BuildContext_Dot_Uses_WorkDir()
    {
        using var env = new EngineTestEnv();
        await env.SetBuildContextAsync(".", dockerfilePath: "docker/Dockerfile.demo");

        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Success, run.Status);

        var buildCmd = ssh.ExecutedCommands.Single(c => c.CommandText.StartsWith("docker build"));
        var expectedWorkDir = RemoteCommandBuilder.WorkDirFor(runId);
        Assert.Equal($"docker build -f '{expectedWorkDir}/docker/Dockerfile.demo' -t 'registry.example.com/shipit-demo:abc1234' '{expectedWorkDir}'", buildCmd.CommandText);
    }

    [Fact]
    public async Task Build_Command_With_BuildContext_Subpath_Uses_WorkDirSubpath()
    {
        using var env = new EngineTestEnv();
        await env.SetBuildContextAsync("src/app", dockerfilePath: "docker/Dockerfile.demo");

        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Success, run.Status);

        var buildCmd = ssh.ExecutedCommands.Single(c => c.CommandText.StartsWith("docker build"));
        var expectedWorkDir = RemoteCommandBuilder.WorkDirFor(runId);
        Assert.Equal($"docker build -f '{expectedWorkDir}/docker/Dockerfile.demo' -t 'registry.example.com/shipit-demo:abc1234' '{expectedWorkDir}/src/app'", buildCmd.CommandText);
    }

    [Fact]
    public async Task Build_Command_With_Null_BuildContext_Keeps_Existing_Dockerfile_Directory_Behavior()
    {
        using var env = new EngineTestEnv();
        await env.SetBuildContextAsync(null, dockerfilePath: "docker/Dockerfile.demo");

        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Success, run.Status);

        var buildCmd = ssh.ExecutedCommands.Single(c => c.CommandText.StartsWith("docker build"));
        var expectedWorkDir = RemoteCommandBuilder.WorkDirFor(runId);
        Assert.Equal($"docker build -f '{expectedWorkDir}/docker/Dockerfile.demo' -t 'registry.example.com/shipit-demo:abc1234' '{expectedWorkDir}/docker'", buildCmd.CommandText);
    }

    [Fact]
    public async Task Deploy_Uses_DeployRegistryHost_When_Configured_While_Build_And_Push_Use_RegistryHost()
    {
        using var env = new EngineTestEnv();
        const string hostA = "registry.example.com";
        const string hostB = "k8s-mirror.local:5000";
        await env.SetDeployRegistryHostAsync(hostB);

        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal("abc1234", run.Tag);
        // Run.Image retains build/push image (using host A)
        Assert.Equal($"{hostA}/shipit-demo:abc1234", run.Image);

        // docker build and docker push use host A
        var buildCmd = ssh.ExecutedCommands.Single(c => c.CommandText.StartsWith("docker build"));
        Assert.Contains($"-t '{hostA}/shipit-demo:abc1234'", buildCmd.CommandText);

        var pushCmd = ssh.ExecutedCommands.Single(c => c.CommandText.StartsWith("docker push"));
        Assert.Equal($"docker push '{hostA}/shipit-demo:abc1234'", pushCmd.CommandText);

        // deploy request to FakeK8sDeployer uses host B
        var request = Assert.Single(k8s.Requests);
        Assert.Equal($"{hostB}/shipit-demo:abc1234", request.Image);

        // system log in deploy step mentions image with host B
        var logs = await env.GetLogsAsync(runId);
        var deployLogs = logs.Where(l => l.Step == StepName.Deploy).ToList();
        Assert.NotEmpty(deployLogs);
        Assert.Contains(deployLogs, l => l.Text.Contains($"{hostB}/shipit-demo:abc1234"));
    }

    [Fact]
    public async Task Deploy_Uses_RegistryHost_When_DeployRegistryHost_Is_Null()
    {
        using var env = new EngineTestEnv();
        const string hostA = "registry.example.com";
        await env.SetDeployRegistryHostAsync(null);

        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runId = await env.QueueRunAsync();

        await env.Runner(ssh, k8s).ExecuteAsync(runId, CancellationToken.None);

        var run = await env.GetRunAsync(runId);
        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal($"{hostA}/shipit-demo:abc1234", run.Image);

        var request = Assert.Single(k8s.Requests);
        Assert.Equal($"{hostA}/shipit-demo:abc1234", request.Image);

        var logs = await env.GetLogsAsync(runId);
        var deployLogs = logs.Where(l => l.Step == StepName.Deploy).ToList();
        Assert.NotEmpty(deployLogs);
        Assert.Contains(deployLogs, l => l.Text.Contains($"{hostA}/shipit-demo:abc1234"));
    }

    internal static async Task WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Condition was not met in time.");
    }
}
