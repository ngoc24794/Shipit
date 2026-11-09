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
        ctx.Runs.AddRange(finishedRun1, finishedRun2);
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
