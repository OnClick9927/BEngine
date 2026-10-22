using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
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
        EditorHostPackages.Initialize();
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddBEnginePlatformServices();
        var bootstrap = new BootstrapServiceResolver(services);
        var fileSystem = bootstrap.GetRequiredService<FileUtility>();
        var pathService = bootstrap.GetRequiredService<PathUtility>();
        var platformService = bootstrap.GetRequiredService<PlatformUtility>();
        var projectPath = pathService.GetFullPath(options.ProjectPath);
        options = options with { ProjectPath = projectPath };
        var workspace = ProjectWorkspace.Open(projectPath, fileSystem, pathService);
        using (EditorServiceAccess.Attach(
                   bootstrap))
            BEngine.ProjectSystem.Editor.PlayerProjectTemplate.EnsureRequiredAotInvariants(workspace);
        ProjectRuntimeSettings.LoadAndApply(workspace, fileSystem, pathService);
        services.AddBEngine(new EngineServiceContext(
            EngineHostKind.Editor, projectPath, $"Editor:{pathService.GetFileName(projectPath)}"),
            [typeof(BObject).Assembly, typeof(EditorWindow).Assembly]);
        services.AddBEngineBuildPipelines();
        services.AddBEngineEditorCompilation();
        services.AddBEngineEditorPlatformServices();
        services.AddBEngineEditorPlayServices();
        services.TryAddSingleton(options);
        services.TryAddSingleton<ISceneLoader>(provider => new EditorProjectSceneLoader(
            options.ProjectPath,
            provider.GetRequiredService<FileUtility>(),
            provider.GetRequiredService<PathUtility>()));
        services.TryAddScoped(_ => workspace);
        services.TryAddScoped(serviceProvider =>
            new EditorLayoutStore(serviceProvider.GetRequiredService<ProjectWorkspace>()));
        services.TryAddScoped(serviceProvider =>
            serviceProvider.GetRequiredService<IProjectAssetDatabaseFactory>()
                .Create(serviceProvider.GetRequiredService<ProjectWorkspace>()));
        services.TryAddScoped(serviceProvider =>
            new BPackageManager(
                serviceProvider.GetRequiredService<ProjectWorkspace>(),
                catalog: null,
                loadAssemblies: true,
                serviceProvider.GetRequiredService<IPackageSourceCompiler>()));
        services.TryAddScoped<IEditorTaskScheduler>(serviceProvider => new EditorTaskScheduler(
            serviceProvider.GetRequiredService<ExecutionUtility>()));
        services.TryAddScoped(serviceProvider =>
        {
            using var constructionServices = EditorServiceAccess.Attach(serviceProvider);
            return new GpuEditorApplication(
                options,
                serviceProvider,
                serviceProvider.GetRequiredService<ProjectWorkspace>(),
                serviceProvider.GetRequiredService<EditorLayoutStore>(),
                serviceProvider.GetRequiredService<ProjectAssetDatabase>(),
                serviceProvider.GetRequiredService<BPackageManager>(),
                serviceProvider.GetRequiredService<ISceneRuntimeFactory>(),
                serviceProvider.GetRequiredService<IEditorTaskScheduler>(),
                serviceProvider.GetRequiredService<NativeWindowUtility>(),
                serviceProvider.GetRequiredService<IEditorVirtualContentSessionFactory>(),
                serviceProvider.GetRequiredService<IEditorHotUpdateSessionFactory>(),
                serviceProvider.GetRequiredService<BEngine.Rendering.ISceneRendererFactory>(),
                serviceProvider.GetRequiredService<SourceMonitorUtility>(),
                serviceProvider.GetRequiredService<BEngine.Platform.FileUtility>(),
                serviceProvider.GetRequiredService<BEngine.Platform.PathUtility>(),
                serviceProvider.GetRequiredService<BEngine.Platform.PlatformUtility>(),
                serviceProvider.GetRequiredService<BEngine.Editor.Diagnostics.FrameDebuggerService>());
        });
        return services;
    }

}
