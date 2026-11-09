using Microsoft.EntityFrameworkCore;
using Shipit.Data.Entities;
using Shipit.K8s;
using Shipit.Ssh;

namespace Shipit.Engine.Tests;

public class RunCoordinatorTests
{
    [Fact]
    public async Task Trigger_creates_a_queued_run_with_four_pending_steps()
    {
        using var env = new EngineTestEnv();
        var coordinator = env.Coordinator(env.Runner(EngineTestEnv.Slave(), new FakeK8sDeployer(EngineTestEnv.AllowDemo())));

        var run = await coordinator.TriggerAsync(env.PipelineId);

        var stored = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Queued, stored.Status);
        Assert.Equal(4, stored.Steps.Count);
        Assert.All(stored.Steps, s => Assert.Equal(StepStatus.Pending, s.Status));
    }

    [Fact]
    public async Task Trigger_for_an_unknown_pipeline_throws_not_found()
    {
        using var env = new EngineTestEnv();
        var coordinator = env.Coordinator(env.Runner(EngineTestEnv.Slave(), new FakeK8sDeployer(EngineTestEnv.AllowDemo())));

        await Assert.ThrowsAsync<PipelineNotFoundException>(() => coordinator.TriggerAsync(9999));
    }

    [Fact]
    public async Task A_second_trigger_is_rejected_while_the_first_run_is_active()
    {
        using var env = new EngineTestEnv();
        var coordinator = env.Coordinator(env.Runner(EngineTestEnv.Slave(), new FakeK8sDeployer(EngineTestEnv.AllowDemo())));

        await coordinator.TriggerAsync(env.PipelineId);

        var ex = await Assert.ThrowsAsync<RunConflictException>(() => coordinator.TriggerAsync(env.PipelineId));
        Assert.Equal(env.PipelineId, ex.PipelineId);
    }

    [Fact]
    public async Task Concurrent_triggers_create_exactly_one_run()
    {
        using var env = new EngineTestEnv();
        var coordinator = env.Coordinator(env.Runner(EngineTestEnv.Slave(), new FakeK8sDeployer(EngineTestEnv.AllowDemo())));

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                await coordinator.TriggerAsync(env.PipelineId);
                return true;
            }
            catch (RunConflictException)
            {
                return false;
            }
        });
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, ok => ok);
        await using var db = await env.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Runs.CountAsync());
    }

    [Fact]
    public async Task The_hosted_loop_executes_a_triggered_run_and_allows_a_new_trigger_afterwards()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());
        var coordinator = env.Coordinator(env.Runner(ssh, k8s));
        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            var first = await coordinator.TriggerAsync(env.PipelineId);
            await PipelineRunnerTests.WaitForAsync(async () => (await env.GetRunAsync(first.Id)).Status == RunStatus.Success);

            var second = await coordinator.TriggerAsync(env.PipelineId);
            await PipelineRunnerTests.WaitForAsync(async () => (await env.GetRunAsync(second.Id)).Status == RunStatus.Success);

            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(2, k8s.Requests.Count);
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Recovery_marks_running_runs_interrupted_and_requeues_queued_runs()
    {
        using var env = new EngineTestEnv();
        var ssh = EngineTestEnv.Slave();
        var k8s = new FakeK8sDeployer(EngineTestEnv.AllowDemo());

        // Left behind by a crashed process: one run mid-way, plus an old failed run so the index allows a queued one.
        long running;
        await using (var db = await env.Factory.CreateDbContextAsync())
        {
            var r = new Run
            {
                PipelineId = env.PipelineId,
                Status = RunStatus.Running,
                StartedAt = DateTime.UtcNow,
                Steps =
                [
                    new RunStep { Name = StepName.Checkout, Status = StepStatus.Success },
                    new RunStep { Name = StepName.Build, Status = StepStatus.Running },
                    new RunStep { Name = StepName.Push, Status = StepStatus.Pending },
                    new RunStep { Name = StepName.Deploy, Status = StepStatus.Pending },
                ],
            };
            db.Runs.Add(r);
            await db.SaveChangesAsync();
            running = r.Id;
        }

        var coordinator = env.Coordinator(env.Runner(ssh, k8s));
        await coordinator.RecoverAsync();

        var interrupted = await env.GetRunAsync(running);
        Assert.Equal(RunStatus.Failed, interrupted.Status);
        Assert.Equal(PipelineRunner.InterruptedReason, interrupted.FailureReason);
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(interrupted, StepName.Checkout));
        Assert.Equal(StepStatus.Failed, EngineTestEnv.StepStatusOf(interrupted, StepName.Build));
        Assert.Equal(StepStatus.Skipped, EngineTestEnv.StepStatusOf(interrupted, StepName.Push));
        Assert.Contains(await env.GetLogsAsync(running), l => l.Text.Contains("interrupted"));

        // The pipeline is free again, and a queued run left in the database is picked up after a restart.
        var queued = await env.QueueRunAsync();
        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            await PipelineRunnerTests.WaitForAsync(async () => (await env.GetRunAsync(queued)).Status == RunStatus.Success);
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }
}
