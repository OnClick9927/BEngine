using BEngine.DependencyInjection;
using BEngine.Hosting;
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
        YamlSerialization.Initialize();
        var yamlAssembly = typeof(YamlSerialization).Assembly;
        if (!yamlAssembly.IsCollectible &&
            (AssemblyDomains.Available is not { } loader || loader.IsDefaultAssembly(yamlAssembly)))
        {
            HostAssemblyRegistry.Register(yamlAssembly);
            HostAssemblyRegistry.Register(typeof(YamlDotNet.Serialization.IYamlTypeConverter).Assembly);
        }
        PlatformRuntime.Initialize(Defaults);
        RuntimeServiceContributions.RegisterDefault<IAssemblyDomainService>(SystemAssemblyDomainService.Shared);
        RuntimeServiceContributions.RegisterDefault<IPlatformHttpTransport>(DotNetHttpTransport.Shared);
        RuntimeServiceContributions.RegisterDefault<IPlatformSha256Service>(SystemSha256Service.Shared);
        RuntimeServiceContributions.RegisterDefault<IPlatformVideoDecoder>(DefaultMediaDecoder);
        RuntimeServiceContributions.RegisterDefault<IPlatformExternalVideoDecoder>(FfmpegMediaDecoder.Shared);
        RuntimeServiceContributions.RegisterDefault<IPlatformSocketTransportFactory>(DotNetSocketTransportFactory.Shared);
        RuntimeServiceContributions.RegisterDefault<IPlatformTcpStreamFactory>(DotNetTcpStreamFactory.Shared);
        RuntimeServiceContributions.RegisterDefault<IPlatformNativeLibraryFactory>(SystemNativeLibraryFactory.Shared);
        RuntimeServiceContributions.RegisterDefault<Func<IPlatformWebSocketTransport>>(static () => new DotNetWebSocketTransport());
        RuntimeServiceContributions.Register(ConfigureStateServices);
    }

    private static void ConfigureStateServices(IServiceCollection services)
    {
        services.TryAddSingleton<IAssemblyDomainService>(SystemAssemblyDomainService.Shared);
        services.TryAddSingleton<IPlatformHttpTransport>(DotNetHttpTransport.Shared);
        services.TryAddSingleton<IPlatformSha256Service>(SystemSha256Service.Shared);
        services.TryAddSingleton<IPlatformVideoDecoder>(DefaultMediaDecoder);
        services.TryAddSingleton<IPlatformExternalVideoDecoder>(FfmpegMediaDecoder.Shared);
        services.TryAddSingleton<IPlatformSocketTransportFactory>(DotNetSocketTransportFactory.Shared);
        services.TryAddSingleton<IPlatformTcpStreamFactory>(DotNetTcpStreamFactory.Shared);
        services.TryAddSingleton<IPlatformNativeLibraryFactory>(SystemNativeLibraryFactory.Shared);
        services.TryAddSingleton<Func<IPlatformWebSocketTransport>>(static _ => static () => new DotNetWebSocketTransport());
        services.TryAddSingleton<IPlayerPrefsStorage, FilePlayerPrefsStorage>();
        services.TryAddSingleton<IHostOutput, SystemHostOutput>();
        services.TryAddSingleton<INativeLibraryLoader, SystemNativeLibraryLoader>();
        services.TryAddSingleton<NativeLibraryUtility>();
        services.TryAddSingleton<IInputService, PlatformInputService>();
        services.TryAddSingleton<IDisplayService, PlatformDisplayService>();
        services.TryAddSingleton<IRenderSettingsService, PlatformRenderSettingsService>();
        services.TryAddSingleton<BEngine.Rendering.Rhi.IGraphicsPlatformService, PlatformGraphicsService>();
        services.TryAddSingleton<ICursorService, PlatformCursorService>();
    }

    private static IPlatformVideoDecoder DefaultMediaDecoder => OperatingSystem.IsWindows()
        ? WindowsMediaDecoder.Shared : FfmpegMediaDecoder.Shared;

    public static IServiceCollection AddBEngineDotNetPlatform(this IServiceCollection services)
    {
        ConfigureStateServices(services);
        services.TryAddSingleton<IBuildTargetCatalog, BEngine.Build.SystemBuildTargetCatalog>();
        services.TryAddSingleton<IFileSystem>(PhysicalFileSystem.Shared);
        services.TryAddSingleton<IPathService>(SystemPathService.Shared);
        services.TryAddSingleton<IPlatformService>(SystemPlatformService.Shared);
        services.TryAddSingleton<IApplicationPaths, SystemApplicationPaths>();
        return services.AddBEnginePlatformUtilities().AddBEngineHostContracts();
    }
}
