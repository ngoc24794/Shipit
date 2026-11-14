using Shipit.Core;
using Xunit;

namespace Shipit.K8s.Tests;

public class FakeK8sJobRunnerTests
{
    private static JobAllowList CreateDefaultAllowList() =>
        new(
            allowedNamespaces: ["devops", "avina-workspace"],
            allowedConfigMaps: [("devops", "app-config")],
            allowedSecrets: [("devops", "app-secret")],
            allowedPullSecrets: [("devops", "regcred")]);

    private static JobTaskRequest CreateRequest(
        string ns = "devops",
        string jobName = "test-job-1",
        string? position = "1") =>
        new(
            Namespace: ns,
            JobName: jobName,
            ContainerName: "migrator",
            Image: "busybox:latest",
            Args: ["echo", "test"],
            EnvFromConfigMaps: ["app-config"],
            EnvFromSecrets: ["app-secret"],
            Env: [new JobEnvVar("FOO", "bar")],
            ImagePullSecrets: ["regcred"],
            ServiceAccount: null,
            Labels: position != null ? new Dictionary<string, string> { ["shipit/task"] = position } : new Dictionary<string, string>(),
            Timeout: TimeSpan.FromMinutes(5),
            BackoffLimit: 0,
            Ttl: TimeSpan.FromHours(1));

    [Fact]
    public async Task OutsideAllowList_RejectsWithoutRunningScenario()
    {
        var fake = new FakeK8sJobRunner(CreateDefaultAllowList());
        var log = new TestLogSink();

        var req = CreateRequest(ns: "forbidden-ns");
        var result = await fake.RunAsync(req, log);

        Assert.False(result.Success);
        Assert.Contains("Namespace 'forbidden-ns' is not allowed", result.Message);
        Assert.Single(fake.Requests);
        Assert.Empty(log.Messages);
    }

    [Fact]
    public async Task DefaultScenario_Succeeds_AndWritesLogs()
    {
        var fake = new FakeK8sJobRunner(CreateDefaultAllowList());
        var log = new TestLogSink();

        var req = CreateRequest();
        var result = await fake.RunAsync(req, log);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Job completed successfully", result.Message);
        Assert.NotEmpty(log.Messages);
    }

    [Fact]
    public async Task ConfiguredByJobName_ExecutesConfiguredScenario()
    {
        var fake = new FakeK8sJobRunner(CreateDefaultAllowList());
        fake.ScenariosByJobName["special-job"] = new FakeJobScenario
        {
            ExitCode = 42,
            FailureMessage = "special failure",
            LogLines = ["custom log line 1", "custom log line 2"]
        };

        var log = new TestLogSink();
        var req = CreateRequest(jobName: "special-job");
        var result = await fake.RunAsync(req, log);

        Assert.False(result.Success);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("special failure", result.Message);

        var lines = log.Messages.Select(m => m.Line).ToList();
        Assert.Equal(["custom log line 1", "custom log line 2"], lines);
    }

    [Fact]
    public async Task ConfiguredByPosition_ExecutesConfiguredScenario()
    {
        var fake = new FakeK8sJobRunner(CreateDefaultAllowList());
        fake.ScenariosByPosition["2"] = new FakeJobScenario
        {
            ExitCode = 1,
            FailureMessage = "task 2 failed",
            LogLines = ["task 2 running..."]
        };

        var log = new TestLogSink();
        var req = CreateRequest(jobName: "job-task-2", position: "2");
        var result = await fake.RunAsync(req, log);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("task 2 failed", result.Message);
    }

    [Fact]
    public async Task SimulateTimeout_ReturnsTimedOut124()
    {
        var fake = new FakeK8sJobRunner(CreateDefaultAllowList());
        fake.DefaultScenario = new FakeJobScenario
        {
            SimulateTimeout = true
        };

        var log = new TestLogSink();
        var req = CreateRequest();
        var result = await fake.RunAsync(req, log);

        Assert.False(result.Success);
        Assert.True(result.TimedOut);
        Assert.Equal(124, result.ExitCode);
    }

    [Fact]
    public async Task HangUntilCancelled_ThrowsOperationCanceledException_WhenCancelled()
    {
        var fake = new FakeK8sJobRunner(CreateDefaultAllowList());
        fake.DefaultScenario = new FakeJobScenario
        {
            HangUntilCancelled = true
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var log = new TestLogSink();
        var req = CreateRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await fake.RunAsync(req, log, cts.Token);
        });
    }

    [Fact]
    public async Task DeleteByRunAsync_RecordsDeletedRun()
    {
        var fake = new FakeK8sJobRunner(CreateDefaultAllowList());

        await fake.DeleteByRunAsync("devops", 101);
        await fake.DeleteByRunAsync("avina-workspace", 202);

        Assert.Equal(2, fake.DeletedRuns.Count);
        Assert.Equal(("devops", 101L), fake.DeletedRuns[0]);
        Assert.Equal(("avina-workspace", 202L), fake.DeletedRuns[1]);
    }
}
