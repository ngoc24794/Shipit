using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;
using Shipit.K8s;
using Shipit.Security;
using Shipit.Ssh;

namespace Shipit.Engine.Tests;

/// <summary>SQLite-file backed environment with one fully configured pipeline and known fake secrets.</summary>
public sealed class EngineTestEnv : IDisposable
{
    public const string GitToken = "ghp_TESTTOKEN_abcdef123456";
    public const string RegistryPassword = "reg-pass-XYZ-98765";
    public const string SshKeyLine1 = "AAAAFAKEKEYMATERIALLINE1abcdef";
    public const string SshKeyLine2 = "AAAAFAKEKEYMATERIALLINE2ghijkl";
    public static readonly string SshKey =
        $"-----BEGIN OPENSSH PRIVATE KEY-----\n{SshKeyLine1}\n{SshKeyLine2}\n-----END OPENSSH PRIVATE KEY-----";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shipit-engine-{Guid.NewGuid():N}.db");

    public EngineTestEnv()
    {
        Protector = new AesGcmSecretProtector(AesGcmSecretProtector.GenerateKey());
        Factory = new FileDbFactory(new DbContextOptionsBuilder<ShipitDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

        using var db = Factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        var ssh = new Credential { Name = "slave-key", Kind = CredentialKind.SshKey, ProtectedValue = Protector.Protect(SshKey) };
        var git = new Credential { Name = "git", Kind = CredentialKind.GitToken, ProtectedValue = Protector.Protect(GitToken) };
        var reg = new Credential { Name = "registry", Kind = CredentialKind.Registry, Username = "ci", ProtectedValue = Protector.Protect(RegistryPassword) };
        var slave = new Slave { Name = "build-1", Host = "build.example.com", Port = 22, User = "devops", SshCredential = ssh };
        var pipeline = new Pipeline
        {
            Name = "demo",
            GitUrl = "https://git.example.com/team/demo.git",
            GitBranch = "main",
            GitCredential = git,
            Slave = slave,
            DockerfilePath = "Dockerfile",
            ImageName = "shipit-demo",
            RegistryHost = "registry.example.com",
            RegistryCredential = reg,
            DeployNamespace = "devops",
            DeployDeployment = "shipit-demo",
            DeployContainer = "app",
        };
        db.Pipelines.Add(pipeline);
        db.SaveChanges();
        PipelineId = pipeline.Id;
    }

    public AesGcmSecretProtector Protector { get; }

    public IDbContextFactory<ShipitDbContext> Factory { get; }

    public long PipelineId { get; }

    public EngineOptions Options { get; } = new()
    {
        LogFlushInterval = TimeSpan.FromMilliseconds(5),
        CleanupTimeout = TimeSpan.FromSeconds(10),
        RolloutTimeout = TimeSpan.FromSeconds(10),
    };

    public static DeployAllowList AllowDemo() =>
        new([new DeployAllowListEntry("devops", ["shipit-demo"])]);

    public PipelineRunner Runner(ISshExecutor ssh, IK8sDeployer k8s) =>
        new(Factory, ssh, k8s, Protector, Options, TimeProvider.System, NullLogger<PipelineRunner>.Instance);

    public RunCoordinator Coordinator(PipelineRunner runner) =>
        new(Factory, runner, Options, TimeProvider.System, NullLogger<RunCoordinator>.Instance);

    /// <summary>Adds a queued run with four pending steps and returns its id.</summary>
    public async Task<long> QueueRunAsync(RunStatus status = RunStatus.Queued, StepStatus stepStatus = StepStatus.Pending)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var run = new Run
        {
            PipelineId = PipelineId,
            Status = status,
            Steps = Enum.GetValues<StepName>().Select(n => new RunStep { Name = n, Status = stepStatus }).ToList(),
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    public async Task ClearRegistryCredentialAsync(long? pipelineId = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var id = pipelineId ?? PipelineId;
        var p = await db.Pipelines.FirstAsync(p => p.Id == id);
        p.RegistryCredentialId = null;
        await db.SaveChangesAsync();
    }

    public async Task SetBuildContextAsync(string? buildContext, string? dockerfilePath = null, long? pipelineId = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var id = pipelineId ?? PipelineId;
        var p = await db.Pipelines.FirstAsync(p => p.Id == id);
        p.BuildContext = buildContext;
        if (dockerfilePath is not null)
        {
            p.DockerfilePath = dockerfilePath;
        }
        await db.SaveChangesAsync();
    }

    public async Task SetDeployRegistryHostAsync(string? deployRegistryHost, long? pipelineId = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var id = pipelineId ?? PipelineId;
        var p = await db.Pipelines.FirstAsync(p => p.Id == id);
        p.DeployRegistryHost = deployRegistryHost;
        await db.SaveChangesAsync();
    }

    public async Task<Run> GetRunAsync(long id)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Runs.AsNoTracking().Include(r => r.Steps).FirstAsync(r => r.Id == id);
    }

    public async Task<List<RunLog>> GetLogsAsync(long runId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.RunLogs.AsNoTracking().Where(l => l.RunId == runId).OrderBy(l => l.Id).ToListAsync();
    }

    public static StepStatus StepStatusOf(Run run, StepName name) => run.Steps.Single(s => s.Name == name).Status;

    /// <summary>
    /// A fake slave behaving like a healthy one. Rules in <paramref name="overrides"/> are added first, so they win.
    /// </summary>
    public static FakeSshExecutor Slave(Action<FakeSshExecutor>? overrides = null)
    {
        var fake = new FakeSshExecutor();
        overrides?.Invoke(fake);
        fake.AddRule("git clone", 0, ["Cloning into repository..."]);
        fake.AddRule("rev-parse --short=7 HEAD", 0, ["abc1234"]);
        fake.AddRule("docker login", 0, ["Login Succeeded"]);
        fake.AddRule("docker build", 0, ["Step 1/3 : FROM base", "Successfully built abc1234"]);
        fake.AddRule("docker push", 0, ["The push refers to repository", "abc1234: digest: sha256:1234 size: 528"]);
        fake.AddRule("docker logout", 0, ["Removing login credentials"]);
        fake.AddRule("rm -rf", 0);
        return fake;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Temp file; best effort.
            }
        }
    }

    private sealed class FileDbFactory(DbContextOptions<ShipitDbContext> options) : IDbContextFactory<ShipitDbContext>
    {
        public ShipitDbContext CreateDbContext() => new(options);
    }
}
