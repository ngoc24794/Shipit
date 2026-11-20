using Shipit.Data.Entities;
using Shipit.K8s;

namespace Shipit.Engine.Tests;

public sealed class SkipStepsTests
{
    [Fact]
    public void Validator_accepts_null_empty_and_trailing_suffixes()
    {
        Assert.True(SkipStepsValidator.TryParse(null, isJob: false, out var p0, out var e0));
        Assert.Empty(p0);
        Assert.Null(e0);

        Assert.True(SkipStepsValidator.TryParse([], isJob: false, out var p1, out _));
        Assert.Empty(p1);

        Assert.True(SkipStepsValidator.TryParse(["Deploy"], isJob: false, out var p2, out _));
        Assert.Equal([StepName.Deploy], p2);

        Assert.True(SkipStepsValidator.TryParse(["push", "deploy"], isJob: false, out var p3, out _));
        Assert.Equal([StepName.Push, StepName.Deploy], p3.OrderBy(s => s));

        Assert.True(SkipStepsValidator.TryParse(["build", "push", "deploy"], isJob: false, out var p4, out _));
        Assert.Equal(3, p4.Count);

        Assert.True(SkipStepsValidator.TryParse(["runjob"], isJob: true, out var j1, out _));
        Assert.Equal([StepName.RunJob], j1);

        Assert.True(SkipStepsValidator.TryParse(["push", "runjob"], isJob: true, out var j2, out _));
        Assert.Equal(2, j2.Count);
    }

    [Theory]
    [InlineData(new[] { "unknown" }, false)]
    [InlineData(new[] { "deploy", "deploy" }, false)]
    [InlineData(new[] { "checkout" }, false)]
    [InlineData(new[] { "checkout", "build", "push", "deploy" }, false)]
    [InlineData(new[] { "build" }, false)]
    [InlineData(new[] { "push" }, false)]
    [InlineData(new[] { "build", "deploy" }, false)]
    [InlineData(new[] { "deploy" }, true)]
    [InlineData(new[] { "runjob" }, false)]
    [InlineData(new[] { "build", "runjob" }, true)]
    public void Validator_rejects_invalid_skipSteps(string[] skipSteps, bool isJob)
    {
        var ok = SkipStepsValidator.TryParse(skipSteps, isJob, out _, out var error);
        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task Pipeline_run_with_skipped_deploy_precreates_skipped_step_never_calls_deployer_and_succeeds()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runner = env.Runner(ssh, k8s);
        var coordinator = env.Coordinator(runner);

        var skip = new HashSet<StepName> { StepName.Deploy };
        var run = await coordinator.TriggerAsync(env.PipelineId, CancellationToken.None, skip);

        var queued = await env.GetRunAsync(run.Id);
        Assert.Equal(StepStatus.Pending, EngineTestEnv.StepStatusOf(queued, StepName.Checkout));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(queued, StepName.Deploy));

        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        var finished = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Success, finished.Status);
        Assert.Null(finished.FailureReason);
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(finished, StepName.Checkout));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(finished, StepName.Build));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(finished, StepName.Push));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(finished, StepName.Deploy));

        var deployStep = finished.Steps.Single(s => s.Name == StepName.Deploy);
        Assert.Null(deployStep.StartedAt);
        Assert.Null(deployStep.FinishedAt);

        // Deployer must never have been invoked.
        Assert.Empty(k8s.Requests);
    }

    [Fact]
    public async Task Pipeline_run_with_skipped_push_and_deploy_succeeds()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var runner = env.Runner(ssh, k8s);
        var coordinator = env.Coordinator(runner);

        var skip = new HashSet<StepName> { StepName.Push, StepName.Deploy };
        var run = await coordinator.TriggerAsync(env.PipelineId, CancellationToken.None, skip);

        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        var finished = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Success, finished.Status);
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(finished, StepName.Checkout));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(finished, StepName.Build));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(finished, StepName.Push));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(finished, StepName.Deploy));
        Assert.Empty(k8s.Requests);
    }

    [Fact]
    public async Task Job_run_with_skipped_runjob_marks_step_and_tasks_skipped_never_calls_job_runner_and_succeeds()
    {
        using var env = new EngineTestEnv();
        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault());
        var ssh = EngineTestEnv.Slave();
        var runner = env.Runner(ssh, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync();
        var skip = new HashSet<StepName> { StepName.RunJob };
        var run = await coordinator.TriggerJobAsync(jobId, CancellationToken.None, skip);

        var queuedTasks = await env.GetRunTasksAsync(run.Id);
        Assert.All(queuedTasks, t => Assert.Equal(StepStatus.Skipped, t.Status));

        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        var finished = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Success, finished.Status);
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(finished, StepName.Checkout));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(finished, StepName.Build));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(finished, StepName.Push));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(finished, StepName.RunJob));

        var finishedTasks = await env.GetRunTasksAsync(run.Id);
        Assert.All(finishedTasks, t => Assert.Equal(StepStatus.Skipped, t.Status));
        Assert.Empty(fakeK8s.Requests);
    }
}
