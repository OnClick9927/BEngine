using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor.Build;

public static class BuildPipelineServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineBuildPipelines(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEnginePlatformServices();
        services.AddBEngineEditorPlatformServices();
        services.TryAddSingleton<PlatformPlayerBuildCapabilities>();
        services.TryAddSingleton<IPlayerBuildPipeline, PlayerBuildPipelineAdapter>();
        return services;
    }
}
