using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Editor.Rendering;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor;

internal static class EditorServicesModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        RuntimeServiceContributions.Register(static services =>
        {
            services.TryAddSingleton<IGpuCanvasResourceResolver>(FileGpuCanvasResourceResolver.Shared);
            services.TryAddSingleton<CanvasResourceUtility>();
        });
        EditorToolingLifecycle.PackageMutationRequested += PreparePackageMutation;
        EditorToolingLifecycle.ScriptCompilationRequested += RequestScriptCompilation;
        ToolingProgress.ReportRequested += EditorUtility.DisplayProgressBar;
        ToolingProgress.ClearRequested += EditorUtility.ClearProgressBar;
        EditorExtensionLifecycle.Invalidated += EditorAssetAdapters.Invalidate;
        TypeCache.RegisterWarmup("AssetPostprocessors", static () => _ = TypeCache.GetTypesDerivedFrom<AssetPostprocessor>());
        TypeCache.RegisterWarmup("AssetModificationProcessors", static () => _ = TypeCache.GetTypesDerivedFrom<AssetModificationProcessor>());
        TypeCache.RegisterWarmup("SerializedMemberMetadata", SerializedMemberMetadata.Warmup);
        TypeCache.RegisterWarmup("AssetProcessorRegistry", AssetProcessorRegistry.Warmup);
        TypeCache.RegisterWarmup("CreateAssetMenuRegistry", CreateAssetMenuRegistry.Warmup);
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            EditorIconRegistry.UnregisterAssembly(assembly);
            EditorBuiltinIcons.UnregisterAssembly(assembly);
            EditorPreviewRegistry.UnregisterAssembly(assembly);
            AssetTypeRegistry.UnregisterAssembly(assembly);
            EditorAssetAdapters.UnregisterAssembly(assembly);
            EditorAssetPreviewSources.UnregisterAssembly(assembly);
            if (assembly != typeof(EditorServicesModule).Assembly) return;
            EditorExtensionLifecycle.Invalidated -= EditorAssetAdapters.Invalidate;
            EditorToolingLifecycle.PackageMutationRequested -= PreparePackageMutation;
            EditorToolingLifecycle.ScriptCompilationRequested -= RequestScriptCompilation;
            ToolingProgress.ReportRequested -= EditorUtility.DisplayProgressBar;
            ToolingProgress.ClearRequested -= EditorUtility.ClearProgressBar;
        });
    }

    private static void PreparePackageMutation()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Packages cannot change during a Play Mode transition.");
    }

    private static void RequestScriptCompilation()
    {
        if (EditorApplication.isAssemblyReloadLocked) EditorApplication.RequestReloadWhenUnlocked();
        else EditorBridge.Host?.RequestScriptCompilation();
    }
}
