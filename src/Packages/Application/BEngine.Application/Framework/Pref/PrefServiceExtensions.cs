using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.App.Pref;

public static class PrefServiceExtensions
{
    /// <summary>Uses one instance per DI scope. Storage/converter overrides use normal DI registrations.</summary>
    public static IServiceCollection UsePref(this IServiceCollection services, string namespaceKey)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceKey);
        services.TryAddScoped<IPrefService>(provider => new PrefService(namespaceKey,
            provider.GetService<IPrefLoader>(), provider.GetService<IPrefConverter>()));
        return services;
    }
    public static IPrefService Pref(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetRequiredService<IPrefService>();
    }
}
