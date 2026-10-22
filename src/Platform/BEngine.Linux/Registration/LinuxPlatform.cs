using BEngine.Build;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Platform;

public static class LinuxPlatform
{
    public static BuildTargetPlatform Target => BuildTargetPlatform.Linux;

    public static IServiceCollection AddBEngineLinux(this IServiceCollection services) =>
        services.AddBEngineDesktopPlatform();

    public static void Initialize() => DesktopPlatformServices.Initialize();
}
