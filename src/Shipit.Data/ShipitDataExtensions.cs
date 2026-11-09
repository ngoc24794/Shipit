using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Shipit.Data;

public static class ShipitDataExtensions
{
    public static IServiceCollection AddShipitData(this IServiceCollection services, string databaseUrlOrConnectionString)
    {
        var connectionString = DbUrlParser.ToNpgsqlConnectionString(databaseUrlOrConnectionString);

        services.AddDbContextFactory<ShipitDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContext());

        return services;
    }
}
