using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor.Build;

internal static class AssetBundlePlayerEditorServices
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register application play-mode services.")]
    internal static void Initialize() => RuntimeServiceContributions.Register(Configure);

    private static void Configure(IServiceCollection services)
    {
        services.TryAddSingleton<EditorVirtualAssetBundleBuilder>();
        services.TryAddSingleton<IEditorVirtualContentSessionFactory, EditorContentSessionFactory>();
    }
}
