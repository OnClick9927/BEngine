using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Platform;

public static class DesktopPlatformServices
{
    private static readonly ServiceProvider Defaults = new ServiceCollection()
        .AddBEngineDesktopPlatform().BuildServiceProvider();

    public static void Initialize() => PlatformRuntime.Initialize(Defaults);

    public static IServiceCollection AddBEngineDesktopPlatform(this IServiceCollection services)
    {
        services.AddBEngineDotNetPlatform();
        return services;
    }
}
