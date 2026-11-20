using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.Editor.Build;
internal static class AssetBundlePlayerBuildRegistration
{
    [ModuleInitializer]
    internal static void Register() => RuntimeServiceContributions.Register(static services =>
    {
        services.TryAddSingleton<IAssetBundleBuildPipeline, AssetBundleBuildPipelineAdapter>();
        services.TryAddSingleton<IPlayerBuildContentProvider, AssetBundlePlayerBuildContentProvider>();
        services.AddBEngineBuildPipelines();
        services.TryAddSingleton<IPlayerBuildEncryptionKeyPrompt>(new AssetBundleBuildEncryptionKeyPrompt());
        services.AddBEngineEditorPlayServices();
    });
}
