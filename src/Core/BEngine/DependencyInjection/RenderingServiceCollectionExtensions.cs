using BEngine.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.DependencyInjection;

public static class RenderingServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineRenderingServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEnginePlatformServices();
        services.TryAddSingleton<ISceneRendererFactory, PortableSceneRendererFactory>();
        return services;
    }
}
