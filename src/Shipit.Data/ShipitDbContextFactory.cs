using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shipit.Data;

public class ShipitDbContextFactory : IDesignTimeDbContextFactory<ShipitDbContext>
{
    public ShipitDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ShipitDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=shipit_design;Username=postgres;Password=postgres");
        return new ShipitDbContext(optionsBuilder.Options);
    }
}
