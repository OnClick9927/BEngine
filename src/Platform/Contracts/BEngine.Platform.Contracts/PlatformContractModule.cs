using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Rendering.Rhi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Platform;

internal static class PlatformContractModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        Hosting.HostAssemblyRegistry.Register(typeof(PlatformContractModule).Assembly);
        RuntimeServiceContributions.Register(static services => services.TryAddSingleton<GraphicsUtility>());
        RuntimeServiceContributions.RegisterPlatformState(static (services, host) =>
            services.Replace(ServiceDescriptor.Singleton(host.GetRequiredService<GraphicsUtility>())));
    }
}

public static class PlatformContractServices
{
    public static IServiceCollection AddBEngineHostContracts(this IServiceCollection services)
    {
        services.TryAddSingleton<GraphicsUtility>();
        return services;
    }
}
