using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shipit.Engine;

public static class EngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the run engine. The host must also register: <c>IDbContextFactory&lt;ShipitDbContext&gt;</c>,
    /// <c>ISecretProtector</c>, <c>ISshExecutor</c> and <c>IK8sDeployer</c>.
    /// </summary>
    public static IServiceCollection AddShipitEngine(this IServiceCollection services, Action<EngineOptions>? configure = null)
    {
        var options = new EngineOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PipelineRunner>();
        services.AddSingleton<RunCoordinator>();
        services.AddSingleton<IRunCoordinator>(sp => sp.GetRequiredService<RunCoordinator>());
        services.AddHostedService(sp => sp.GetRequiredService<RunCoordinator>());
        return services;
    }
}
