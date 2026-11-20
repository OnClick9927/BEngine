using BEngine.ProjectSystem;
using BEngine.Player.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.AssetBundles;
using BEngine.Platform;

namespace BEngine.Player;

internal sealed class PlayerProjectSceneLoader : IAsyncSceneLoader
{
    private readonly RuntimeWorkspace _workspace;
    private readonly IAssetBundleManager? _assetBundles;
    private readonly PlayerBuiltInResourceProvider? _builtInResources;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;

    internal PlayerProjectSceneLoader(
        RuntimeWorkspace workspace,
        IAssetBundleManager? assetBundles = null,
        PlayerBuiltInResourceProvider? builtInResources = null,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _assetBundles = assetBundles;
        _builtInResources = builtInResources;
        _fileSystem = fileSystem ?? RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
        _paths = pathService ?? RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>();
    }

    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services)
    {
        if (TryLoadBuiltInScene(sceneNameOrPath, services, out var builtInScene)) return builtInScene;
        if (TryLoadBundledScene(sceneNameOrPath, services, out var bundledScene)) return bundledScene;
        var sourcePath = ResolveScenePath(sceneNameOrPath);
        var scene = SceneAssetLoading.LoadSource(sourcePath, services);
        RuntimeAssetReferences.BindScenePath(scene, sourcePath);
        return scene;
    }

    public async BTask<Scene> LoadSceneAsync(string sceneNameOrPath, IServiceProvider services, AsyncLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.cancellationToken.ThrowIfCancellationRequested();
        string? address = null;
        byte[] bytes;
        AssetBundleHandle<byte[]>? payload = null;
        Scene? scene = null;
        var succeeded = false;
        try
        {
            if (_builtInResources is not null)
                address = ResolveBuiltInSceneAddress(sceneNameOrPath);
            if (address is not null)
                bytes = await _builtInResources!.ReadBytesAsync(address, context.cancellationToken);
            else if (HasActiveCatalog(_assetBundles) &&
                     (address = ResolveBundledSceneAddress(sceneNameOrPath)) is not null)
            {
                // A cold scene is allowed: loading its verified dependency closure must
                // finish asynchronously before field binding touches bundled objects.
                payload = await _assetBundles!.LoadBytesAsync(address, context.cancellationToken);
                bytes = payload.Asset;
            }
            else
            {
                address = ResolveScenePath(sceneNameOrPath);
                using var stream = _fileSystem.OpenRead(address);
                var readContext = new AsyncLoadContext(context.cancellationToken,
                    value => context.ReportProgress(.05f + .06f * value)) { priority = context.priority };
                bytes = await readContext.ReadAllBytesAsync(stream);
            }
            context.ReportProgress(.12f);
            SceneLoadData document = default;
            await context.RunSteps(() =>
            {
                document = SceneAssetLoading.ReadData(bytes);
                return CooperativeWorkStatus.Complete;
            });
            context.ReportProgress(.2f);
            context.ReportProgress(.3f);
            var restoreContext = new AsyncLoadContext(context.cancellationToken,
                value => context.ReportProgress(.3f + .5f * value)) { priority = context.priority };
            scene = await SceneAssetLoading.RestoreAsync(document, services, restoreContext);
            context.cancellationToken.ThrowIfCancellationRequested();
            RuntimeAssetReferences.BindScenePath(scene, address);
            context.ReportProgress(.89f);
            succeeded = true;
            return scene;
        }
        finally
        {
            payload?.Dispose();
            if (!succeeded && scene is { isCreated: true }) scene.Dispose();
        }
    }

    private bool TryLoadBuiltInScene(
        string sceneNameOrPath,
        IServiceProvider services,
        out Scene scene)
    {
        scene = null!;
        if (_builtInResources is null) return false;
        var address = ResolveBuiltInSceneAddress(sceneNameOrPath);
        if (address is null) return false;
        var document = SceneAssetLoading.ReadData(_builtInResources.ReadBytes(address));
        scene = SceneAssetLoading.Restore(document, services);
        RuntimeAssetReferences.BindScenePath(scene, address);
        return true;
    }

    private bool TryLoadBundledScene(
        string sceneNameOrPath,
        IServiceProvider services,
        out Scene scene)
    {
        scene = null!;
        if (!HasActiveCatalog(_assetBundles)) return false;
        var address = ResolveBundledSceneAddress(sceneNameOrPath);
        if (address is null) return false;
        if (!_assetBundles!.TryLoadBytes(address, out var bytes))
            throw new InvalidOperationException(
                $"Scene '{address}' is not resident in the active AssetBundle release.");
        var document = SceneAssetLoading.ReadData(bytes);
        scene = SceneAssetLoading.Restore(document, services);
        RuntimeAssetReferences.BindScenePath(scene, address);
        return true;
    }

    private static bool HasActiveCatalog(IAssetBundleManager? manager)
    {
        if (manager is not { IsInitialized: true }) return false;
        // Both sealed built-in implementations publish version and catalog from
        // the same owned state. Inspect its immutable revision without cloning
        // either public metadata snapshot just to check for presence.
        // Custom implementations retain the original public-contract check.
        return manager switch
        {
            AssetBundleManager runtime => runtime.ResourceRevision.Version is not null,
            VirtualAssetBundleManager snapshot => snapshot.ResourceRevision.Version is not null,
            _ => manager.ActiveCatalog is not null
        };
    }

    private string? ResolveBuiltInSceneAddress(string sceneNameOrPath)
    {
        var requested = ToProjectRelativePath(sceneNameOrPath);
        if (IsProjectSceneAddress(requested))
        {
            try
            {
                if (_builtInResources!.TryGetCanonicalAddress(requested, out var canonical) &&
                    canonical.Equals(requested, StringComparison.OrdinalIgnoreCase)) return canonical;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                // Preserve name lookup/fallback for paths that are not valid
                // archive addresses rather than changing their loading contract.
            }
        }
        return ResolveSceneAddress(sceneNameOrPath,
            _builtInResources!.EnumerateAddresses("Assets"), "built-in AOT archive");
    }

    private string? ResolveBundledSceneAddress(string sceneNameOrPath)
    {
        var requested = ToProjectRelativePath(sceneNameOrPath);
        if (IsProjectSceneAddress(requested))
        {
            if (_assetBundles is AssetBundleManager runtime &&
                runtime.TryGetActiveAsset(requested, out var asset)) return asset.Address;
            if (_assetBundles is VirtualAssetBundleManager snapshot &&
                snapshot.TryGetActiveAsset(requested, out var snapshotAsset)) return snapshotAsset.Address;
        }
        return ResolveSceneAddress(sceneNameOrPath,
            _assetBundles!.EnumerateAddresses("Assets"), "active AssetBundle catalog");
    }

    private static bool IsProjectSceneAddress(string address) =>
        address.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
        address.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase);

    private string? ResolveSceneAddress(
        string sceneNameOrPath,
        IEnumerable<string> addresses,
        string sourceDescription)
    {
        var requested = ToProjectRelativePath(sceneNameOrPath);
        var requestedFile = _paths.GetFileName(requested).AsSpan();
        string? match = null;
        var ambiguous = false;
        foreach (var address in addresses)
        {
            if (!address.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)) continue;
            if (address.Equals(requested, StringComparison.OrdinalIgnoreCase)) return address;
            if (ambiguous) continue; // A later exact address still takes priority.
            var fileName = SceneFileName(address);
            if (!fileName.Equals(requestedFile, StringComparison.OrdinalIgnoreCase) &&
                !fileName[..^".scene.yaml".Length].Equals(sceneNameOrPath.AsSpan(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (match is null) match = address;
            else if (!match.Equals(address, StringComparison.OrdinalIgnoreCase)) ambiguous = true;
        }
        if (ambiguous)
            throw new InvalidOperationException(
                $"Scene name '{sceneNameOrPath}' is ambiguous in the {sourceDescription}.");
        return match;
    }

    private ReadOnlySpan<char> SceneFileName(string path)
    {
        var span = path.AsSpan();
        return span[(span.LastIndexOfAny(_paths.DirectorySeparatorChar, _paths.AltDirectorySeparatorChar) + 1)..];
    }

    private string ToProjectRelativePath(string sceneNameOrPath)
    {
        if (!_paths.IsPathRooted(sceneNameOrPath))
            return sceneNameOrPath.Replace('\\', '/').TrimStart('/');
        var fullPath = _paths.GetFullPath(sceneNameOrPath);
        var relative = _paths.GetRelativePath(_workspace.RootPath, fullPath).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) ? sceneNameOrPath : relative;
    }

    private string ResolveScenePath(string sceneNameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneNameOrPath);
        var candidate = _paths.IsPathRooted(sceneNameOrPath)
            ? _paths.GetFullPath(sceneNameOrPath)
            : _workspace.ResolveInside(sceneNameOrPath.Replace('\\', '/'));
        if (_fileSystem.FileExists(candidate)) return EnsureInsideProject(candidate);

        var requestedFile = _paths.GetFileName(sceneNameOrPath);
        var matches = _fileSystem.EnumerateFiles(
                _workspace.AssetsPath, "*.scene.yaml", SearchOption.AllDirectories)
            .Where(path => _paths.GetFileName(path).Equals(requestedFile, StringComparison.OrdinalIgnoreCase) ||
                           SceneName(path).Equals(sceneNameOrPath, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"Scene '{sceneNameOrPath}' was not found."),
            _ => throw new InvalidOperationException(
                $"Scene name '{sceneNameOrPath}' is ambiguous. Use a project-relative path.")
        };
    }

    private string EnsureInsideProject(string path)
    {
        var relative = _paths.GetRelativePath(_workspace.RootPath, path);
        if (relative == ".." || relative.StartsWith($"..{_paths.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            throw new InvalidOperationException("Player scenes must be located inside the project.");
        return path;
    }

    private string SceneName(string path)
    {
        var fileName = _paths.GetFileName(path);
        return fileName.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".scene.yaml".Length]
            : _paths.GetFileNameWithoutExtension(fileName);
    }
}
