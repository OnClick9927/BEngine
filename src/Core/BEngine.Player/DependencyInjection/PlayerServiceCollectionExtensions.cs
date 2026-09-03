using BEngine.DependencyInjection;
using BEngine.Documents;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Player;

public static class PlayerServiceCollectionExtensions
{
    public const string AssetBundleRemoteUrlEnvironmentVariable = "BENGINE_ASSET_BUNDLE_REMOTE_URL";

    public static IServiceCollection AddBEnginePlayer(
        this IServiceCollection services,
        string projectPath,
        string? persistentDataPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var workspace = ProjectWorkspace.OpenRuntime(Path.GetFullPath(projectPath));
        var projectSettings = ProjectRuntimeSettings.LoadAndApply(workspace);
        Application.companyName = projectSettings.CompanyName;
        Application.productName = projectSettings.ProductName;
        var assetBundleSettingsPath = Path.Combine(
            workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        var assetBundleSettings = workspace.RuntimeMetadata?.AssetBundles ??
            (File.Exists(assetBundleSettingsPath)
                ? YamlUtility.Load<AssetBundleSettingsDocument>(assetBundleSettingsPath)
                : new AssetBundleSettingsDocument());
        var remoteUrlOverride = Environment.GetEnvironmentVariable(
            AssetBundleRemoteUrlEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(remoteUrlOverride))
        {
            assetBundleSettings.Enabled = true;
            assetBundleSettings.RemoteBaseUrl = remoteUrlOverride.Trim();
            assetBundleSettings.CheckForUpdatesOnStartup = true;
            assetBundleSettings.ApplyUpdatesOnStartup = true;
        }
        AssetDataValidation.ValidateAssetBundleSettings(assetBundleSettings);
        AssetBundleRuntimeOptions? assetBundleOptions = null;
        AssetBundleManager? assetBundleManager = null;
        AssetBundleResourceProvider? assetBundleProvider = null;
        PlayerAssetBundleBootstrap? assetBundleBootstrap = null;
        PlayerHotUpdateSession? hotUpdateSession = null;
        PlayerBuiltInResourceProvider? packagedResources = null;
        try
        {
            if (workspace.RuntimeMetadata?.PlayerBootstrap is { Version: >= 4 } manifest)
            {
                var playerRoot = PlayerPersistentDataPaths.ResolvePlayerRoot(
                    workspace.RootPath, manifest);
                packagedResources = new PlayerBuiltInResourceProvider(
                    manifest.ResolvePlayerResourceArchive(playerRoot));
                PlayerAssetEnvironment.Initialize(workspace, packagedResources);
                BuildTargetManifestSerializer.SetCurrent(
                    packagedResources.ReadBytes(manifest.BuildTargetResource));
            }
            else
                PlayerAssetEnvironment.Initialize(workspace);

            if (assetBundleSettings.Enabled)
            {
                assetBundleOptions = CreateAssetBundleOptions(
                    workspace, assetBundleSettings, persistentDataPath);
                assetBundleManager = new AssetBundleManager(assetBundleOptions);
                assetBundleProvider = new AssetBundleResourceProvider(assetBundleManager);
                assetBundleBootstrap = new PlayerAssetBundleBootstrap(
                    assetBundleManager, assetBundleProvider, assetBundleSettings);
                _ = assetBundleBootstrap.PrepareAsync().AsTask()
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
            hotUpdateSession = PlayerPackageLoader.Load(workspace, assetBundleManager);
        }
        catch (Exception exception)
        {
            assetBundleBootstrap?.RollbackAfterStartupFailure(exception);
            hotUpdateSession?.Dispose();
            assetBundleBootstrap?.Dispose();
            assetBundleManager?.Dispose();
            packagedResources?.Dispose();
            throw;
        }
        RuntimeTypeCache.Warmup();
        var serviceContext = new EngineServiceContext(
            EngineHostKind.Player, workspace.RootPath, $"Player:{workspace.Project.Name}");
        if (hotUpdateSession is null) services.AddBEngine(serviceContext);
        else services.AddBEngine(serviceContext, hotUpdateSession.Assemblies);
        services.TryAddSingleton(workspace);
        services.TryAddSingleton(projectSettings);
        services.TryAddSingleton(assetBundleSettings);
        if (assetBundleManager is not null && assetBundleProvider is not null &&
            assetBundleBootstrap is not null && assetBundleOptions is not null)
        {
            services.TryAddSingleton(_ => assetBundleOptions);
            services.TryAddSingleton(_ => assetBundleManager);
            services.TryAddSingleton<IAssetBundleManager>(_ => assetBundleManager);
            services.TryAddSingleton(_ => assetBundleProvider);
            services.TryAddSingleton(_ => assetBundleBootstrap);
            services.TryAddSingleton<IContentBootstrapper>(_ => assetBundleBootstrap);
        }
        else
            services.TryAddSingleton<IContentBootstrapper>(new WorkspaceContentBootstrapper());
        if (hotUpdateSession is not null) services.TryAddSingleton(_ => hotUpdateSession);
        if (packagedResources is not null)
            services.TryAddSingleton<PlayerBuiltInResourceProvider>(packagedResources);
        services.TryAddSingleton<ISceneLoader>(provider => new PlayerProjectSceneLoader(
            provider.GetRequiredService<ProjectWorkspace>(),
            provider.GetService<IAssetBundleManager>()));
        services.TryAddSingleton(provider => new PlayerApplication(
            provider.GetRequiredService<ProjectWorkspace>(),
            provider.GetRequiredService<ProjectSettingsData>(),
            provider,
            provider.GetRequiredService<ISceneRuntimeFactory>(),
            provider.GetRequiredService<IRuntimeSceneManager>(),
            provider.GetService<PlayerAssetBundleBootstrap>(),
            provider.GetService<PlayerHotUpdateSession>(),
            packagedResources: provider.GetService<PlayerBuiltInResourceProvider>()));
        return services;
    }

    internal static AssetBundleRuntimeOptions CreateAssetBundleOptions(
        ProjectWorkspace workspace,
        AssetBundleSettingsDocument settings,
        string? persistentDataPath,
        bool usePersistentRootAsCache = false)
    {
        var builtInRoot = string.IsNullOrWhiteSpace(settings.BuiltInDirectory)
            ? null
            : Path.Combine(workspace.ResolveInside(settings.BuiltInDirectory), settings.PackageName);
        var cacheRoot = ResolvePersistentCacheDirectory(
            workspace, settings, persistentDataPath, usePersistentRootAsCache);
        return new AssetBundleRuntimeOptions
        {
            PackageName = settings.PackageName,
            BuiltInDirectory = builtInRoot,
            CacheDirectory = cacheRoot,
            RemoteBaseUri = string.IsNullOrWhiteSpace(settings.RemoteBaseUrl)
                ? null
                : new Uri(settings.RemoteBaseUrl, UriKind.Absolute),
            RequireHttps = settings.RequireHttps,
            MaxRetries = settings.MaxRetries,
            HttpTransferObserver = diagnostic => PlayerStartupDiagnostics.Phase(
                "04_HTTP_TRANSFER",
                $"resource={diagnostic.ResourceKind};result={diagnostic.Result};" +
                $"status={diagnostic.StatusCode?.ToString() ?? "none"};" +
                $"bytes={diagnostic.ReceivedBytes};attempt={diagnostic.Attempt};" +
                $"endpoint={diagnostic.Endpoint}")
        };
    }

    private static string ResolvePersistentCacheDirectory(
        ProjectWorkspace workspace,
        AssetBundleSettingsDocument settings,
        string? persistentDataPath,
        bool usePersistentRootAsCache = false)
    {
        var persistentRoot = Path.TrimEndingDirectorySeparator(PlayerPersistentDataPaths.Resolve(
            persistentDataPath, workspace, Application.companyName, Application.productName));
        EnsurePhysicalCacheDirectory(persistentRoot);
        if (usePersistentRootAsCache) return persistentRoot;

        var relative = string.IsNullOrWhiteSpace(settings.CacheDirectory)
            ? Path.Combine("AssetBundles", settings.PackageName)
            : Path.Combine(settings.CacheDirectory.Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar), settings.PackageName);
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException("AssetBundle cache directory must be relative to persistentDataPath.");
        var cacheRoot = Path.GetFullPath(Path.Combine(persistentRoot, relative));
        var relativeCache = Path.GetRelativePath(persistentRoot, cacheRoot);
        if (Path.IsPathRooted(relativeCache) || relativeCache == ".." ||
            relativeCache.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidDataException("AssetBundle cache directory escapes persistentDataPath.");

        var current = persistentRoot;
        foreach (var segment in relativeCache
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            EnsurePhysicalCacheDirectory(current);
        }
        return cacheRoot;
    }

    internal static void RemoveLegacyStagedCache(
        ProjectWorkspace workspace,
        AssetBundleSettingsDocument settings,
        string? persistentDataPath,
        string flatCacheRoot)
    {
        var legacyRoot = ResolvePersistentCacheDirectory(
            workspace, settings, persistentDataPath, usePersistentRootAsCache: false);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(flatCacheRoot));
        var legacy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyRoot));
        if (legacy.Equals(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(legacy)) return;
        var relative = Path.GetRelativePath(root, legacy);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Legacy AssetBundle cache '{legacy}' is outside the Player sandbox '{root}'.");

        Directory.Delete(legacy, recursive: true);
        RemoveEmptyParents(Path.GetDirectoryName(legacy), root);
        PlayerStartupDiagnostics.Phase("03_LEGACY_CACHE_REMOVED", $"path={legacy}");
    }

    private static void RemoveEmptyParents(string? path, string stop)
    {
        while (!string.IsNullOrWhiteSpace(path) &&
               !path.Equals(stop, StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any()) return;
            Directory.Delete(path);
            path = Path.GetDirectoryName(path);
        }
    }

    private static void EnsurePhysicalCacheDirectory(string path)
    {
        if (File.Exists(path) && !Directory.Exists(path))
            throw new InvalidDataException(
                $"AssetBundle cache directory traverses a file: '{path}'.");
        var directory = new DirectoryInfo(path);
        directory.Refresh();
        if (directory.LinkTarget is not null || directory.Exists &&
            (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"AssetBundle cache directory cannot traverse a reparse point: '{path}'.");
    }
}
