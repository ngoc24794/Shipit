using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data.Entities;
using Shipit.K8s;
using Shipit.Ssh;
using Xunit;

namespace Shipit.Engine.Tests;

public sealed class JobRunnerTests
{
    [Fact]
    public async Task TwoTasks_Success_RecordsRunTasksAndLogPositions()
    {
        using var env = new EngineTestEnv();
        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault());
        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync();
        var run = await coordinator.TriggerJobAsync(jobId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        var dbRun = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Success, dbRun.Status);
        Assert.Null(dbRun.FailureReason);

        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(dbRun, StepName.Checkout));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(dbRun, StepName.Build));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(dbRun, StepName.Push));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(dbRun, StepName.RunJob));

        var tasks = await env.GetRunTasksAsync(run.Id);
        Assert.Equal(2, tasks.Count);
        Assert.Equal(StepStatus.Success, tasks[0].Status);
        Assert.Equal(0, tasks[0].ExitCode);
        Assert.NotNull(tasks[0].K8sJobName);
        Assert.NotNull(tasks[0].StartedAt);
        Assert.NotNull(tasks[0].FinishedAt);

        Assert.Equal(StepStatus.Success, tasks[1].Status);
        Assert.Equal(0, tasks[1].ExitCode);
        Assert.NotNull(tasks[1].K8sJobName);

        var logs = await env.GetLogsAsync(run.Id);
        var jobLogs = logs.Where(l => l.Step == StepName.RunJob).ToList();
        Assert.NotEmpty(jobLogs);
        Assert.Contains(jobLogs, l => l.TaskPosition == 1);
        Assert.Contains(jobLogs, l => l.TaskPosition == 2);
    }

    [Fact]
    public async Task Task1Fails_Exit3_Task2Skipped_RunFails()
    {
        using var env = new EngineTestEnv();
        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault());
        fakeK8s.ScenariosByPosition["1"] = new FakeJobScenario { ExitCode = 3, FailureMessage = "job exited with code 3" };
        fakeK8s.ScenariosByPosition["2"] = new FakeJobScenario { ExitCode = 0 };

        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync();
        var run = await coordinator.TriggerJobAsync(jobId);

        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        var dbRun = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, dbRun.Status);
        Assert.Contains("exited with code 3", dbRun.FailureReason);
        Assert.Contains("1", dbRun.FailureReason);
        Assert.Contains("task-1", dbRun.FailureReason);

        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(dbRun, StepName.Checkout));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(dbRun, StepName.Build));
        Assert.Equal(StepStatus.Success, EngineTestEnv.StepStatusOf(dbRun, StepName.Push));
        Assert.Equal(StepStatus.Failed, EngineTestEnv.StepStatusOf(dbRun, StepName.RunJob));

        var tasks = await env.GetRunTasksAsync(run.Id);
        Assert.Equal(2, tasks.Count);
        Assert.Equal(StepStatus.Failed, tasks[0].Status);
        Assert.Equal(3, tasks[0].ExitCode);

        Assert.Equal(StepStatus.Skipped, tasks[1].Status);
        Assert.Null(tasks[1].ExitCode);
    }

    [Fact]
    public async Task TaskTimeout_FailedWithJobTimedOut()
    {
        using var env = new EngineTestEnv();
        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault());
        fakeK8s.DefaultScenario = new FakeJobScenario { SimulateTimeout = true };

        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync();
        var run = await coordinator.TriggerJobAsync(jobId);

        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        var dbRun = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, dbRun.Status);
        Assert.Contains("job timed out", dbRun.FailureReason);
        Assert.Equal(StepStatus.Failed, EngineTestEnv.StepStatusOf(dbRun, StepName.RunJob));

        var tasks = await env.GetRunTasksAsync(run.Id);
        Assert.Equal(StepStatus.Failed, tasks[0].Status);
        Assert.Equal(StepStatus.Skipped, tasks[1].Status);
    }

    [Fact]
    public async Task AllowListBlocks_RunnerNotCalled()
    {
        using var env = new EngineTestEnv();
        var restrictiveAllowList = new JobAllowList(allowedNamespaces: ["other-namespace"]);
        var fakeK8s = new FakeK8sJobRunner(restrictiveAllowList);

        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: restrictiveAllowList);
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync(ns: "devops");
        var run = await coordinator.TriggerJobAsync(jobId);

        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        // IK8sJobRunner must NOT have been called!
        Assert.Empty(fakeK8s.Requests);

        var dbRun = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, dbRun.Status);
        Assert.Contains("not allowed", dbRun.FailureReason);

        var tasks = await env.GetRunTasksAsync(run.Id);
        Assert.Equal(StepStatus.Failed, tasks[0].Status);
        Assert.Equal(StepStatus.Skipped, tasks[1].Status);
    }

    [Fact]
    public async Task CancelMidTask_MarksCancelled_AndCleansUp()
    {
        using var env = new EngineTestEnv();
        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault());
        fakeK8s.DefaultScenario = new FakeJobScenario { HangUntilCancelled = true };

        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync();
        var run = await coordinator.TriggerJobAsync(jobId);

        using var userCts = new CancellationTokenSource();
        var executeTask = Task.Run(() => runner.ExecuteAsync(run.Id, CancellationToken.None, userCts.Token));

        // Poll until task 1 is running
        for (var i = 0; i < 50; i++)
        {
            var tasks = await env.GetRunTasksAsync(run.Id);
            if (tasks.Any(t => t.Status == StepStatus.Running))
            {
                break;
            }
            await Task.Delay(50);
        }

        userCts.Cancel();
        await executeTask;

        var dbRun = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Cancelled, dbRun.Status);
        Assert.Equal("cancelled by user", dbRun.FailureReason);

        var finalTasks = await env.GetRunTasksAsync(run.Id);
        Assert.Equal(StepStatus.Cancelled, finalTasks[0].Status);
        Assert.Equal(StepStatus.Cancelled, finalTasks[1].Status);
        Assert.Equal(StepStatus.Cancelled, EngineTestEnv.StepStatusOf(dbRun, StepName.RunJob));

        // Slave cleanup must have run
        Assert.Contains(slave.ExecutedCommands, c => c.CommandText.Contains("docker logout"));
    }

    [Fact]
    public async Task TriggerJobAsync_Conflict_ThrowsJobRunConflictException_AndAllowsParallelPipeline()
    {
        using var env = new EngineTestEnv();
        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault());
        fakeK8s.DefaultScenario = new FakeJobScenario { HangUntilCancelled = true };

        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync();
        var jobRun = await coordinator.TriggerJobAsync(jobId);

        // Triggering the same job again while active throws RunConflictException / JobRunConflictException
        var ex = await Assert.ThrowsAsync<JobRunConflictException>(() => coordinator.TriggerJobAsync(jobId));
        Assert.Equal(jobId, ex.JobId);
        Assert.IsAssignableFrom<RunConflictException>(ex);

        // Different workload (Pipeline) can be triggered without conflict
        var pipelineRun = await coordinator.TriggerAsync(env.PipelineId);
        Assert.NotNull(pipelineRun);
        Assert.Equal(RunStatus.Queued, pipelineRun.Status);
    }

    [Fact]
    public async Task LogMasker_MasksFakeSecretsInLogs()
    {
        using var env = new EngineTestEnv();
        const string customSecret = "secret-super-token-123456";
        env.Options.AdditionalSecretsToMask.Add(customSecret);

        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault())
        {
            DefaultScenario = new FakeJobScenario
            {
                ExitCode = 0,
                LogLines =
                [
                    $"Starting task with git token {EngineTestEnv.GitToken}",
                    $"Registry secret is {EngineTestEnv.RegistryPassword}",
                    $"Custom runner secret: {customSecret}"
                ]
            }
        };

        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync();
        var run = await coordinator.TriggerJobAsync(jobId);

        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        var logs = await env.GetLogsAsync(run.Id);
        foreach (var log in logs)
        {
            Assert.DoesNotContain(EngineTestEnv.GitToken, log.Text);
            Assert.DoesNotContain(EngineTestEnv.RegistryPassword, log.Text);
            Assert.DoesNotContain(customSecret, log.Text);
        }

        // Verify the masked marker was actually written
        var jobLogs = logs.Where(l => l.Step == StepName.RunJob).Select(l => l.Text).ToList();
        Assert.Contains(jobLogs, l => l.Contains("***"));
    }

    [Fact]
    public async Task RecoverAsync_RunningJobRun_Interrupted_AndDeletesK8sOrphans()
    {
        using var env = new EngineTestEnv();
        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault());
        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync(ns: "devops");

        // Seed a run that was left Running (e.g. from server crash)
        await using (var db = await env.Factory.CreateDbContextAsync())
        {
            var run = new Run
            {
                JobId = jobId,
                Status = RunStatus.Running,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                Steps = new List<RunStep>
                {
                    new() { Name = StepName.Checkout, Status = StepStatus.Success },
                    new() { Name = StepName.Build, Status = StepStatus.Success },
                    new() { Name = StepName.Push, Status = StepStatus.Success },
                    new() { Name = StepName.RunJob, Status = StepStatus.Running },
                },
                Tasks = new List<RunTask>
                {
                    new() { Position = 1, Label = "task-1", Status = StepStatus.Running, K8sJobName = "demo-job-1" },
                    new() { Position = 2, Label = "task-2", Status = StepStatus.Pending },
                }
            };
            db.Runs.Add(run);
            await db.SaveChangesAsync();

            await coordinator.RecoverAsync();

            var dbRun = await db.Runs.AsNoTracking().Include(r => r.Steps).Include(r => r.Tasks).FirstAsync(r => r.Id == run.Id);
            Assert.Equal(RunStatus.Failed, dbRun.Status);
            Assert.Equal("interrupted", dbRun.FailureReason);
            Assert.Equal(StepStatus.Failed, dbRun.Steps.Single(s => s.Name == StepName.RunJob).Status);
            Assert.Equal(StepStatus.Failed, dbRun.Tasks.Single(t => t.Position == 1).Status);
            Assert.Equal(StepStatus.Skipped, dbRun.Tasks.Single(t => t.Position == 2).Status);

            // DeleteByRunAsync must have been called for this orphan run
            Assert.Contains(fakeK8s.DeletedRuns, d => d.Namespace == "devops" && d.RunId == run.Id);
        }
    }

    [Fact]
    public async Task CorruptedJson_FailsGracefullyWithoutThrowing()
    {
        using var env = new EngineTestEnv();
        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault());
        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var badTasks = new List<JobTask>
        {
            new() { Position = 1, Label = "bad-task", ArgsJson = "{not-an-array", TimeoutSeconds = 60 },
            new() { Position = 2, Label = "good-task", ArgsJson = "[\"ok\"]", TimeoutSeconds = 60 },
        };

        var jobId = await env.CreateJobAsync(tasks: badTasks);
        var run = await coordinator.TriggerJobAsync(jobId);

        // Does not throw
        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        var dbRun = await env.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, dbRun.Status);
        Assert.Contains("invalid JSON", dbRun.FailureReason);
        Assert.Equal(StepStatus.Failed, EngineTestEnv.StepStatusOf(dbRun, StepName.RunJob));

        var tasks = await env.GetRunTasksAsync(run.Id);
        Assert.Equal(StepStatus.Failed, tasks[0].Status);
        Assert.Equal(StepStatus.Skipped, tasks[1].Status);
    }

    [Fact]
    public async Task TriggerJobAsync_NonExistentJob_ThrowsJobNotFoundException()
    {
        using var env = new EngineTestEnv();
        var coordinator = env.Coordinator(env.Runner(EngineTestEnv.Slave()));

        await Assert.ThrowsAsync<JobNotFoundException>(() => coordinator.TriggerJobAsync(999999));
    }

    [Fact]
    public async Task ClusterImage_UsesDeployRegistryHost_WhenSpecified()
    {
        using var env = new EngineTestEnv();
        var fakeK8s = new FakeK8sJobRunner(EngineTestEnv.AllowJobDefault());
        var slave = EngineTestEnv.Slave();
        var runner = env.Runner(slave, jobRunner: fakeK8s, allowList: EngineTestEnv.AllowJobDefault());
        var coordinator = env.Coordinator(runner, fakeK8s);

        var jobId = await env.CreateJobAsync(deployRegistryHost: "cluster-registry.internal:5000");
        var run = await coordinator.TriggerJobAsync(jobId);

        await runner.ExecuteAsync(run.Id, CancellationToken.None, CancellationToken.None);

        Assert.NotEmpty(fakeK8s.Requests);
        Assert.StartsWith("cluster-registry.internal:5000/shipit-demo-job:", fakeK8s.Requests[0].Image);
    }
}
