using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shipit.Core;
using Shipit.Data;
using Shipit.Security;
using Shipit.Ssh;

namespace Shipit.Engine;

public static class EngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the run engine. The host must also register: <c>IDbContextFactory&lt;ShipitDbContext&gt;</c>,
    /// <c>ISecretProtector</c>, <c>ISshExecutor</c>, <c>IK8sDeployer</c>, and optionally <c>IK8sJobRunner</c> and <c>IJobAllowList</c> or <c>IJobAllowStore</c>.
    /// </summary>
    public static IServiceCollection AddShipitEngine(this IServiceCollection services, Action<EngineOptions>? configure = null)
    {
        var options = new EngineOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PipelineRunner>(sp => new PipelineRunner(
            sp.GetRequiredService<IDbContextFactory<ShipitDbContext>>(),
            sp.GetRequiredService<ISshExecutor>(),
            sp.GetRequiredService<IK8sDeployer>(),
            sp.GetRequiredService<ISecretProtector>(),
            sp.GetRequiredService<EngineOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<PipelineRunner>>(),
            sp.GetService<IK8sJobRunner>(),
            sp.GetService<IJobAllowList>() ?? (sp.GetService<IJobAllowStore>() is { } store ? new JobAllowStoreAdapter(store) : null)));
        services.AddSingleton<RunCoordinator>(sp => new RunCoordinator(
            sp.GetRequiredService<IDbContextFactory<ShipitDbContext>>(),
            sp.GetRequiredService<PipelineRunner>(),
            sp.GetRequiredService<EngineOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RunCoordinator>>(),
            sp.GetService<IK8sJobRunner>()));
        services.AddSingleton<IRunCoordinator>(sp => sp.GetRequiredService<RunCoordinator>());
        services.AddHostedService(sp => sp.GetRequiredService<RunCoordinator>());
        return services;
    }
}
