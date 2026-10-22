using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor;

internal static class AssetBundleEditorServices
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register package services.")]
    internal static void Initialize() => RuntimeServiceContributions.Register(Configure);

    private static void Configure(IServiceCollection services)
    {
        services.TryAddSingleton<IAssetBundleBuildPipeline, AssetBundleBuildPipelineAdapter>();
        services.TryAddSingleton<EditorVirtualAssetBundleBuilder>();
        services.TryAddSingleton<IEditorVirtualContentSessionFactory, EditorVirtualContentSessionFactory>();
    }
}
