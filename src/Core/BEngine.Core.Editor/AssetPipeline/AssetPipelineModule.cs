using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor;

internal static class AssetPipelineModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        RuntimeServiceContributions.Register(ConfigureServices);
        RuntimeAssemblyCleanup.Register(PlatformAssetBuildPipeline.UnregisterAssembly);
    }
    private static void ConfigureServices(IServiceCollection services) =>
        services.TryAddSingleton<IProjectAssetDatabaseFactory, ProjectAssetDatabaseFactory>();
}
