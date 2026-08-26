using BEngine.DependencyInjection;
using BEngine.Documents;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.AssetBundles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Player;

public static class PlayerServiceCollectionExtensions
{
    public static IServiceCollection AddBEnginePlayer(
        this IServiceCollection services,
        string projectPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        var workspace = ProjectWorkspace.Open(Path.GetFullPath(projectPath));
        PlayerAssetEnvironment.Initialize(workspace);
        var projectSettings = ProjectRuntimeSettings.LoadAndApply(workspace);
        var assetBundleSettingsPath = Path.Combine(
            workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        var assetBundleSettings = File.Exists(assetBundleSettingsPath)
            ? Document.Load<AssetBundleSettingsDocument>(assetBundleSettingsPath)
            : new AssetBundleSettingsDocument();
        DocumentValidationRegistry.Validate(assetBundleSettings);
        PlayerPackageLoader.Load(workspace);
        RuntimeTypeCache.Warmup();
        services.AddBEngine(new EngineServiceContext(
            EngineHostKind.Player, workspace.RootPath, $"Player:{workspace.Project.Name}"));
        services.TryAddSingleton(workspace);
        services.TryAddSingleton(projectSettings);
        services.TryAddSingleton(assetBundleSettings);
        if (assetBundleSettings.Enabled)
        {
            services.TryAddSingleton(provider => CreateAssetBundleOptions(
                provider.GetRequiredService<ProjectWorkspace>(),
                provider.GetRequiredService<AssetBundleSettingsDocument>()));
            services.TryAddSingleton<AssetBundleManager>();
            services.TryAddSingleton<IAssetBundleManager>(provider =>
                provider.GetRequiredService<AssetBundleManager>());
            services.TryAddSingleton(provider => new AssetBundleResourceProvider(
                provider.GetRequiredService<IAssetBundleManager>()));
            services.TryAddSingleton(provider => new PlayerAssetBundleBootstrap(
                provider.GetRequiredService<IAssetBundleManager>(),
                provider.GetRequiredService<AssetBundleResourceProvider>(),
                provider.GetRequiredService<AssetBundleSettingsDocument>()));
        }
        services.TryAddSingleton<ISceneLoader>(provider => new PlayerProjectSceneLoader(
            provider.GetRequiredService<ProjectWorkspace>(),
            provider.GetService<IAssetBundleManager>()));
        services.TryAddSingleton(provider => new PlayerApplication(
            provider.GetRequiredService<ProjectWorkspace>(),
            provider.GetRequiredService<ProjectSettingsDocument>(),
            provider,
            provider.GetRequiredService<ISceneRuntimeFactory>(),
            provider.GetRequiredService<IRuntimeSceneManager>(),
            provider.GetService<PlayerAssetBundleBootstrap>()));
        return services;
    }

    private static AssetBundleRuntimeOptions CreateAssetBundleOptions(
        ProjectWorkspace workspace,
        AssetBundleSettingsDocument settings)
    {
        var builtInRoot = string.IsNullOrWhiteSpace(settings.BuiltInDirectory)
            ? null
            : Path.Combine(workspace.ResolveInside(settings.BuiltInDirectory), settings.PackageName);
        var cacheRoot = ResolvePersistentCacheDirectory(settings);
        return new AssetBundleRuntimeOptions
        {
            PackageName = settings.PackageName,
            BuiltInDirectory = builtInRoot,
            CacheDirectory = cacheRoot,
            RemoteBaseUri = string.IsNullOrWhiteSpace(settings.RemoteBaseUrl)
                ? null
                : new Uri(settings.RemoteBaseUrl, UriKind.Absolute),
            RequireHttps = settings.RequireHttps,
            MaxRetries = settings.MaxRetries
        };
    }

    private static string ResolvePersistentCacheDirectory(AssetBundleSettingsDocument settings)
    {
        var persistentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Application.persistentDataPath));
        var relative = string.IsNullOrWhiteSpace(settings.CacheDirectory)
            ? Path.Combine("AssetBundles", settings.PackageName)
            : Path.Combine(settings.CacheDirectory.Replace('/', Path.DirectorySeparatorChar), settings.PackageName);
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException("AssetBundle cache directory must be relative to persistentDataPath.");
        var cacheRoot = Path.GetFullPath(Path.Combine(persistentRoot, relative));
        if (!cacheRoot.StartsWith(persistentRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AssetBundle cache directory escapes persistentDataPath.");

        var current = persistentRoot;
        foreach (var segment in Path.GetRelativePath(persistentRoot, cacheRoot)
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    $"AssetBundle cache directory cannot traverse a reparse point: '{current}'.");
        }
        return cacheRoot;
    }
}
