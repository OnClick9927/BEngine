using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.App.Value;

public static class ValueServiceExtensions
{
    /// <summary>Registers the facade; register values and their lifetimes with the normal IServiceCollection API.</summary>
    public static IServiceCollection UseValues(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IValueService, ValueService>();
        return services;
    }

    public static IValueService Values(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetRequiredService<IValueService>();
    }

    [return: MaybeNull]
    public static T Get<T>(this IValueService service, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.Get(typeof(T), name) is { } value ? (T)value : default;
    }

    public static T GetRequired<T>(this IValueService service, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        return (T)service.GetRequired(typeof(T), name);
    }

    public static bool TryGet<T>(this IValueService service, [MaybeNullWhen(false)] out T value, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service.TryGet(typeof(T), out var result, name)) { value = (T)result; return true; }
        value = default;
        return false;
    }
}
