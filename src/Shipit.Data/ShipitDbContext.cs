using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shipit.Core;
using Shipit.Data.Entities;

namespace Shipit.Data;

public class ShipitDbContext : DbContext
{
    public ShipitDbContext(DbContextOptions<ShipitDbContext> options)
        : base(options)
    {
    }

    public DbSet<Slave> Slaves => Set<Slave>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunStep> RunSteps => Set<RunStep>();
    public DbSet<RunLog> RunLogs => Set<RunLog>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<DeployTarget> DeployTargets => Set<DeployTarget>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobTask> JobTasks => Set<JobTask>();
    public DbSet<JobAllowEntry> JobAllowEntries => Set<JobAllowEntry>();
    public DbSet<RunTask> RunTasks => Set<RunTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Slave
        modelBuilder.Entity<Slave>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.Name).IsUnique();
            b.Property(s => s.Port).HasDefaultValue(22);

            b.HasOne(s => s.SshCredential)
                .WithMany(c => c.Slaves)
                .HasForeignKey(s => s.SshCredentialId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 2. Credential
        modelBuilder.Entity<Credential>(b =>
        {
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.Name).IsUnique();
            b.Property(c => c.Kind).HasConversion<string>();
        });

        // 3. Pipeline
        modelBuilder.Entity<Pipeline>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasIndex(p => p.Name).IsUnique();
            b.Property(p => p.GitBranch).HasDefaultValue("main");
            b.Property(p => p.DockerfilePath).HasDefaultValue("Dockerfile");

            b.HasOne(p => p.GitCredential)
                .WithMany(c => c.GitPipelines)
                .HasForeignKey(p => p.GitCredentialId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(p => p.RegistryCredential)
                .WithMany(c => c.RegistryPipelines)
                .HasForeignKey(p => p.RegistryCredentialId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(p => p.Slave)
                .WithMany(s => s.Pipelines)
                .HasForeignKey(p => p.SlaveId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. Run
        modelBuilder.Entity<Run>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Status).HasConversion<string>();

            b.HasOne(r => r.Pipeline)
                .WithMany(p => p.Runs)
                .HasForeignKey(r => r.PipelineId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(r => r.Job)
                .WithMany(j => j.Runs)
                .HasForeignKey(r => r.JobId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            // A run belongs to exactly one of a pipeline or a job.
            b.ToTable(t => t.HasCheckConstraint(
                "CK_Runs_PipelineXorJob",
                "(\"PipelineId\" IS NOT NULL AND \"JobId\" IS NULL) OR (\"PipelineId\" IS NULL AND \"JobId\" IS NOT NULL)"));

            // Partial unique indexes: at most one active (Queued or Running) run per pipeline / per job.
            // NULLs are distinct in a unique index, so job runs do not collide with pipeline runs.
            b.HasIndex(r => r.PipelineId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Queued', 'Running')");

            b.HasIndex(r => r.JobId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Queued', 'Running')")
                .HasDatabaseName("IX_Runs_JobId_Active");
        });

        // 5. RunStep
        modelBuilder.Entity<RunStep>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).HasConversion<string>();
            b.Property(s => s.Status).HasConversion<string>();

            b.HasOne(s => s.Run)
                .WithMany(r => r.Steps)
                .HasForeignKey(s => s.RunId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(s => new { s.RunId, s.Name }).IsUnique();
        });

        // 6. RunLog
        modelBuilder.Entity<RunLog>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).ValueGeneratedOnAdd();
            b.Property(l => l.Step).HasConversion<string>();
            b.Property(l => l.Stream).HasConversion<string>();

            b.HasOne(l => l.Run)
                .WithMany(r => r.Logs)
                .HasForeignKey(l => l.RunId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(l => new { l.RunId, l.Id });
        });

        // 7. DeployTarget
        modelBuilder.Entity<DeployTarget>(b =>
        {
            b.HasKey(t => t.Id);
            b.HasIndex(t => new { t.Namespace, t.Deployment }).IsUnique();
        });

        // 9. Job, JobTask, JobAllowEntry, RunTask
        modelBuilder.Entity<Job>(b =>
        {
            b.HasKey(j => j.Id);
            b.HasIndex(j => j.Name).IsUnique();
            b.Property(j => j.GitBranch).HasDefaultValue("main");
            b.Property(j => j.DockerfilePath).HasDefaultValue("Dockerfile");
            b.Property(j => j.ContainerName).HasDefaultValue("app");

            b.HasOne(j => j.GitCredential).WithMany().HasForeignKey(j => j.GitCredentialId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(j => j.RegistryCredential).WithMany().HasForeignKey(j => j.RegistryCredentialId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(j => j.Slave).WithMany().HasForeignKey(j => j.SlaveId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JobTask>(b =>
        {
            b.HasKey(t => t.Id);
            b.HasOne(t => t.Job).WithMany(j => j.Tasks).HasForeignKey(t => t.JobId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(t => new { t.JobId, t.Position }).IsUnique();
        });

        modelBuilder.Entity<JobAllowEntry>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Kind).HasConversion<string>();
            b.HasIndex(e => new { e.Namespace, e.Kind, e.Name }).IsUnique();
        });

        modelBuilder.Entity<RunTask>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Status).HasConversion<string>();
            b.HasOne(t => t.Run).WithMany(r => r.Tasks).HasForeignKey(t => t.RunId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(t => new { t.RunId, t.Position }).IsUnique();
        });

        // 8. AppSetting
        modelBuilder.Entity<AppSetting>(b =>
        {
            b.HasKey(s => s.Key);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Run>())
        {
            if (entry.State is EntityState.Modified or EntityState.Added &&
                entry.Entity.Status == RunStatus.Success &&
                entry.Entity.PipelineId != null)
            {
                var runId = entry.Entity.Id;
                var pendingRunJob = await RunSteps.FirstOrDefaultAsync(
                    s => s.RunId == runId && s.Name == StepName.RunJob && s.Status == StepStatus.Pending,
                    cancellationToken);
                if (pendingRunJob is not null)
                {
                    pendingRunJob.Status = StepStatus.Success;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        foreach (var entry in ChangeTracker.Entries<Run>())
        {
            if (entry.State is EntityState.Modified or EntityState.Added &&
                entry.Entity.Status == RunStatus.Success &&
                entry.Entity.PipelineId != null)
            {
                var runId = entry.Entity.Id;
                var pendingRunJob = RunSteps.FirstOrDefault(
                    s => s.RunId == runId && s.Name == StepName.RunJob && s.Status == StepStatus.Pending);
                if (pendingRunJob is not null)
                {
                    pendingRunJob.Status = StepStatus.Success;
                }
            }
        }

        return base.SaveChanges();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(d => d.ToUniversalTime(), d => DateTime.SpecifyKind(d, DateTimeKind.Utc))
        {
        }
    }
}
