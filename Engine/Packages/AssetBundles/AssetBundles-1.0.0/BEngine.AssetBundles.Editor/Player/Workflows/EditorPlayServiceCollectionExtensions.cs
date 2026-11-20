using BEngine.DependencyInjection;
using BEngine.Player.HotUpdate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor.Build;

public static class EditorPlayServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineEditorPlayServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEnginePlatformServices();
        services.TryAddSingleton<EditorVirtualAssetBundleBuilder>();
        services.TryAddSingleton<IEditorVirtualContentSessionFactory>(serviceProvider =>
            new EditorContentSessionFactory(
                serviceProvider.GetRequiredService<EditorVirtualAssetBundleBuilder>(),
                serviceProvider.GetRequiredService<BEngine.Platform.FileUtility>(),
                serviceProvider.GetRequiredService<BEngine.Platform.PathUtility>()));
        services.TryAddSingleton<IManagedCodeRuntimeFactory,
            EditorHotUpdateSession.EditorManagedCodeRuntimeFactory>();
        services.TryAddSingleton<IEditorHotUpdateSessionFactory, EditorHotUpdateSessionFactory>();
        return services;
    }
}
