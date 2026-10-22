using BEngine.DependencyInjection;
using BEngine.Editor.Diagnostics;
using BEngine.HotUpdate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor;

public static class EditorPlayServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineEditorPlayServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEnginePlatformServices();
        services.TryAddSingleton<EditorVirtualAssetBundleBuilder>();
        services.TryAddSingleton<IEditorVirtualContentSessionFactory>(serviceProvider =>
            new EditorVirtualContentSessionFactory(
                serviceProvider.GetRequiredService<EditorVirtualAssetBundleBuilder>()));
        services.TryAddSingleton<IManagedCodeRuntimeFactory,
            EditorHotUpdateSession.EditorManagedCodeRuntimeFactory>();
        services.TryAddSingleton<IEditorHotUpdateSessionFactory, EditorHotUpdateSessionFactory>();
        services.TryAddSingleton(FrameDebuggerService.Shared);
        return services;
    }
}
