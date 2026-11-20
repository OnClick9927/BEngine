using BEngine.Player.HotUpdate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Player;

public static class PlayerRuntimeServiceCollectionExtensions
{
    /// <summary>Registers engine strategies over the host's neutral assembly-loading capability.</summary>
    public static IServiceCollection AddBEnginePlayerRuntimeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IManagedCodeRuntimeProvider, AotInterpreterManagedCodeRuntimeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IManagedCodeRuntimeProvider, CoreClrManagedCodeRuntimeProvider>());
        services.TryAddSingleton<IManagedCodeRuntimeFactory, PlayerManagedCodeRuntimeFactory>();
        return services;
    }
}
