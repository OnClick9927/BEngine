using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.DependencyInjection;

/// <summary>Composes explicitly supplied service modules without discovering engine or platform types.</summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineCore(
        this IServiceCollection services,
        EngineServiceContext context,
        IEnumerable<IEngineServiceModule>? modules = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
        services.TryAddSingleton(context);
        if (modules is not null)
            foreach (var module in modules)
            {
                ArgumentNullException.ThrowIfNull(module);
                module.ConfigureServices(services, context);
            }
        return services;
    }
}
