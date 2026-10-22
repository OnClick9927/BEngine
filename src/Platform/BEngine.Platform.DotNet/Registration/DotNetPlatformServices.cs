using BEngine.DependencyInjection;
using System.Runtime.CompilerServices;
using BEngine.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Platform.DotNet;

public static class DotNetPlatformServices
{
    private static readonly ServiceProvider Defaults = new ServiceCollection()
        .AddBEngineDotNetPlatform().BuildServiceProvider();

    [ModuleInitializer]
    public static void Initialize()
    {
        PlatformRuntime.Initialize(Defaults);
        RuntimeServiceContributions.Register(ConfigureStateServices);
    }

    private static void ConfigureStateServices(IServiceCollection services)
    {
        services.TryAddSingleton<IInputService, PlatformInputService>();
        services.TryAddSingleton<IDisplayService, PlatformDisplayService>();
        services.TryAddSingleton<IRenderSettingsService, PlatformRenderSettingsService>();
        services.TryAddSingleton<BEngine.Rendering.Rhi.IGraphicsPlatformService, PlatformGraphicsService>();
        services.TryAddSingleton<ICursorService, PlatformCursorService>();
    }

    public static IServiceCollection AddBEngineDotNetPlatform(this IServiceCollection services)
    {
        ConfigureStateServices(services);
        services.TryAddSingleton<IBuildTargetCatalog, BEngine.Build.SystemBuildTargetCatalog>();
        services.TryAddSingleton<IFileSystem>(PhysicalFileSystem.Shared);
        services.TryAddSingleton<IPathService>(SystemPathService.Shared);
        services.TryAddSingleton<IPlatformService>(SystemPlatformService.Shared);
        services.TryAddSingleton<IApplicationPaths, SystemApplicationPaths>();
        services.TryAddSingleton<ISceneRendererFactory, PortableSceneRendererFactory>();
        return services.AddBEnginePlatformUtilities();
    }
}
