using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shipit.Data.Entities;

namespace Shipit.Data.Tests;

public class DeployTargetEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ShipitDbContext> _options;
    private readonly IDbContextFactory<ShipitDbContext> _factory;

    public DeployTargetEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ShipitDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new ShipitDbContext(_options);
        ctx.Database.EnsureCreated();

        _factory = new TestDbContextFactory(_options);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Can_Add_Query_And_Delete_DeployTarget()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var target = new DeployTarget
            {
                Namespace = "devops",
                Deployment = "shipit-demo",
                CreatedAt = DateTime.UtcNow
            };
            db.DeployTargets.Add(target);
            await db.SaveChangesAsync();
            Assert.True(target.Id > 0);
        }

        await using (var db = _factory.CreateDbContext())
        {
            var target = await db.DeployTargets.FirstOrDefaultAsync(t => t.Namespace == "devops" && t.Deployment == "shipit-demo");
            Assert.NotNull(target);
            Assert.Equal("devops", target.Namespace);
            Assert.Equal("shipit-demo", target.Deployment);
            Assert.Equal(DateTimeKind.Utc, target.CreatedAt.Kind);

            db.DeployTargets.Remove(target);
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            Assert.False(await db.DeployTargets.AnyAsync());
        }
    }

    [Fact]
    public async Task Duplicate_Namespace_And_Deployment_Throws()
    {
        await using var db = _factory.CreateDbContext();
        db.DeployTargets.Add(new DeployTarget { Namespace = "prod", Deployment = "api", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.DeployTargets.Add(new DeployTarget { Namespace = "prod", Deployment = "api", CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Same_Deployment_In_Different_Namespace_Is_Allowed()
    {
        await using var db = _factory.CreateDbContext();
        db.DeployTargets.Add(new DeployTarget { Namespace = "staging", Deployment = "api", CreatedAt = DateTime.UtcNow });
        db.DeployTargets.Add(new DeployTarget { Namespace = "prod", Deployment = "api", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var count = await db.DeployTargets.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task DbDeployTargetStore_Correctly_Checks_Allowed_Targets()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.DeployTargets.Add(new DeployTarget { Namespace = "ns1", Deployment = "dep1", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var store = new DbDeployTargetStore(_factory);

        Assert.True(await store.IsAllowedAsync("ns1", "dep1"));
        Assert.False(await store.IsAllowedAsync("ns1", "other"));
        Assert.False(await store.IsAllowedAsync("other", "dep1"));
        Assert.False(await store.IsAllowedAsync("", "dep1"));
        Assert.False(await store.IsAllowedAsync("ns1", ""));
    }

    private sealed class TestDbContextFactory(DbContextOptions<ShipitDbContext> options) : IDbContextFactory<ShipitDbContext>
    {
        public ShipitDbContext CreateDbContext() => new(options);
    }
}
