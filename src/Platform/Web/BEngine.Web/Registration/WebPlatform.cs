using BEngine.Build;
using BEngine.DependencyInjection;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Platform;

public static class WebPlatform
{
    private static readonly ITextRasterizer TextRasterizer = new WebTextRasterizer();
    private static ServiceProvider? _defaults;

    public static BuildTargetPlatform Target => BuildTargetPlatform.Web;

    public static IServiceCollection AddBEngineWeb(this IServiceCollection services)
    {
        ConfigureTextServices(services);
        return services.AddBEngineDotNetPlatform();
    }

    public static void Initialize()
    {
        DotNetPlatformServices.Initialize();
        RuntimeServiceContributions.RegisterDefault(TextRasterizer);
        if (OperatingSystem.IsBrowser())
        {
            RuntimeServiceContributions.RegisterDefault<IPlayerPrefsStorage>(WebPlayerPrefsStorage.Shared);
            RuntimeServiceContributions.RegisterDefault<IPlatformAudioOutputFactory>(WebAudioOutputFactory.Shared);
            RuntimeServiceContributions.RegisterDefault<IPlatformAudioDecoderFactory>(WebAudioDecoderFactory.Shared);
            RuntimeServiceContributions.RegisterDefault<IPlatformHttpTransport>(WebHttpTransport.Shared);
            RuntimeServiceContributions.RegisterDefault<IPlatformSha256Service>(WebSha256Service.Shared);
            _defaults ??= new ServiceCollection().AddBEngineWeb().BuildServiceProvider();
            PlatformRuntime.Initialize(_defaults);
        }
        RuntimeServiceContributions.Register(ConfigureTextServices);
    }

    private static void ConfigureTextServices(IServiceCollection services)
    {
        services.TryAddSingleton(TextRasterizer);
        if (OperatingSystem.IsBrowser())
        {
            services.TryAddSingleton<IPlayerPrefsStorage>(WebPlayerPrefsStorage.Shared);
            services.TryAddSingleton<IPlatformAudioOutputFactory>(WebAudioOutputFactory.Shared);
            services.TryAddSingleton<IPlatformAudioDecoderFactory>(WebAudioDecoderFactory.Shared);
            services.TryAddSingleton<IPlatformHttpTransport>(WebHttpTransport.Shared);
            services.TryAddSingleton<IPlatformSha256Service>(WebSha256Service.Shared);
        }
    }
}
