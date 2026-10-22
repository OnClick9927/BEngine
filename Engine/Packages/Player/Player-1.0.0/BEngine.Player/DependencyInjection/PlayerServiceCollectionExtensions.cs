using BEngine.DependencyInjection;
using BEngine.Serialization;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.Networking;
using BEngine.Platform;
using BEngine.HotUpdate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Player;

public static class PlayerServiceCollectionExtensions
{
    public const string AssetBundleRemoteUrlEnvironmentVariable = "BENGINE_ASSET_BUNDLE_REMOTE_URL";
    public const string AssetBundleAllowInsecureLoopbackEnvironmentVariable =
        "BENGINE_ASSET_BUNDLE_ALLOW_INSECURE_LOOPBACK";

    public static IServiceCollection AddBEnginePlayerManagedCodeServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IManagedCodeRuntimeProvider, AotInterpreterManagedCodeRuntimeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IManagedCodeRuntimeProvider, CoreClrManagedCodeRuntimeProvider>());
        services.TryAddSingleton<IManagedCodeRuntimeFactory, PlayerManagedCodeRuntimeFactory>();
        return services;
    }

    public static IServiceCollection AddBEnginePlayer(
        this IServiceCollection services,
        string projectPath,
        string? persistentDataPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEnginePlatformServices();
        services.AddBEnginePlayerPlatformServices();
        services.AddBEnginePlayerManagedCodeServices();
        persistentDataPath = PlayerPersistentApplicationPaths.Configure(services, persistentDataPath);
        var platformServices = new BootstrapServiceResolver(services);
        var fileSystem = platformServices.GetRequiredService<FileUtility>();
        var pathService = platformServices.GetRequiredService<PathUtility>();
        var platformService = platformServices.GetRequiredService<PlatformUtility>();
        var applicationPaths = platformServices.GetRequiredService<ApplicationPathUtility>();
        var httpRequestFactory = GetBootstrapSingleton<IHttpNetworkRequestFactory>(services);
        var assetBundleManagerFactory = GetAssetBundleManagerFactory(
            services, fileSystem, pathService, httpRequestFactory);
        var workspace = PlayerWorkspace.OpenRuntime(
            pathService.GetFullPath(projectPath), fileSystem, pathService);
        var projectSettings = ProjectRuntimeSettings.LoadAndApply(workspace, fileSystem, pathService);
        Application.companyName = projectSettings.CompanyName;
        Application.productName = projectSettings.ProductName;
        var assetBundleSettingsPath = pathService.Combine(
            workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        var assetBundleSettings = workspace.GetRuntimeMetadata()?.AssetBundles ??
            (fileSystem.FileExists(assetBundleSettingsPath)
                ? YamlUtility.Load<AssetBundleSettingsDocument>(
                    assetBundleSettingsPath, fileSystem, pathService)
                : new AssetBundleSettingsDocument());
        ApplyAssetBundleRemoteOverride(assetBundleSettings, platformService);
        BEngine.AssetBundles.AssetBundleSettingsValidation.Validate(assetBundleSettings);
        AssetBundleRuntimeOptions? assetBundleOptions = null;
        ITransactionalAssetBundleManager? assetBundleManager = null;
        AssetBundleResourceProvider? assetBundleProvider = null;
        PlayerAssetBundleBootstrap? assetBundleBootstrap = null;
        PlayerHotUpdateSession? hotUpdateSession = null;
        ServiceProvider? managedCodeServices = null;
        PlayerBuiltInResourceProvider? packagedResources = null;
        try
        {
            if (workspace.GetRuntimeMetadata()?.PlayerBootstrap is { Version: >= 4 } manifest)
            {
                var playerRoot = PlayerPersistentDataPaths.ResolvePlayerRoot(
                    workspace.RootPath, manifest);
                packagedResources = new PlayerBuiltInResourceProvider(
                    manifest.ResolvePlayerResourceArchive(playerRoot), fileSystem, pathService);
                PlayerAssetEnvironment.Initialize(
                    workspace, packagedResources, fileSystem, pathService,
                    platformService, applicationPaths);
                BuildTargetManifestSerializer.SetCurrent(
                    packagedResources.ReadBytes(manifest.BuildTargetResource));
            }
            else
                PlayerAssetEnvironment.Initialize(
                    workspace, fileSystem: fileSystem, pathService: pathService,
                    platformService: platformService, applicationPaths: applicationPaths);

            if (assetBundleSettings.Enabled)
            {
                assetBundleOptions = CreateAssetBundleOptions(
                    workspace, assetBundleSettings, persistentDataPath,
                    fileSystem: fileSystem,
                    pathService: pathService,
                    platformService: platformService,
                    applicationPaths: applicationPaths);
                assetBundleManager = assetBundleManagerFactory.Create(assetBundleOptions);
                assetBundleProvider = new AssetBundleResourceProvider(assetBundleManager);
                assetBundleBootstrap = new PlayerAssetBundleBootstrap(
                    assetBundleManager, assetBundleProvider, assetBundleSettings);
                PlayerMainThread.Complete(assetBundleBootstrap.PrepareAsync());
            }
            managedCodeServices = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            hotUpdateSession = PlayerMainThread.Complete(
                PlayerPackageLoader.LoadAsync(
                    workspace,
                    managedCodeServices.GetRequiredService<IManagedCodeRuntimeFactory>(),
                    assetBundleManager,
                    fileSystem: fileSystem,
                    pathService: pathService,
                    platformService: platformService,
                    applicationPaths: applicationPaths));
            hotUpdateSession?.OwnRuntimeFactoryServices(managedCodeServices);
            if (hotUpdateSession is not null) managedCodeServices = null;
            else
            {
                managedCodeServices.Dispose();
                managedCodeServices = null;
            }
        }
        catch (Exception exception)
        {
            try
            {
                if (assetBundleBootstrap is not null)
                    PlayerMainThread.Complete(
                        assetBundleBootstrap.RollbackAfterStartupFailureAsync(exception));
            }
            finally
            {
                hotUpdateSession?.Dispose();
                assetBundleBootstrap?.Dispose();
                assetBundleManager?.Dispose();
                packagedResources?.Dispose();
                managedCodeServices?.Dispose();
            }
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
            services.TryAddSingleton<ITransactionalAssetBundleManager>(_ => assetBundleManager);
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
            provider.GetService<IAssetBundleManager>(),
            fileSystem: provider.GetRequiredService<FileUtility>(),
            pathService: provider.GetRequiredService<PathUtility>()));
        services.TryAddSingleton(provider => new PlayerApplication(
            provider.GetRequiredService<ProjectWorkspace>(),
            provider.GetRequiredService<ProjectSettingsData>(),
            provider,
            provider.GetRequiredService<ISceneRuntimeFactory>(),
            provider.GetRequiredService<IRuntimeSceneManager>(),
            provider.GetRequiredService<IPlayerWindowFactory>(),
            provider.GetRequiredService<IPlayerRenderContextFactory>(),
            provider.GetRequiredService<IPlayerInputContextFactory>(),
            provider.GetRequiredService<PlatformUtility>(),
            provider.GetService<PlayerAssetBundleBootstrap>(),
            provider.GetService<PlayerHotUpdateSession>(),
            packagedResources: provider.GetService<PlayerBuiltInResourceProvider>()));
        return services;
    }

    internal static void ApplyAssetBundleRemoteOverride(
        AssetBundleSettingsDocument settings,
        PlatformUtility platformService)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(platformService);
        var remote = platformService.GetEnvironmentVariable(AssetBundleRemoteUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(remote)) return;
        settings.Enabled = true;
        settings.RemoteBaseUrl = remote.Trim();
        settings.CheckForUpdatesOnStartup = true;
        settings.ApplyUpdatesOnStartup = true;

        var allowLoopback = platformService.GetEnvironmentVariable(
            AssetBundleAllowInsecureLoopbackEnvironmentVariable);
        if (allowLoopback != "1" ||
            !Uri.TryCreate(settings.RemoteBaseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttp ||
            !endpoint.IsLoopback)
            return;
        settings.RequireHttps = false;
    }

    internal static AssetBundleRuntimeOptions CreateAssetBundleOptions(
        ProjectWorkspace workspace,
        AssetBundleSettingsDocument settings,
        string? persistentDataPath,
        bool usePersistentRootAsCache = false,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null,
        ApplicationPathUtility? applicationPaths = null)
    {
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        platformService ??= RuntimeServiceAccess.PlatformService;
        applicationPaths ??= RuntimeServiceAccess.ApplicationPaths;
        var builtInRoot = string.IsNullOrWhiteSpace(settings.BuiltInDirectory)
            ? null
            : pathService.Combine(
                workspace.ResolveInside(settings.BuiltInDirectory), settings.PackageName);
        var cacheRoot = ResolvePersistentCacheDirectory(
            workspace, settings, persistentDataPath, usePersistentRootAsCache,
            fileSystem, pathService, platformService, applicationPaths);
        return new AssetBundleRuntimeOptions
        {
            EncryptionKey = workspace.GetContentEncryptionKey(),
            RequireEncryptedContent = workspace.GetContentEncryptionKey() is not null,
            PackageName = settings.PackageName,
            BuiltInDirectory = builtInRoot,
            CacheDirectory = cacheRoot,
            RemoteBaseUri = string.IsNullOrWhiteSpace(settings.RemoteBaseUrl)
                ? null
                : new Uri(settings.RemoteBaseUrl, UriKind.Absolute),
            RequireHttps = settings.RequireHttps,
            MaxRetries = settings.MaxRetries,
            RequiredBundleNames = settings.RequiredBundleNames?.ToArray(),
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
        bool usePersistentRootAsCache,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths)
    {
        var persistentRoot = pathService.TrimEndingDirectorySeparator(PlayerPersistentDataPaths.Resolve(
            persistentDataPath, workspace, Application.companyName, Application.productName,
            fileSystem, pathService, platformService, applicationPaths));
        EnsurePhysicalCacheDirectory(persistentRoot, fileSystem);
        PlayerPersistentDataPaths.EnsurePhysicalDirectoryTree(
            persistentRoot, "Player sandbox cache", fileSystem, pathService);
        if (usePersistentRootAsCache) return persistentRoot;

        var relative = string.IsNullOrWhiteSpace(settings.CacheDirectory)
            ? pathService.Combine("AssetBundles", settings.PackageName)
            : pathService.Combine(settings.CacheDirectory.Replace('\\', pathService.DirectorySeparatorChar)
                .Replace('/', pathService.DirectorySeparatorChar), settings.PackageName);
        if (pathService.IsPathRooted(relative))
            throw new InvalidDataException("AssetBundle cache directory must be relative to persistentDataPath.");
        var cacheRoot = pathService.GetFullPath(pathService.Combine(persistentRoot, relative));
        var relativeCache = pathService.GetRelativePath(persistentRoot, cacheRoot);
        if (pathService.IsPathRooted(relativeCache) || relativeCache == ".." ||
            relativeCache.StartsWith($"..{pathService.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidDataException("AssetBundle cache directory escapes persistentDataPath.");

        var current = persistentRoot;
        foreach (var segment in relativeCache
                     .Split(pathService.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = pathService.Combine(current, segment);
            EnsurePhysicalCacheDirectory(current, fileSystem);
        }
        PlayerPersistentDataPaths.EnsurePhysicalDirectoryTree(
            cacheRoot, "AssetBundle cache", fileSystem, pathService);
        return cacheRoot;
    }

    internal static void RemoveLegacyStagedCache(
        ProjectWorkspace workspace,
        AssetBundleSettingsDocument settings,
        string? persistentDataPath,
        string flatCacheRoot,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null,
        ApplicationPathUtility? applicationPaths = null)
    {
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        platformService ??= RuntimeServiceAccess.PlatformService;
        applicationPaths ??= RuntimeServiceAccess.ApplicationPaths;
        var legacyRoot = ResolvePersistentCacheDirectory(
            workspace, settings, persistentDataPath, false, fileSystem, pathService,
            platformService, applicationPaths);
        var root = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(flatCacheRoot));
        var legacy = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(legacyRoot));
        if (legacy.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            !fileSystem.DirectoryExists(legacy)) return;
        var relative = pathService.GetRelativePath(root, legacy);
        if (pathService.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{pathService.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Legacy AssetBundle cache '{legacy}' is outside the Player sandbox '{root}'.");

        PlayerPersistentDataPaths.EnsurePhysicalDirectoryTree(
            root, "Player sandbox cache", fileSystem, pathService);
        PlayerPersistentDataPaths.EnsurePhysicalDirectoryTree(
            legacy, "legacy AssetBundle cache", fileSystem, pathService);

        var pointerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "previous.yaml", "pending.yaml", "active.yaml"
        };
        var files = fileSystem.EnumerateFiles(legacy, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Relative = pathService.GetRelativePath(legacy, path)
            })
            .Where(file => IsMigratableLegacyCacheFile(file.Relative, pointerNames))
            .OrderBy(file => pointerNames.Contains(pathService.GetFileName(file.Relative)) ? 1 : 0)
            .ThenBy(file => PointerOrder(pathService.GetFileName(file.Relative)))
            .ThenBy(file => file.Relative, StringComparer.Ordinal)
            .ToArray();

        var complete = true;
        string? conflict = null;
        foreach (var file in files)
        {
            var isPointer = pointerNames.Contains(pathService.GetFileName(file.Relative));
            if (isPointer && !complete) continue;
            var destination = pathService.GetFullPath(pathService.Combine(root, file.Relative));
            var destinationRelative = pathService.GetRelativePath(root, destination);
            if (pathService.IsPathRooted(destinationRelative) || destinationRelative == ".." ||
                destinationRelative.StartsWith(
                    $"..{pathService.DirectorySeparatorChar}", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Legacy AssetBundle cache entry escapes the Player sandbox: '{file.Relative}'.");
            if (TryCopyLegacyCacheFile(file.Path, destination, fileSystem, pathService)) continue;
            complete = false;
            conflict ??= destination;
        }

        PlayerStartupDiagnostics.Phase(
            complete ? "03_LEGACY_CACHE_MIGRATED" : "03_LEGACY_CACHE_PRESERVED",
            complete
                ? $"source={legacy};destination={root};source-preserved=true"
                : $"source={legacy};destination={root};conflict={conflict};source-preserved=true");
    }

    private static bool IsMigratableLegacyCacheFile(
        string relative,
        IReadOnlySet<string> pointerNames)
    {
        var normalized = relative.Replace('\\', '/');
        var separator = normalized.IndexOf('/');
        var topLevel = separator < 0 ? normalized : normalized[..separator];
        return topLevel.Equals("objects", StringComparison.OrdinalIgnoreCase) ||
               topLevel.Equals("catalogs", StringComparison.OrdinalIgnoreCase) ||
               separator < 0 && pointerNames.Contains(topLevel);
    }

    private static int PointerOrder(string fileName) =>
        fileName.Equals("previous.yaml", StringComparison.OrdinalIgnoreCase) ? 0 :
        fileName.Equals("pending.yaml", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

    private static bool TryCopyLegacyCacheFile(
        string source,
        string destination,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        if (fileSystem.FileExists(destination)) return FilesEqual(source, destination, fileSystem);
        var directory = pathService.GetDirectoryName(destination) ??
                        throw new InvalidDataException(
                            $"Legacy cache destination '{destination}' has no parent directory.");
        fileSystem.CreateDirectory(directory);
        EnsurePhysicalCacheDirectory(directory, fileSystem);
        var temporary = pathService.Combine(directory,
            $".{pathService.GetFileName(destination)}.{Guid.NewGuid():N}.migration.tmp");
        try
        {
            using (var input = fileSystem.OpenFile(
                       source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                       FileOptions.SequentialScan))
            using (var output = fileSystem.OpenFile(
                       temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                       FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                fileSystem.Flush(output, flushToDisk: true);
            }
            try { fileSystem.MoveFile(temporary, destination); }
            catch (IOException) when (fileSystem.FileExists(destination))
            {
                return FilesEqual(source, destination, fileSystem);
            }
            return true;
        }
        finally
        {
            if (fileSystem.FileExists(temporary)) fileSystem.DeleteFile(temporary);
        }
    }

    private static bool FilesEqual(string left, string right, FileUtility fileSystem)
    {
        if (fileSystem.GetFileLength(left) != fileSystem.GetFileLength(right)) return false;
        using var leftStream = fileSystem.OpenRead(left);
        using var rightStream = fileSystem.OpenRead(right);
        return System.Security.Cryptography.SHA256.HashData(leftStream)
            .SequenceEqual(System.Security.Cryptography.SHA256.HashData(rightStream));
    }

    private static void EnsurePhysicalCacheDirectory(string path, FileUtility fileSystem)
    {
        if (fileSystem.FileExists(path) && !fileSystem.DirectoryExists(path))
            throw new InvalidDataException(
                $"AssetBundle cache directory traverses a file: '{path}'.");
        if (fileSystem.DirectoryExists(path) &&
            (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"AssetBundle cache directory cannot traverse a reparse point: '{path}'.");
    }

    private static T GetBootstrapSingleton<T>(IServiceCollection services) where T : class
    {
        var registration = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(T));
        if (registration?.ImplementationInstance is T instance) return instance;
        throw new InvalidOperationException(
            $"Player bootstrap service '{typeof(T).FullName}' must be registered as a singleton instance " +
            "before AddBEnginePlayer because it is required while discovering HotUpdate assemblies.");
    }

    private static IAssetBundleManagerFactory GetAssetBundleManagerFactory(
        IServiceCollection services,
        FileUtility fileSystem,
        PathUtility pathService,
        IHttpNetworkRequestFactory httpRequestFactory)
    {
        var registration = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IAssetBundleManagerFactory));
        if (registration?.ImplementationInstance is IAssetBundleManagerFactory instance)
            return instance;
        if (registration is { Lifetime: ServiceLifetime.Singleton } &&
            registration.ImplementationType == typeof(AssetBundleManagerFactory))
        {
            var factory = new AssetBundleManagerFactory(fileSystem, pathService, httpRequestFactory);
            services.AddSingleton<IAssetBundleManagerFactory>(factory);
            return factory;
        }
        throw new InvalidOperationException(
            $"Player bootstrap service '{typeof(IAssetBundleManagerFactory).FullName}' must use the " +
            "default singleton implementation or be registered as a singleton instance before " +
            "AddBEnginePlayer because it is required while discovering HotUpdate assemblies.");
    }


}
