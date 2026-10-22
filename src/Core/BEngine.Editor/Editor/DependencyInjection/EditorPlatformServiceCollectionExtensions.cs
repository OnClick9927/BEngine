using BEngine.Editor.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public static class EditorPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineEditorPlatformServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEnginePlatformServices();
        services.TryAddSingleton<IProjectAssetDatabaseFactory, ProjectAssetDatabaseFactory>();
        services.TryAddSingleton<IBPackageArchiveService, BPackageArchiveService>();
        services.TryAddSingleton<ProcessUtility>();
        services.TryAddSingleton<ProcessHostUtility>();
        services.TryAddSingleton<WaitUtility>();
        services.TryAddSingleton<ExecutionUtility>();
        services.TryAddSingleton<LogTimestampUtility>();
        services.TryAddSingleton<HostOutputUtility>();
        services.TryAddSingleton<NativeWindowUtility>();
        services.TryAddSingleton<DesktopUtility>();
        services.TryAddSingleton<ClipboardUtility>();
        services.TryAddSingleton<ShaderCompilerUtility>();
        services.TryAddSingleton<EditorTextUtility>();
        services.TryAddSingleton<CanvasResourceUtility>();
        services.TryAddSingleton<FileDialogUtility>();
        services.TryAddSingleton<SourceMonitorUtility>();
        services.TryAddSingleton<AssemblyLoadingUtility>();
        return services;
    }
}
