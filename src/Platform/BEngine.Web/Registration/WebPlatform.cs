using BEngine.Build;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Platform;

public static class WebPlatform
{
    public static BuildTargetPlatform Target => BuildTargetPlatform.Web;

    public static IServiceCollection AddBEngineWeb(this IServiceCollection services) =>
        services.AddBEngineDotNetPlatform();

    public static void Initialize() => DotNetPlatformServices.Initialize();
}
