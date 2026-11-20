using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.DependencyInjection;

public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddBEnginePlatformServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPlatformDefault<IBuildTargetCatalog>(services);
        AddPlatformDefault<IFileSystem>(services);
        AddPlatformDefault<IPathService>(services);
        AddPlatformDefault<IPlatformService>(services);
        AddPlatformDefault<IApplicationPaths>(services);
        services.AddBEnginePlatformUtilities();
        if (PlatformRuntime.GetService<IPlayerPrefsStorage>() is { } preferences)
            services.TryAddSingleton(preferences);
        RuntimeServiceContributions.Configure(services);
        return services;
    }

    public static IServiceCollection AddBEnginePlatformUtilities(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<BuildTargetUtility>();
        services.TryAddSingleton<FileUtility>();
        services.TryAddSingleton<InputUtility>();
        services.TryAddSingleton<DisplayUtility>();
        services.TryAddSingleton<RenderSettingsUtility>();
        services.TryAddSingleton<CursorUtility>();
        services.TryAddSingleton<PathUtility>();
        services.TryAddSingleton<PlatformUtility>();
        services.TryAddSingleton<ApplicationPathUtility>();
        bool __queryResult1581;
        {
            var __querySource1581 = services;
            ArgumentNullException.ThrowIfNull(__querySource1581, "source");
            __queryResult1581 = false;
            var __querySource1581Type = __querySource1581.GetType();
            if (__querySource1581Type == typeof(Microsoft.Extensions.DependencyInjection.ServiceDescriptor[]) || __querySource1581Type == typeof(System.Collections.Generic.List<Microsoft.Extensions.DependencyInjection.ServiceDescriptor>))
            {
                System.ReadOnlySpan<Microsoft.Extensions.DependencyInjection.ServiceDescriptor> __querySource1581Span = __querySource1581Type == typeof(Microsoft.Extensions.DependencyInjection.ServiceDescriptor[]) ? (Microsoft.Extensions.DependencyInjection.ServiceDescriptor[])(object)__querySource1581 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<Microsoft.Extensions.DependencyInjection.ServiceDescriptor>)(object)__querySource1581);
                foreach (var __queryItem1581 in __querySource1581Span)
                {
                    if ((__queryItem1581.ServiceType == typeof(ITextInputService)))
                    {
                        __queryResult1581 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem1581 in __querySource1581)
                {
                    if ((__queryItem1581.ServiceType == typeof(ITextInputService)))
                    {
                        __queryResult1581 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult1581)
            services.TryAddSingleton<TextInputUtility>();
        bool __queryResult1726;
        {
            var __querySource1726 = services;
            ArgumentNullException.ThrowIfNull(__querySource1726, "source");
            __queryResult1726 = false;
            var __querySource1726Type = __querySource1726.GetType();
            if (__querySource1726Type == typeof(Microsoft.Extensions.DependencyInjection.ServiceDescriptor[]) || __querySource1726Type == typeof(System.Collections.Generic.List<Microsoft.Extensions.DependencyInjection.ServiceDescriptor>))
            {
                System.ReadOnlySpan<Microsoft.Extensions.DependencyInjection.ServiceDescriptor> __querySource1726Span = __querySource1726Type == typeof(Microsoft.Extensions.DependencyInjection.ServiceDescriptor[]) ? (Microsoft.Extensions.DependencyInjection.ServiceDescriptor[])(object)__querySource1726 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<Microsoft.Extensions.DependencyInjection.ServiceDescriptor>)(object)__querySource1726);
                foreach (var __queryItem1726 in __querySource1726Span)
                {
                    if ((__queryItem1726.ServiceType == typeof(ITouchInputService)))
                    {
                        __queryResult1726 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem1726 in __querySource1726)
                {
                    if ((__queryItem1726.ServiceType == typeof(ITouchInputService)))
                    {
                        __queryResult1726 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult1726)
            services.TryAddSingleton<TouchInputUtility>();
        return services;
    }

    /// <summary>Reuse host input/display/device state across play mode or managed-code stages.</summary>
    public static IServiceCollection ShareBEnginePlatformState(this IServiceCollection services, IServiceProvider host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        services.Replace(ServiceDescriptor.Singleton(host.GetRequiredService<InputUtility>()));
        services.Replace(ServiceDescriptor.Singleton(host.GetRequiredService<CursorUtility>()));
        services.Replace(ServiceDescriptor.Singleton(host.GetRequiredService<DisplayUtility>()));
        services.Replace(ServiceDescriptor.Singleton(host.GetRequiredService<RenderSettingsUtility>()));
        RuntimeServiceContributions.SharePlatformState(services, host);
        return services;
    }

    public static IDisposable AttachBEngineRuntimeServices(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return RuntimeServices.Attach(services);
    }

    private static void AddPlatformDefault<T>(IServiceCollection services) where T : class
    {
        bool __queryResult3048;
        {
            var __querySource3048 = services;
            ArgumentNullException.ThrowIfNull(__querySource3048, "source");
            __queryResult3048 = false;
            var __querySource3048Type = __querySource3048.GetType();
            if (__querySource3048Type == typeof(Microsoft.Extensions.DependencyInjection.ServiceDescriptor[]) || __querySource3048Type == typeof(System.Collections.Generic.List<Microsoft.Extensions.DependencyInjection.ServiceDescriptor>))
            {
                System.ReadOnlySpan<Microsoft.Extensions.DependencyInjection.ServiceDescriptor> __querySource3048Span = __querySource3048Type == typeof(Microsoft.Extensions.DependencyInjection.ServiceDescriptor[]) ? (Microsoft.Extensions.DependencyInjection.ServiceDescriptor[])(object)__querySource3048 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<Microsoft.Extensions.DependencyInjection.ServiceDescriptor>)(object)__querySource3048);
                foreach (var __queryItem3048 in __querySource3048Span)
                {
                    if ((__queryItem3048.ServiceType == typeof(T)))
                    {
                        __queryResult3048 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem3048 in __querySource3048)
                {
                    if ((__queryItem3048.ServiceType == typeof(T)))
                    {
                        __queryResult3048 = true;
                        break;
                    }
                }
            }
        }
        
        if (!__queryResult3048)
            services.AddSingleton(PlatformRuntime.GetRequiredService<T>());
    }
}
