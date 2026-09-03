using BEngine.DependencyInjection;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

public static class EditorServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineEditor(
        this IServiceCollection services,
        EditorLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var projectPath = Path.GetFullPath(options.ProjectPath);
        options = options with { ProjectPath = projectPath };
        var workspace = ProjectWorkspace.Open(projectPath);
        BEngine.ProjectSystem.Editor.ProjectWorkspaceFactory.EnsureRequiredAotInvariants(workspace);
        ProjectRuntimeSettings.LoadAndApply(workspace);
        services.AddBEngine(new EngineServiceContext(
            EngineHostKind.Editor, projectPath, $"Editor:{Path.GetFileName(projectPath)}"),
            [typeof(BObject).Assembly, typeof(EditorWindow).Assembly]);
        services.TryAddSingleton(options);
        services.TryAddSingleton<ISceneLoader>(_ => new EditorProjectSceneLoader(options.ProjectPath));
        services.TryAddScoped(_ => workspace);
        services.TryAddScoped(serviceProvider =>
            new EditorLayoutStore(serviceProvider.GetRequiredService<ProjectWorkspace>()));
        services.TryAddScoped(serviceProvider =>
            new ProjectAssetDatabase(serviceProvider.GetRequiredService<ProjectWorkspace>()));
        services.TryAddScoped(serviceProvider =>
            new BPackageManager(serviceProvider.GetRequiredService<ProjectWorkspace>()));
        services.TryAddScoped<IEditorTaskScheduler>(_ => new EditorTaskScheduler());
        services.TryAddScoped(serviceProvider => new GpuEditorApplication(
            options,
            serviceProvider,
            serviceProvider.GetRequiredService<ProjectWorkspace>(),
            serviceProvider.GetRequiredService<EditorLayoutStore>(),
            serviceProvider.GetRequiredService<ProjectAssetDatabase>(),
            serviceProvider.GetRequiredService<BPackageManager>(),
            serviceProvider.GetRequiredService<ISceneRuntimeFactory>(),
            serviceProvider.GetRequiredService<IEditorTaskScheduler>()));
        return services;
    }
}
