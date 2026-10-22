using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BEngine.Networking;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public static class BuildPipelineServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineBuildPipelines(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEnginePlatformServices();
        services.TryAddSingleton<IHttpNetworkRequestFactory>(HttpNetworkRequestFactory.Shared);
        services.AddBEngineEditorPlatformServices();
        services.TryAddSingleton<PlatformPlayerBuildCapabilities>();
        services.TryAddSingleton<IProjectAssetDatabaseFactory, ProjectAssetDatabaseFactory>();
        services.TryAddSingleton<IPlayerBuildPipeline, PlayerBuildPipelineAdapter>();
        services.TryAddSingleton<IAssetBundleBuildPipeline, AssetBundleBuildPipelineAdapter>();
        return services;
    }
}
