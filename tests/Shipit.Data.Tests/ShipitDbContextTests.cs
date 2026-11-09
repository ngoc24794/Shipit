using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data.Entities;
using Shipit.Security;

namespace Shipit.Data.Tests;

public class ShipitDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ShipitDbContext> _options;

    public ShipitDbContextTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

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

    [Fact]
    public async Task UniqueNames_EnforcedForSlaveCredentialAndPipeline()
    {
        // 1. Unique Credential Name
        using (var ctx = CreateContext())
        {
            var cred1 = new Credential { Name = "my-cred", Kind = CredentialKind.GitToken, ProtectedValue = "v1:dummy" };
            var cred2 = new Credential { Name = "my-cred", Kind = CredentialKind.GitToken, ProtectedValue = "v1:dummy" };
            ctx.Credentials.Add(cred1);
            await ctx.SaveChangesAsync();

            ctx.Credentials.Add(cred2);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // 2. Unique Slave Name
        long sshCredId;
        using (var ctx = CreateContext())
        {
            var sshCred = new Credential { Name = "ssh-cred", Kind = CredentialKind.SshKey, ProtectedValue = "v1:dummy" };
            ctx.Credentials.Add(sshCred);
            await ctx.SaveChangesAsync();
            sshCredId = sshCred.Id;

            var slave1 = new Slave { Name = "slave-1", Host = "192.168.1.10", User = "devops", SshCredentialId = sshCredId };
            var slave2 = new Slave { Name = "slave-1", Host = "192.168.1.11", User = "devops", SshCredentialId = sshCredId };
            ctx.Slaves.Add(slave1);
            await ctx.SaveChangesAsync();

            ctx.Slaves.Add(slave2);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // 3. Unique Pipeline Name
        using (var ctx = CreateContext())
        {
            var regCred = new Credential { Name = "reg-cred", Kind = CredentialKind.Registry, ProtectedValue = "v1:dummy" };
            ctx.Credentials.Add(regCred);
            var slave = new Slave { Name = "pipe-slave", Host = "192.168.1.20", User = "devops", SshCredentialId = sshCredId };
            ctx.Slaves.Add(slave);
            await ctx.SaveChangesAsync();

            var pipe1 = new Pipeline
            {
                Name = "demo-pipeline",
                GitUrl = "https://git.example.com/repo.git",
                GitCredentialId = sshCredId,
                SlaveId = slave.Id,
                ImageName = "demo",
                RegistryHost = "registry.local",
                RegistryCredentialId = regCred.Id,
                DeployNamespace = "devops",
                DeployDeployment = "demo",
                DeployContainer = "app"
            };
            var pipe2 = new Pipeline
            {
                Name = "demo-pipeline",
                GitUrl = "https://git.example.com/other.git",
                GitCredentialId = sshCredId,
                SlaveId = slave.Id,
                ImageName = "demo",
                RegistryHost = "registry.local",
                RegistryCredentialId = regCred.Id,
                DeployNamespace = "devops",
                DeployDeployment = "demo",
                DeployContainer = "app"
            };
            ctx.Pipelines.Add(pipe1);
            await ctx.SaveChangesAsync();

            ctx.Pipelines.Add(pipe2);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ActiveRunConstraint_AllowsOnlyOneActiveRunPerPipeline()
    {
        using var ctx = CreateContext();

        var cred = new Credential { Name = "ssh-key", Kind = CredentialKind.SshKey, ProtectedValue = "v1:abc" };
        ctx.Credentials.Add(cred);
        await ctx.SaveChangesAsync();

        var slave = new Slave { Name = "builder", Host = "10.0.0.1", User = "dev", SshCredentialId = cred.Id };
        ctx.Slaves.Add(slave);
        await ctx.SaveChangesAsync();

        var pipe1 = new Pipeline
        {
            Name = "pipe-1",
            GitUrl = "https://git.example.com/a.git",
            GitCredentialId = cred.Id,
            SlaveId = slave.Id,
            ImageName = "a",
            RegistryHost = "reg.local",
            RegistryCredentialId = cred.Id,
            DeployNamespace = "dev",
            DeployDeployment = "a",
            DeployContainer = "app"
        };
        var pipe2 = new Pipeline
        {
            Name = "pipe-2",
            GitUrl = "https://git.example.com/b.git",
            GitCredentialId = cred.Id,
            SlaveId = slave.Id,
            ImageName = "b",
            RegistryHost = "reg.local",
            RegistryCredentialId = cred.Id,
            DeployNamespace = "dev",
            DeployDeployment = "b",
            DeployContainer = "app"
        };
        ctx.Pipelines.AddRange(pipe1, pipe2);
        await ctx.SaveChangesAsync();

        // 1. Add Queued run to pipe1 -> succeeds
        var run1 = new Run { PipelineId = pipe1.Id, Status = RunStatus.Queued };
        ctx.Runs.Add(run1);
        await ctx.SaveChangesAsync();

        // 2. Add second active run (Running) to pipe1 -> should violate filtered index
        var run2 = new Run { PipelineId = pipe1.Id, Status = RunStatus.Running };
        ctx.Runs.Add(run2);
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());

        // Detach failed entity
        ctx.Entry(run2).State = EntityState.Detached;

        // 3. Add active run (Queued) to pipe2 -> succeeds (different pipeline)
        var runPipe2 = new Run { PipelineId = pipe2.Id, Status = RunStatus.Queued };
        ctx.Runs.Add(runPipe2);
        await ctx.SaveChangesAsync();

        // 4. Mark run1 as Success (finished)
        run1.Status = RunStatus.Success;
        run1.FinishedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        // 5. Now pipe1 has no active runs, adding a new Queued run should succeed
        var run3 = new Run { PipelineId = pipe1.Id, Status = RunStatus.Queued };
        ctx.Runs.Add(run3);
        await ctx.SaveChangesAsync();

        // 6. Multiple finished runs for the same pipeline should succeed
        var finishedRun1 = new Run { PipelineId = pipe1.Id, Status = RunStatus.Success };
        var finishedRun2 = new Run { PipelineId = pipe1.Id, Status = RunStatus.Failed };
        var cancelledRun = new Run { PipelineId = pipe1.Id, Status = RunStatus.Cancelled };
        ctx.Runs.AddRange(finishedRun1, finishedRun2, cancelledRun);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task DeleteCredential_InUseBySlaveOrPipeline_IsRejected()
    {
        long credId;
        using (var ctx = CreateContext())
        {
            var cred = new Credential { Name = "in-use-cred", Kind = CredentialKind.SshKey, ProtectedValue = "v1:dummy" };
            ctx.Credentials.Add(cred);
            await ctx.SaveChangesAsync();
            credId = cred.Id;

            var slave = new Slave { Name = "slave-1", Host = "host", User = "user", SshCredentialId = credId };
            ctx.Slaves.Add(slave);
            await ctx.SaveChangesAsync();
        }

        // Delete from fresh context where slave is not tracked in memory, verifying DB Restrict constraint
        using (var ctxDelete = CreateContext())
        {
            var toDelete = await ctxDelete.Credentials.FirstAsync(c => c.Id == credId);
            ctxDelete.Credentials.Remove(toDelete);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctxDelete.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ProtectedSecret_StoresCiphertextInDbAndDecodesSuccessfully()
    {
        var key = AesGcmSecretProtector.GenerateKey();
        var protector = new AesGcmSecretProtector(key);
        var rawSecret = "super-secret-ssh-private-key-pem";
        var ciphertext = protector.Protect(rawSecret);

        using (var ctx = CreateContext())
        {
            var cred = new Credential
            {
                Name = "prod-ssh",
                Kind = CredentialKind.SshKey,
                ProtectedValue = ciphertext
            };
            ctx.Credentials.Add(cred);
            await ctx.SaveChangesAsync();
        }

        // Verify ciphertext in database does NOT contain raw secret
        using (var ctx = CreateContext())
        {
            var stored = await ctx.Credentials.FirstAsync(c => c.Name == "prod-ssh");
            Assert.DoesNotContain(rawSecret, stored.ProtectedValue);
            Assert.StartsWith("v1:", stored.ProtectedValue);

            // Successfully decrypts
            var decrypted = protector.Unprotect(stored.ProtectedValue);
            Assert.Equal(rawSecret, decrypted);

            // Tamper 1 byte in DB
            var payload = Convert.FromBase64String(stored.ProtectedValue["v1:".Length..]);
            payload[^1] ^= 0xFF;
            stored.ProtectedValue = "v1:" + Convert.ToBase64String(payload);
            await ctx.SaveChangesAsync();
        }

        // Verify tampered secret in DB fails decryption
        using (var ctx = CreateContext())
        {
            var tampered = await ctx.Credentials.FirstAsync(c => c.Name == "prod-ssh");
            Assert.Throws<System.Security.Cryptography.CryptographicException>(() => protector.Unprotect(tampered.ProtectedValue));
        }
    }

    [Fact]
    public async Task CascadeDelete_DeletingRun_DeletesRunStepsAndRunLogs()
    {
        using var ctx = CreateContext();

        var cred = new Credential { Name = "c-cred", Kind = CredentialKind.SshKey, ProtectedValue = "v1:abc" };
        ctx.Credentials.Add(cred);
        await ctx.SaveChangesAsync();

        var slave = new Slave { Name = "c-slave", Host = "host", User = "user", SshCredentialId = cred.Id };
        ctx.Slaves.Add(slave);
        await ctx.SaveChangesAsync();

        var pipe = new Pipeline
        {
            Name = "c-pipe",
            GitUrl = "https://git.example.com/repo",
            GitCredentialId = cred.Id,
            SlaveId = slave.Id,
            ImageName = "img",
            RegistryHost = "reg.local",
            RegistryCredentialId = cred.Id,
            DeployNamespace = "dev",
            DeployDeployment = "dep",
            DeployContainer = "app"
        };
        ctx.Pipelines.Add(pipe);
        await ctx.SaveChangesAsync();

        var run = new Run { PipelineId = pipe.Id, Status = RunStatus.Running };
        ctx.Runs.Add(run);
        await ctx.SaveChangesAsync();

        var step = new RunStep { RunId = run.Id, Name = StepName.Build, Status = StepStatus.Running };
        var log = new RunLog { RunId = run.Id, Step = StepName.Build, Stream = LogStream.Stdout, Text = "Building..." };
        ctx.RunSteps.Add(step);
        ctx.RunLogs.Add(log);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.RunSteps.CountAsync());
        Assert.Equal(1, await ctx.RunLogs.CountAsync());

        // Delete Run
        ctx.Runs.Remove(run);
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await ctx.Runs.CountAsync());
        Assert.Equal(0, await ctx.RunSteps.CountAsync());
        Assert.Equal(0, await ctx.RunLogs.CountAsync());
    }

    [Fact]
    public async Task OptionalRegistryCredential_AllowsNull_And_PreservesCredentialWhenSet()
    {
        using var ctx = CreateContext();

        var gitCred = new Credential { Name = "git-cred", Kind = CredentialKind.GitToken, ProtectedValue = "v1:git" };
        var regCred = new Credential { Name = "reg-cred", Kind = CredentialKind.Registry, Username = "ci", ProtectedValue = "v1:reg" };
        var sshCred = new Credential { Name = "ssh-cred", Kind = CredentialKind.SshKey, ProtectedValue = "v1:ssh" };
        ctx.Credentials.AddRange(gitCred, regCred, sshCred);
        await ctx.SaveChangesAsync();

        var slave = new Slave { Name = "worker-1", Host = "worker.local", User = "devops", SshCredentialId = sshCred.Id };
        ctx.Slaves.Add(slave);
        await ctx.SaveChangesAsync();

        // Pipeline without registry credential
        var pipeNoCred = new Pipeline
        {
            Name = "pipe-no-cred",
            GitUrl = "https://git.example.com/repo.git",
            GitCredentialId = gitCred.Id,
            SlaveId = slave.Id,
            ImageName = "no-cred",
            RegistryHost = "registry.local",
            RegistryCredentialId = null,
            DeployNamespace = "devops",
            DeployDeployment = "app-no-cred",
            DeployContainer = "app"
        };
        ctx.Pipelines.Add(pipeNoCred);

        // Pipeline with registry credential (existing style)
        var pipeWithCred = new Pipeline
        {
            Name = "pipe-with-cred",
            GitUrl = "https://git.example.com/repo.git",
            GitCredentialId = gitCred.Id,
            SlaveId = slave.Id,
            ImageName = "with-cred",
            RegistryHost = "registry.local",
            RegistryCredentialId = regCred.Id,
            DeployNamespace = "devops",
            DeployDeployment = "app-with-cred",
            DeployContainer = "app"
        };
        ctx.Pipelines.Add(pipeWithCred);
        await ctx.SaveChangesAsync();

        // Verify reading both
        var loadedNoCred = await ctx.Pipelines.AsNoTracking().Include(p => p.RegistryCredential).FirstAsync(p => p.Id == pipeNoCred.Id);
        Assert.Null(loadedNoCred.RegistryCredentialId);
        Assert.Null(loadedNoCred.RegistryCredential);

        var loadedWithCred = await ctx.Pipelines.AsNoTracking().Include(p => p.RegistryCredential).FirstAsync(p => p.Id == pipeWithCred.Id);
        Assert.Equal(regCred.Id, loadedWithCred.RegistryCredentialId);
        Assert.NotNull(loadedWithCred.RegistryCredential);
        Assert.Equal("reg-cred", loadedWithCred.RegistryCredential.Name);

        // Deleting regCred from a fresh context is blocked by pipeWithCred (DB Restrict constraint)
        using (var ctxDelete = CreateContext())
        {
            var toDelete = await ctxDelete.Credentials.FirstAsync(c => c.Id == regCred.Id);
            ctxDelete.Credentials.Remove(toDelete);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctxDelete.SaveChangesAsync());
        }

        // Release regCred by setting RegistryCredentialId to null on pipeWithCred
        using (var ctxUpdate = CreateContext())
        {
            var p = await ctxUpdate.Pipelines.FirstAsync(p => p.Id == pipeWithCred.Id);
            p.RegistryCredentialId = null;
            await ctxUpdate.SaveChangesAsync();
        }

        // Now regCred can be deleted even though pipeNoCred and pipeWithCred still exist
        using (var ctxDeleteSuccess = CreateContext())
        {
            var toDelete = await ctxDeleteSuccess.Credentials.FirstAsync(c => c.Id == regCred.Id);
            ctxDeleteSuccess.Credentials.Remove(toDelete);
            await ctxDeleteSuccess.SaveChangesAsync();
            Assert.Null(await ctxDeleteSuccess.Credentials.FirstOrDefaultAsync(c => c.Id == regCred.Id));
        }
    }

    [Fact]
    public void OptionalRegistryCredential_Migration_IsRegistered_And_GeneratesScript()
    {
        using var ctx = CreateContext();
        var migrations = ctx.Database.GetMigrations().ToList();
        Assert.Contains("20261109035520_InitialCreate", migrations);
        Assert.Contains(migrations, m => m.EndsWith("OptionalRegistryCredential"));

        var factory = new ShipitDbContextFactory();
        using var npgsqlCtx = factory.CreateDbContext([]);
        var script = npgsqlCtx.Database.GenerateCreateScript();
        Assert.NotEmpty(script);
        Assert.Contains("RegistryCredentialId", script);
    }

    [Fact]
    public void AddBuildContext_Migration_IsRegistered_And_GeneratesScript()
    {
        using var ctx = CreateContext();
        var migrations = ctx.Database.GetMigrations().ToList();
        Assert.Contains("20261109035520_InitialCreate", migrations);
        Assert.Contains(migrations, m => m.EndsWith("OptionalRegistryCredential"));
        Assert.Contains(migrations, m => m.EndsWith("AddBuildContext"));
        Assert.Contains(migrations, m => m.EndsWith("AddDeployRegistryHost"));

        var factory = new ShipitDbContextFactory();
        using var npgsqlCtx = factory.CreateDbContext([]);
        var script = npgsqlCtx.Database.GenerateCreateScript();
        Assert.NotEmpty(script);
        Assert.Contains("BuildContext", script);
        Assert.Contains("DeployRegistryHost", script);
    }

    [Fact]
    public void AddDeployRegistryHost_Migration_IsRegistered_And_GeneratesScript()
    {
        using var ctx = CreateContext();
        var migrations = ctx.Database.GetMigrations().ToList();
        Assert.Contains(migrations, m => m.EndsWith("AddDeployRegistryHost"));

        var factory = new ShipitDbContextFactory();
        using var npgsqlCtx = factory.CreateDbContext([]);
        var script = npgsqlCtx.Database.GenerateCreateScript();
        Assert.NotEmpty(script);
        Assert.Contains("DeployRegistryHost", script);
    }

    [Fact]
    public async Task Pipeline_DeployRegistryHost_AllowsNullAndExplicitValues_OldDataReadsAsNull()
    {
        using var ctx = CreateContext();

        var gitCred = new Credential { Name = "git-cred-drh", Kind = CredentialKind.GitToken, ProtectedValue = "v1:git" };
        var sshCred = new Credential { Name = "ssh-cred-drh", Kind = CredentialKind.SshKey, ProtectedValue = "v1:ssh" };
        ctx.Credentials.AddRange(gitCred, sshCred);
        await ctx.SaveChangesAsync();

        var slave = new Slave { Name = "worker-drh", Host = "worker.local", User = "devops", SshCredentialId = sshCred.Id };
        ctx.Slaves.Add(slave);
        await ctx.SaveChangesAsync();

        // 1. Pipeline with null DeployRegistryHost
        var pipeNull = new Pipeline
        {
            Name = "pipe-drh-null",
            GitUrl = "https://git.example.com/repo.git",
            GitCredentialId = gitCred.Id,
            SlaveId = slave.Id,
            ImageName = "drh-null",
            RegistryHost = "registry.local",
            DeployRegistryHost = null,
            DeployNamespace = "devops",
            DeployDeployment = "app-drh-null",
            DeployContainer = "app"
        };

        // 2. Pipeline with explicit DeployRegistryHost
        var pipeExplicit = new Pipeline
        {
            Name = "pipe-drh-explicit",
            GitUrl = "https://git.example.com/repo.git",
            GitCredentialId = gitCred.Id,
            SlaveId = slave.Id,
            ImageName = "drh-explicit",
            RegistryHost = "registry.local",
            DeployRegistryHost = "10.0.0.1:5000",
            DeployNamespace = "devops",
            DeployDeployment = "app-drh-explicit",
            DeployContainer = "app"
        };

        ctx.Pipelines.AddRange(pipeNull, pipeExplicit);
        await ctx.SaveChangesAsync();

        // 3. Raw insert omitting DeployRegistryHost column (simulating legacy data)
        await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Pipelines\" (\"Name\", \"GitUrl\", \"GitBranch\", \"GitCredentialId\", \"SlaveId\", \"DockerfilePath\", \"ImageName\", \"RegistryHost\", \"DeployNamespace\", \"DeployDeployment\", \"DeployContainer\", \"CreatedAt\", \"UpdatedAt\") " +
            "VALUES ('pipe-drh-legacy', 'https://git.example.com/repo.git', 'main', {0}, {1}, 'Dockerfile', 'drh-legacy', 'registry.local', 'devops', 'app-drh-legacy', 'app', '2026-01-01', '2026-01-01')",
            gitCred.Id, slave.Id);

        // Verify values read back
        using var ctxRead = CreateContext();
        var loadedNull = await ctxRead.Pipelines.AsNoTracking().FirstAsync(p => p.Id == pipeNull.Id);
        Assert.Null(loadedNull.DeployRegistryHost);

        var loadedExplicit = await ctxRead.Pipelines.AsNoTracking().FirstAsync(p => p.Id == pipeExplicit.Id);
        Assert.Equal("10.0.0.1:5000", loadedExplicit.DeployRegistryHost);

        var loadedLegacy = await ctxRead.Pipelines.AsNoTracking().FirstAsync(p => p.Name == "pipe-drh-legacy");
        Assert.Null(loadedLegacy.DeployRegistryHost);
    }

    [Fact]
    public async Task Pipeline_BuildContext_AllowsNullAndExplicitValues_OldDataReadsAsNull()
    {
        using var ctx = CreateContext();

        var gitCred = new Credential { Name = "git-cred-bc", Kind = CredentialKind.GitToken, ProtectedValue = "v1:git" };
        var sshCred = new Credential { Name = "ssh-cred-bc", Kind = CredentialKind.SshKey, ProtectedValue = "v1:ssh" };
        ctx.Credentials.AddRange(gitCred, sshCred);
        await ctx.SaveChangesAsync();

        var slave = new Slave { Name = "worker-bc", Host = "worker.local", User = "devops", SshCredentialId = sshCred.Id };
        ctx.Slaves.Add(slave);
        await ctx.SaveChangesAsync();

        // 1. Pipeline with null BuildContext
        var pipeNull = new Pipeline
        {
            Name = "pipe-bc-null",
            GitUrl = "https://git.example.com/repo.git",
            GitCredentialId = gitCred.Id,
            SlaveId = slave.Id,
            ImageName = "bc-null",
            RegistryHost = "registry.local",
            BuildContext = null,
            DeployNamespace = "devops",
            DeployDeployment = "app-bc-null",
            DeployContainer = "app"
        };

        // 2. Pipeline with "." BuildContext
        var pipeDot = new Pipeline
        {
            Name = "pipe-bc-dot",
            GitUrl = "https://git.example.com/repo.git",
            GitCredentialId = gitCred.Id,
            SlaveId = slave.Id,
            ImageName = "bc-dot",
            RegistryHost = "registry.local",
            BuildContext = ".",
            DeployNamespace = "devops",
            DeployDeployment = "app-bc-dot",
            DeployContainer = "app"
        };

        // 3. Pipeline with subpath BuildContext
        var pipeSub = new Pipeline
        {
            Name = "pipe-bc-sub",
            GitUrl = "https://git.example.com/repo.git",
            GitCredentialId = gitCred.Id,
            SlaveId = slave.Id,
            ImageName = "bc-sub",
            RegistryHost = "registry.local",
            BuildContext = "src/app",
            DeployNamespace = "devops",
            DeployDeployment = "app-bc-sub",
            DeployContainer = "app"
        };

        ctx.Pipelines.AddRange(pipeNull, pipeDot, pipeSub);
        await ctx.SaveChangesAsync();

        // Verify values read back
        using var ctxRead = CreateContext();
        var loadedNull = await ctxRead.Pipelines.AsNoTracking().FirstAsync(p => p.Id == pipeNull.Id);
        Assert.Null(loadedNull.BuildContext);

        var loadedDot = await ctxRead.Pipelines.AsNoTracking().FirstAsync(p => p.Id == pipeDot.Id);
        Assert.Equal(".", loadedDot.BuildContext);

        var loadedSub = await ctxRead.Pipelines.AsNoTracking().FirstAsync(p => p.Id == pipeSub.Id);
        Assert.Equal("src/app", loadedSub.BuildContext);
    }

    [Fact]
    public async Task PostgresIntegration_SkippedWhenNoPgUrlConfigured()
    {
        var pgUrl = Environment.GetEnvironmentVariable("SHIPIT_TEST_PG_URL");
        if (string.IsNullOrWhiteSpace(pgUrl))
        {
            // Expected to skip when SHIPIT_TEST_PG_URL is not set
            return;
        }

        var connStr = DbUrlParser.ToNpgsqlConnectionString(pgUrl);
        var options = new DbContextOptionsBuilder<ShipitDbContext>()
            .UseNpgsql(connStr)
            .Options;

        using var ctx = new ShipitDbContext(options);
        await ctx.Database.EnsureCreatedAsync();
        Assert.True(await ctx.Database.CanConnectAsync());
    }
}
