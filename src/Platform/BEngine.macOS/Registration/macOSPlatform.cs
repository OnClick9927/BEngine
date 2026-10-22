using BEngine.Build;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Platform;

public static class macOSPlatform
{
    public static BuildTargetPlatform Target => BuildTargetPlatform.MacOS;

    public static IServiceCollection AddBEnginemacOS(this IServiceCollection services) =>
        services.AddBEngineDesktopPlatform();

    public static void Initialize() => DesktopPlatformServices.Initialize();
}
