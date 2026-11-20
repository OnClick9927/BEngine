using BEngine.DependencyInjection;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Player;

public static class PlayerPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddBEnginePlayerPlatformServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEngineRenderingServices();
        services.AddBEnginePlatformServices();
        services.TryAddSingleton<IPlayerStartupResolver, SystemPlayerStartupResolver>();
        return services;
    }
}
