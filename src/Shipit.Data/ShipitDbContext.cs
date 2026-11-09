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
                .OnDelete(DeleteBehavior.Cascade);

            // Partial unique index: at most one active (Queued or Running) run per pipeline
            b.HasIndex(r => r.PipelineId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Queued', 'Running')");
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
