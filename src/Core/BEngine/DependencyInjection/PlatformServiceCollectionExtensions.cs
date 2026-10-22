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
        services.TryAddSingleton<BEngine.Rendering.Rhi.GraphicsUtility>();
        services.TryAddSingleton<CursorUtility>();
        services.TryAddSingleton<PathUtility>();
        services.TryAddSingleton<PlatformUtility>();
        services.TryAddSingleton<ApplicationPathUtility>();
        if (services.Any(service => service.ServiceType == typeof(ITextInputService)))
            services.TryAddSingleton<TextInputUtility>();
        if (services.Any(service => service.ServiceType == typeof(ITouchInputService)))
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
        services.Replace(ServiceDescriptor.Singleton(host.GetRequiredService<BEngine.Rendering.Rhi.GraphicsUtility>()));
        return services;
    }

    public static IDisposable AttachBEngineRuntimeServices(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return RuntimeServiceAccess.Attach(services);
    }

    private static void AddPlatformDefault<T>(IServiceCollection services) where T : class
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(T)))
            services.AddSingleton(PlatformRuntime.GetRequiredService<T>());
    }
}
