using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data.Entities;

namespace Shipit.Data.Tests;

public class JobEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ShipitDbContext> _options;

    public JobEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();

        _options = new DbContextOptionsBuilder<ShipitDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    private ShipitDbContext CreateContext() => new(_options);

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(Credential gitCred, Credential regCred, Slave slave)> SeedBaseResourcesAsync(ShipitDbContext ctx)
    {
        var gitCred = new Credential { Name = $"git-{Guid.NewGuid():N}", Kind = CredentialKind.GitToken, ProtectedValue = "v1:dummy" };
        var regCred = new Credential { Name = $"reg-{Guid.NewGuid():N}", Kind = CredentialKind.Registry, Username = "ci", ProtectedValue = "v1:dummy" };
        var sshCred = new Credential { Name = $"ssh-{Guid.NewGuid():N}", Kind = CredentialKind.SshKey, ProtectedValue = "v1:dummy" };
        ctx.Credentials.AddRange(gitCred, regCred, sshCred);
        await ctx.SaveChangesAsync();

        var slave = new Slave { Name = $"slave-{Guid.NewGuid():N}", Host = "192.168.1.50", User = "devops", SshCredentialId = sshCred.Id };
        ctx.Slaves.Add(slave);
        await ctx.SaveChangesAsync();

        return (gitCred, regCred, slave);
    }

    [Fact]
    public async Task Can_Create_And_Read_Job_Task_AllowEntry_And_RunTask()
    {
        long jobId;
        long allowEntryId;
        long runId;

        using (var ctx = CreateContext())
        {
            var (gitCred, regCred, slave) = await SeedBaseResourcesAsync(ctx);

            var job = new Job
            {
                Name = "avina-dbmigrator",
                GitUrl = "https://git.example.com/avina.git",
                GitBranch = "main",
                GitCredentialId = gitCred.Id,
                SlaveId = slave.Id,
                DockerfilePath = "docker/Dockerfile.dbmigrator",
                BuildContext = ".",
                ImageName = "avina-dbmigrator",
                RegistryHost = "registry.local",
                DeployRegistryHost = "10.0.0.1:5000",
                RegistryCredentialId = regCred.Id,
                Namespace = "avina-workspace",
                ContainerName = "migrator",
                ServiceAccount = "migrator-sa",
                ImagePullSecretsJson = "[\"regcred\"]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            ctx.Jobs.Add(job);
            await ctx.SaveChangesAsync();
            jobId = job.Id;

            var task = new JobTask
            {
                JobId = jobId,
                Position = 0,
                Label = "migrate",
                ArgsJson = "[\"--run\"]",
                EnvFromConfigMapsJson = "[\"avina-config\"]",
                EnvFromSecretsJson = "[\"avina-secret\"]",
                EnvJson = "[{\"Name\":\"MIGRATE_MODE\",\"Value\":\"Apply\"}]",
                TimeoutSeconds = 1800,
                BackoffLimit = 0,
                TtlSeconds = 3600,
            };
            ctx.JobTasks.Add(task);

            var allow = new JobAllowEntry
            {
                Namespace = "avina-workspace",
                Kind = JobAllowKind.Secret,
                Name = "avina-secret",
                CreatedAt = DateTime.UtcNow,
            };
            ctx.JobAllowEntries.Add(allow);
            await ctx.SaveChangesAsync();
            allowEntryId = allow.Id;

            var run = new Run
            {
                JobId = jobId,
                PipelineId = null,
                Status = RunStatus.Running,
                Tag = "abc1234",
                Image = "registry.local/avina-dbmigrator:abc1234",
            };
            ctx.Runs.Add(run);
            await ctx.SaveChangesAsync();
            runId = run.Id;

            var runTask = new RunTask
            {
                RunId = runId,
                Position = 0,
                Label = "migrate",
                Status = StepStatus.Running,
                K8sJobName = "avina-dbmigrator-1-0",
                StartedAt = DateTime.UtcNow,
            };
            ctx.RunTasks.Add(runTask);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var loadedJob = await ctx.Jobs.AsNoTracking()
                .Include(j => j.Tasks)
                .Include(j => j.Runs)
                .FirstOrDefaultAsync(j => j.Id == jobId);
            Assert.NotNull(loadedJob);
            Assert.Equal("avina-dbmigrator", loadedJob.Name);
            Assert.Equal("avina-workspace", loadedJob.Namespace);
            Assert.Equal(".", loadedJob.BuildContext);
            Assert.Equal("10.0.0.1:5000", loadedJob.DeployRegistryHost);
            Assert.Single(loadedJob.Tasks);
            Assert.Equal("migrate", loadedJob.Tasks.First().Label);
            Assert.Equal(1800, loadedJob.Tasks.First().TimeoutSeconds);

            var loadedAllow = await ctx.JobAllowEntries.AsNoTracking().FirstOrDefaultAsync(a => a.Id == allowEntryId);
            Assert.NotNull(loadedAllow);
            Assert.Equal(JobAllowKind.Secret, loadedAllow.Kind);
            Assert.Equal("avina-secret", loadedAllow.Name);

            var loadedRun = await ctx.Runs.AsNoTracking()
                .Include(r => r.Tasks)
                .FirstOrDefaultAsync(r => r.Id == runId);
            Assert.NotNull(loadedRun);
            Assert.Equal(jobId, loadedRun.JobId);
            Assert.Null(loadedRun.PipelineId);
            Assert.Single(loadedRun.Tasks);
            Assert.Equal("migrate", loadedRun.Tasks.First().Label);
            Assert.Equal("avina-dbmigrator-1-0", loadedRun.Tasks.First().K8sJobName);
        }
    }

    [Fact]
    public async Task CheckConstraint_CK_Runs_PipelineXorJob_Enforced()
    {
        long pipelineId;
        long jobId;

        using (var ctx = CreateContext())
        {
            var (gitCred, regCred, slave) = await SeedBaseResourcesAsync(ctx);

            var pipe = new Pipeline
            {
                Name = "demo-pipe",
                GitUrl = "https://git.example.com/repo.git",
                GitCredentialId = gitCred.Id,
                SlaveId = slave.Id,
                ImageName = "demo",
                RegistryHost = "registry.local",
                DeployNamespace = "devops",
                DeployDeployment = "demo-dep",
                DeployContainer = "app",
            };
            ctx.Pipelines.Add(pipe);

            var job = new Job
            {
                Name = "demo-job",
                GitUrl = "https://git.example.com/repo.git",
                GitCredentialId = gitCred.Id,
                SlaveId = slave.Id,
                ImageName = "demo-job",
                RegistryHost = "registry.local",
                Namespace = "devops",
            };
            ctx.Jobs.Add(job);
            await ctx.SaveChangesAsync();

            pipelineId = pipe.Id;
            jobId = job.Id;
        }

        // Neither: both null -> throws
        using (var ctx = CreateContext())
        {
            var runNeither = new Run { PipelineId = null, JobId = null, Status = RunStatus.Queued };
            ctx.Runs.Add(runNeither);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // Both: both set -> throws
        using (var ctx = CreateContext())
        {
            var runBoth = new Run { PipelineId = pipelineId, JobId = jobId, Status = RunStatus.Queued };
            ctx.Runs.Add(runBoth);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // Valid: only PipelineId
        using (var ctx = CreateContext())
        {
            var runPipe = new Run { PipelineId = pipelineId, JobId = null, Status = RunStatus.Queued };
            ctx.Runs.Add(runPipe);
            await ctx.SaveChangesAsync();
            Assert.True(runPipe.Id > 0);
        }

        // Valid: only JobId
        using (var ctx = CreateContext())
        {
            var runJob = new Run { PipelineId = null, JobId = jobId, Status = RunStatus.Queued };
            ctx.Runs.Add(runJob);
            await ctx.SaveChangesAsync();
            Assert.True(runJob.Id > 0);
        }
    }

    [Fact]
    public async Task UniqueConstraint_JobId_Position_Enforced()
    {
        using var ctx = CreateContext();
        var (gitCred, _, slave) = await SeedBaseResourcesAsync(ctx);

        var job = new Job
        {
            Name = "multi-task-job",
            GitUrl = "https://git.example.com/repo.git",
            GitCredentialId = gitCred.Id,
            SlaveId = slave.Id,
            ImageName = "multi-task",
            RegistryHost = "reg.local",
            Namespace = "devops",
        };
        ctx.Jobs.Add(job);
        await ctx.SaveChangesAsync();

        var task1 = new JobTask { JobId = job.Id, Position = 0, Label = "task-0", ArgsJson = "[]" };
        ctx.JobTasks.Add(task1);
        await ctx.SaveChangesAsync();

        // Duplicate position for same job -> throws
        var taskDuplicate = new JobTask { JobId = job.Id, Position = 0, Label = "task-0-dup", ArgsJson = "[]" };
        ctx.JobTasks.Add(taskDuplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task UniqueConstraint_JobAllowEntry_Namespace_Kind_Name_Enforced()
    {
        using (var ctx = CreateContext())
        {
            var entry1 = new JobAllowEntry
            {
                Namespace = "avina-workspace",
                Kind = JobAllowKind.ConfigMap,
                Name = "app-config",
            };
            ctx.JobAllowEntries.Add(entry1);
            await ctx.SaveChangesAsync();
        }

        // Duplicate (Namespace, Kind, Name) -> throws
        using (var ctx = CreateContext())
        {
            var duplicate = new JobAllowEntry
            {
                Namespace = "avina-workspace",
                Kind = JobAllowKind.ConfigMap,
                Name = "app-config",
            };
            ctx.JobAllowEntries.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        using (var ctx = CreateContext())
        {
            // Same name but different kind -> allowed
            var diffKind = new JobAllowEntry
            {
                Namespace = "avina-workspace",
                Kind = JobAllowKind.Secret,
                Name = "app-config",
            };

            // Same kind and name but different namespace -> allowed
            var diffNs = new JobAllowEntry
            {
                Namespace = "other-ns",
                Kind = JobAllowKind.ConfigMap,
                Name = "app-config",
            };

            ctx.JobAllowEntries.AddRange(diffKind, diffNs);
            await ctx.SaveChangesAsync();

            Assert.Equal(3, await ctx.JobAllowEntries.CountAsync());
        }
    }

    [Fact]
    public async Task CascadeDelete_DeletingJob_DeletesTasksAndRuns()
    {
        long jobId;
        long runId;

        using (var ctx = CreateContext())
        {
            var (gitCred, _, slave) = await SeedBaseResourcesAsync(ctx);

            var job = new Job
            {
                Name = "cascade-job",
                GitUrl = "https://git.example.com/repo.git",
                GitCredentialId = gitCred.Id,
                SlaveId = slave.Id,
                ImageName = "cascade-job",
                RegistryHost = "reg.local",
                Namespace = "devops",
            };
            ctx.Jobs.Add(job);
            await ctx.SaveChangesAsync();
            jobId = job.Id;

            ctx.JobTasks.Add(new JobTask { JobId = jobId, Position = 0, Label = "t0", ArgsJson = "[]" });
            ctx.JobTasks.Add(new JobTask { JobId = jobId, Position = 1, Label = "t1", ArgsJson = "[]" });

            var run = new Run { JobId = jobId, Status = RunStatus.Success };
            ctx.Runs.Add(run);
            await ctx.SaveChangesAsync();
            runId = run.Id;

            ctx.RunTasks.Add(new RunTask { RunId = runId, Position = 0, Label = "t0", Status = StepStatus.Success });
            ctx.RunLogs.Add(new RunLog { RunId = runId, Step = StepName.RunJob, Stream = LogStream.Stdout, Text = "done", TaskPosition = 0 });
            await ctx.SaveChangesAsync();
        }

        // Delete Job
        using (var ctx = CreateContext())
        {
            var toDelete = await ctx.Jobs.FirstAsync(j => j.Id == jobId);
            ctx.Jobs.Remove(toDelete);
            await ctx.SaveChangesAsync();
        }

        // Verify tasks, runs, run tasks, run logs are all gone
        using (var ctx = CreateContext())
        {
            Assert.Null(await ctx.Jobs.FirstOrDefaultAsync(j => j.Id == jobId));
            Assert.Equal(0, await ctx.JobTasks.CountAsync(t => t.JobId == jobId));
            Assert.Equal(0, await ctx.Runs.CountAsync(r => r.JobId == jobId));
            Assert.Equal(0, await ctx.RunTasks.CountAsync(rt => rt.RunId == runId));
            Assert.Equal(0, await ctx.RunLogs.CountAsync(rl => rl.RunId == runId));
        }
    }

    [Fact]
    public async Task ActiveRun_UniquePerJob_And_DoesNotBlockPipelineRun()
    {
        long jobId;
        long pipelineId;

        using (var ctx = CreateContext())
        {
            var (gitCred, _, slave) = await SeedBaseResourcesAsync(ctx);

            var job = new Job
            {
                Name = "active-test-job",
                GitUrl = "https://git.example.com/repo.git",
                GitCredentialId = gitCred.Id,
                SlaveId = slave.Id,
                ImageName = "active-test",
                RegistryHost = "reg.local",
                Namespace = "devops",
            };
            ctx.Jobs.Add(job);

            var pipe = new Pipeline
            {
                Name = "active-test-pipe",
                GitUrl = "https://git.example.com/repo.git",
                GitCredentialId = gitCred.Id,
                SlaveId = slave.Id,
                ImageName = "active-test",
                RegistryHost = "reg.local",
                DeployNamespace = "devops",
                DeployDeployment = "active-dep",
                DeployContainer = "app",
            };
            ctx.Pipelines.Add(pipe);
            await ctx.SaveChangesAsync();

            jobId = job.Id;
            pipelineId = pipe.Id;
        }

        // 1. Create first active (Running) run for Job
        using (var ctx = CreateContext())
        {
            ctx.Runs.Add(new Run { JobId = jobId, Status = RunStatus.Running });
            await ctx.SaveChangesAsync();
        }

        // 2. Second active run (Queued) for same Job -> throws
        using (var ctx = CreateContext())
        {
            ctx.Runs.Add(new Run { JobId = jobId, Status = RunStatus.Queued });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // 3. Active run on Pipeline does not collide with active run on Job
        using (var ctx = CreateContext())
        {
            ctx.Runs.Add(new Run { PipelineId = pipelineId, Status = RunStatus.Running });
            await ctx.SaveChangesAsync();
        }

        // 4. Second active run on same Pipeline -> throws
        using (var ctx = CreateContext())
        {
            ctx.Runs.Add(new Run { PipelineId = pipelineId, Status = RunStatus.Queued });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // 5. Finished runs for same Job are allowed
        using (var ctx = CreateContext())
        {
            ctx.Runs.Add(new Run { JobId = jobId, Status = RunStatus.Success });
            ctx.Runs.Add(new Run { JobId = jobId, Status = RunStatus.Failed });
            ctx.Runs.Add(new Run { JobId = jobId, Status = RunStatus.Cancelled });
            await ctx.SaveChangesAsync();
        }
    }
}
