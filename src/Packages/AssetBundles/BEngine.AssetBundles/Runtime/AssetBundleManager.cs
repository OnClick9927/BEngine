using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Core;
using BEngine.Networking;
using BEngine.Platform;

namespace BEngine.AssetBundles;

public sealed partial class AssetBundleManager : ITransactionalAssetBundleManager, IAssetBundleDownloadManager,
    IAssetBundleLocalInstaller
{
    private const string LatestFileName = "latest.yaml";
    private const string VersionFileName = "version.yaml";
    private const string ActiveFileName = "active.yaml";
    private const string PreviousFileName = "previous.yaml";
    private const string PendingFileName = "pending.yaml";

    private readonly AssetBundleRuntimeOptions _options;
    private readonly string[]? _requiredBundleNames;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly IHttpNetworkRequestFactory _httpRequestFactory;
    private readonly string _cacheRoot;
    private readonly string? _builtInRoot;
    private readonly Dictionary<string, BTask<LoadedAssetBundle>> _loadedBundles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _builtInBundlePaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Uri> _builtInBundleUris = new(StringComparer.OrdinalIgnoreCase);
    private readonly BCancellationSource _httpLoadsCancellation = new();
    private RuntimeState? _active;
    private RuntimeState? _builtIn;
    private bool _initialized;
    private bool _disposed;
    private bool _hasPendingActivation;
    private bool _stateBusy;

    public AssetBundleManager(AssetBundleRuntimeOptions options) : this(
        options,
        RuntimeServiceAccess.FileSystem,
        RuntimeServiceAccess.PathService,
        HttpNetworkRequestFactory.Shared)
    {
    }

    public AssetBundleManager(
        AssetBundleRuntimeOptions options,
        FileUtility fileSystem,
        PathUtility pathService,
        IHttpNetworkRequestFactory httpRequestFactory)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
        _requiredBundleNames = options.RequiredBundleNames?.ToArray();
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _httpRequestFactory = httpRequestFactory ??
                              throw new ArgumentNullException(nameof(httpRequestFactory));
        _cacheRoot = _options.UsePersistentCache ? _pathService.GetFullPath(_options.CacheDirectory) : string.Empty;
        _builtInRoot = string.IsNullOrWhiteSpace(_options.BuiltInDirectory)
            ? null
            : _pathService.GetFullPath(_options.BuiltInDirectory);
    }

    public AssetBundleCatalog? ActiveCatalog
    {
        get
        {
            var catalog = _active?.Catalog;
            return catalog is null ? null : CloneCatalog(catalog);
        }
    }

    public AssetBundleVersion? ActiveVersion
    {
        get
        {
            var version = _active?.Version;
            return version is null ? null : CloneVersion(version);
        }
    }
    internal AssetBundleResourceRevision ResourceRevision =>
        AssetBundleResourceRevision.FromVersion(_active?.Version);

    public bool IsInitialized => _initialized;
    public string CacheDirectory => _cacheRoot;
    public string ObjectsDirectory => CachePath("objects");
    public string CatalogsDirectory => CachePath("catalogs");
    public string StagingDirectory => CachePath("staging");
    public string ActivePointerPath => CachePath(ActiveFileName);
    public string PreviousPointerPath => CachePath(PreviousFileName);
    internal string PendingPointerPath => CachePath(PendingFileName);
    private string CachePath(string name) => _options.UsePersistentCache
        ? _pathService.Combine(_cacheRoot, name) : string.Empty;
    public bool HasPendingActivation => _hasPendingActivation;

    public BValueTask InitializeAsync(BCancellationToken cancellationToken = default) => InitializeTrackedAsync(cancellationToken, null);

    internal async BValueTask InitializeTrackedAsync(BCancellationToken cancellationToken, AssetBundleLoadTracker? progress)
    {
        ThrowIfDisposed();
        if (IsInitialized) return;
        var entering = EnterStateGateAsync(cancellationToken);
        while (!entering.IsCompleted)
        {
            if (_initializationProgress is { } shared) progress?.Report(shared.Snapshot);
            await BTask.Yield();
        }
        using (await entering)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (IsInitialized) return;
            progress ??= new();
            _initializationProgress = progress;
            progress?.Report(new(AssetBundleLoadKind.Initialization, AssetBundleLoadPhase.Opening, "catalog", 0, null));
            if (!_options.UsePersistentCache)
            {
                _builtIn = await LoadBuiltInStateAsync(cancellationToken, progress) ??
                           throw new InvalidDataException(
                               "The read-only built-in AssetBundle release is missing.");
                _active = _builtIn;
                BAssetReferenceLoader.Clear();
                _hasPendingActivation = false;
                _initialized = true;
                progress?.Report(new(AssetBundleLoadKind.Initialization, AssetBundleLoadPhase.Completed, "catalog", 1, 1));
                return;
            }
            CreateCacheLayout();
            CleanupStagingFiles();

            _builtIn = await LoadBuiltInStateAsync(cancellationToken, progress);
            RuntimeState? selected = null;
            try
            {
                selected = await LoadCachedStateAsync(ActivePointerPath, cancellationToken);
            }
            catch (Exception exception) when (IsRecoverableContentFailure(exception))
            {
                selected = null;
            }

            if (selected is null)
            {
                try
                {
                    selected = await LoadCachedStateAsync(PreviousPointerPath, cancellationToken);
                    if (selected is not null)
                        await WritePointerAsync(ActivePointerPath, selected.Version, cancellationToken);
                }
                catch (Exception exception) when (IsRecoverableContentFailure(exception))
                {
                    selected = null;
                }
            }
            selected ??= _builtIn;
            _active = selected;
            BAssetReferenceLoader.Clear();
            _hasPendingActivation = await IsPendingActivationAsync(selected, cancellationToken);
            _initialized = true;
            progress?.Report(new(AssetBundleLoadKind.Initialization, AssetBundleLoadPhase.Completed, "catalog", 1, 1));
        }
    }

    public async BValueTask<AssetBundleUpdatePlan> CheckForUpdatesAsync(
        BCancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_options.RemoteBaseUri is null)
        {
            var active = _active ??
                         throw new InvalidOperationException(
                             "No active asset bundle catalog and no remote source are configured.");
            return new AssetBundleUpdatePlan(
                active.Version, active.Catalog, (byte[])active.CatalogBytes.Clone(), [], false);
        }

        var version = await FetchRemoteVersionAsync(cancellationToken);
        ValidatePackage(version.PackageName);
        var catalogUri = BuildRemoteUri(version.Version, version.CatalogFile);
        var catalogBytes = await FetchBytesWithRetryAsync(
            catalogUri, AssetBundleHttpResourceKind.Catalog,
            _options.MaximumCatalogSize, cancellationToken);
        VerifyPayload(catalogBytes, version.CatalogSize, version.CatalogSha256, "remote catalog");
        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(catalogBytes);
        ValidateCatalogPair(version, catalog);
        ValidateCatalogLimits(catalog);

        var downloads = new List<AssetBundleDescriptor>();
        var selectedBundles = GetRequiredBundles(catalog, includeInstalled: true);
        foreach (var descriptor in selectedBundles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await HasValidBundleAsync(descriptor, cancellationToken))
                downloads.Add(descriptor);
        }
        var activeState = _active;
        var hasUpdates = activeState is null ||
                         !activeState.Version.CatalogSha256.Equals(
                             version.CatalogSha256, StringComparison.OrdinalIgnoreCase) ||
                         !activeState.Version.Version.Equals(version.Version, StringComparison.Ordinal) ||
                         downloads.Count > 0;
        return new AssetBundleUpdatePlan(version, catalog, catalogBytes, downloads, hasUpdates,
            selectedBundles.Select(static bundle => bundle.Name).ToArray());
    }

    public async BValueTask<long> GetDownloadSizeAsync(
        IEnumerable<string> bundleNames, BCancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfDisposed();
        var state = _active ?? throw new InvalidOperationException("There is no active catalog.");
        long size = 0;
        if (_options.BuiltInBaseUri is not null)
        {
            foreach (var descriptor in ResolveBundleSelection(state.Catalog, bundleNames))
                if (!IsHttpBundleLoaded(descriptor)) size = checked(size + descriptor.Size);
            return size;
        }
        foreach (var descriptor in ResolveBundleSelection(state.Catalog, bundleNames))
            if (!await HasValidBundleAsync(descriptor, cancellationToken)) size = checked(size + descriptor.Size);
        return size;
    }

    public async BValueTask<AssetBundleUpdateResult> DownloadDependenciesAsync(
        IEnumerable<string> bundleNames,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundleNames);
        var names = bundleNames.ToArray();
        ThrowIfDisposed();
        using (await EnterStateGateAsync(cancellationToken))
        {
            EnsureInitialized();
            ThrowIfDisposed();
            var state = _active ?? throw new InvalidOperationException("There is no active catalog.");
            if (_options.BuiltInBaseUri is not null)
                return await PreloadHttpBundlesAsync(state, names, progress, cancellationToken);
            var downloads = new List<AssetBundleDescriptor>();
            foreach (var descriptor in ResolveBundleSelection(state.Catalog, names))
                if (!await HasValidBundleAsync(descriptor, cancellationToken)) downloads.Add(descriptor);
            if (downloads.Count > 0 && (!_options.UsePersistentCache || _options.RemoteBaseUri is null))
                throw new InvalidOperationException("Optional bundle downloads require a remote source and persistent cache.");
            var total = downloads.Aggregate(0L, static (sum, bundle) => checked(sum + bundle.Size));
            var count = 0;
            long bytes = 0;
            progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Downloading,
                0, downloads.Count, 0, total));
            foreach (var descriptor in downloads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await DownloadBundleAsync(state.Version, descriptor, cancellationToken))
                {
                    count++;
                    bytes += descriptor.Size;
                }
                progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Downloading,
                    count, downloads.Count, bytes, total, descriptor.Name));
            }
            progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Completed,
                downloads.Count, downloads.Count, total, total));
            return new AssetBundleUpdateResult(false, state.Version.Version, state.Version.Version, count, bytes);
        }
    }

    private IReadOnlyList<AssetBundleDescriptor> GetRequiredBundles(
        AssetBundleCatalog catalog, bool includeInstalled)
    {
        if (_requiredBundleNames is null) return catalog.Bundles;
        var names = new HashSet<string>(_requiredBundleNames, StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in catalog.Bundles)
            if (bundle.Name is "bengine-hotupdate" or "bengine-packages") names.Add(bundle.Name);
        // Keep previously installed optional content usable when its hash changes in a new release.
        if (includeInstalled && _active is { } active)
        {
            var availableNames = catalog.Bundles.Select(static bundle => bundle.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in active.Catalog.Bundles)
                if (availableNames.Contains(bundle.Name) && BundleFileExists(bundle)) names.Add(bundle.Name);
        }
        return ResolveBundleSelection(catalog, names);
    }

    private bool BundleFileExists(AssetBundleDescriptor descriptor) =>
        (_options.UsePersistentCache && _fileSystem.FileExists(GetObjectPath(descriptor))) ||
        (_builtInBundlePaths.TryGetValue(descriptor.Sha256, out var path) && _fileSystem.FileExists(path));

    private static IReadOnlyList<AssetBundleDescriptor> ResolveBundleSelection(
        AssetBundleCatalog catalog, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var index = new AssetBundleCatalogIndex(catalog, cloneCatalog: false);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<AssetBundleDescriptor>();
        foreach (var name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            foreach (var bundle in index.GetBundleClosure(name))
                if (selected.Add(bundle.Descriptor.Name)) ordered.Add(bundle.Descriptor);
        }
        return ordered;
    }

    public async BValueTask<AssetBundleUpdateResult> ApplyUpdateAsync(
        AssetBundleUpdatePlan plan,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ThrowIfDisposed();
        using (await EnterStateGateAsync(cancellationToken))
        {
            EnsureInitialized();
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_options.UsePersistentCache)
                throw new InvalidOperationException(
                    "AssetBundle updates require a persistent cache.");
            var targetVersion = plan.CreateTargetVersionSnapshot();
            VerifyPayload(plan.CatalogBytes.Span, targetVersion.CatalogSize,
                targetVersion.CatalogSha256, "update catalog");
            var targetCatalog = plan.CreateTargetCatalogSnapshot();
            ValidateCatalogPair(targetVersion, targetCatalog);
            ValidateCatalogLimits(targetCatalog);

            var downloads = new List<AssetBundleDescriptor>();
            var selectedBundles = ResolveBundleSelection(targetCatalog,
                plan.BundleNames.Concat(GetRequiredBundles(targetCatalog, includeInstalled: true)
                    .Select(static bundle => bundle.Name)));
            foreach (var descriptor in selectedBundles)
                if (!await HasValidBundleAsync(descriptor, cancellationToken))
                    downloads.Add(descriptor);

            var current = _active;
            var previousVersion = current?.Version.Version ?? string.Empty;
            if (current is not null &&
                current.Version.Version.Equals(targetVersion.Version, StringComparison.Ordinal) &&
                current.Version.CatalogSha256.Equals(
                    targetVersion.CatalogSha256, StringComparison.OrdinalIgnoreCase) &&
                downloads.Count == 0)
                return new AssetBundleUpdateResult(
                    false, previousVersion, previousVersion, 0, 0);
            if (downloads.Count > 0 && _options.RemoteBaseUri is null)
                throw new InvalidOperationException("The update requires downloads but no remote source is configured.");

            var totalBytes = downloads.Aggregate(0L, (total, item) => checked(total + item.Size));
            var completedBundles = 0;
            long completedBytes = 0;
            var downloadedBundles = 0;
            long downloadedBytes = 0;
            progress?.Report(new AssetBundleUpdateProgress(
                AssetBundleUpdatePhase.Downloading, 0, downloads.Count, 0, totalBytes));

            try
            {
                foreach (var descriptor in downloads)
                {
                    var downloaded = await DownloadBundleAsync(
                        targetVersion, descriptor, cancellationToken);
                    if (downloaded)
                    {
                        downloadedBundles++;
                        downloadedBytes += descriptor.Size;
                    }
                    completedBundles++;
                    completedBytes += descriptor.Size;
                    progress?.Report(new AssetBundleUpdateProgress(
                        AssetBundleUpdatePhase.Downloading,
                        completedBundles,
                        downloads.Count,
                        completedBytes,
                        totalBytes,
                        descriptor.Name));
                }
            }
            catch
            {
                CleanupStagingFiles();
                throw;
            }

            progress?.Report(new AssetBundleUpdateProgress(
                AssetBundleUpdatePhase.Verifying,
                downloads.Count,
                downloads.Count,
                totalBytes,
                totalBytes));
            foreach (var descriptor in selectedBundles)
                if (!await HasValidBundleAsync(descriptor, cancellationToken))
                    throw new InvalidDataException(
                        $"Bundle '{descriptor.Name}' is unavailable after the update download.");

            progress?.Report(new AssetBundleUpdateProgress(
                AssetBundleUpdatePhase.Activating,
                downloads.Count,
                downloads.Count,
                totalBytes,
                totalBytes));
            var catalogPath = GetCatalogCachePath(targetVersion.CatalogSha256);
            await AtomicWriteAsync(catalogPath, plan.CatalogBytes, cancellationToken);

            if (current is not null)
            {
                await PersistCatalogAsync(current, cancellationToken);
                await WritePointerAsync(PreviousPointerPath, current.Version, cancellationToken);
            }
            else if (_fileSystem.FileExists(PreviousPointerPath))
            {
                _fileSystem.DeleteFile(PreviousPointerPath);
            }
            await WritePointerAsync(PendingPointerPath, targetVersion, cancellationToken);
            await WritePointerAsync(ActivePointerPath, targetVersion, cancellationToken);
            var activated = new RuntimeState(
                targetVersion,
                targetCatalog,
                plan.CatalogBytes.ToArray());
            _active = activated;
            BAssetReferenceLoader.Clear();
            _hasPendingActivation = true;
            progress?.Report(new AssetBundleUpdateProgress(
                AssetBundleUpdatePhase.Completed,
                downloads.Count,
                downloads.Count,
                totalBytes,
                totalBytes));
            return new AssetBundleUpdateResult(
                true,
                previousVersion,
                activated.Version.Version,
                downloadedBundles,
                downloadedBytes);
        }
    }

    public async BValueTask<bool> RollbackAsync(BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using (await EnterStateGateAsync(cancellationToken))
        {
            EnsureInitialized();
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_options.UsePersistentCache) return false;
            RuntimeState? previous;
            try
            {
                previous = await LoadCachedStateAsync(PreviousPointerPath, cancellationToken);
            }
            catch (Exception exception) when (IsRecoverableContentFailure(exception))
            {
                previous = null;
            }
            if (previous is null)
            {
                // A first remote-only activation has no previous release. It must remain
                // reversible after its pending marker is committed as part of a larger
                // Player startup transaction.
                if (!_hasPendingActivation && !_fileSystem.FileExists(ActivePointerPath))
                    return false;
                if (_fileSystem.FileExists(ActivePointerPath)) _fileSystem.DeleteFile(ActivePointerPath);
                if (_fileSystem.FileExists(PreviousPointerPath)) _fileSystem.DeleteFile(PreviousPointerPath);
                if (_fileSystem.FileExists(PendingPointerPath)) _fileSystem.DeleteFile(PendingPointerPath);
                _active = _builtIn;
                BAssetReferenceLoader.Clear();
                _hasPendingActivation = false;
                return true;
            }

            var current = _active;
            if (!_hasPendingActivation && SameRelease(current, previous))
            {
                if (_fileSystem.FileExists(PreviousPointerPath))
                    _fileSystem.DeleteFile(PreviousPointerPath);
                return true;
            }
            await WritePointerAsync(ActivePointerPath, previous.Version, cancellationToken);
            _active = previous;
            BAssetReferenceLoader.Clear();
            if (_fileSystem.FileExists(PendingPointerPath))
                _fileSystem.DeleteFile(PendingPointerPath);
            _hasPendingActivation = false;
            if (current is not null && !SameRelease(current, previous))
            {
                await PersistCatalogAsync(current, cancellationToken);
                await WritePointerAsync(PreviousPointerPath, current.Version, cancellationToken);
            }
            else if (_fileSystem.FileExists(PreviousPointerPath))
            {
                _fileSystem.DeleteFile(PreviousPointerPath);
            }
            return true;
        }
    }

    public async BValueTask CommitPendingActivationAsync(
        BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using (await EnterStateGateAsync(cancellationToken))
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_options.UsePersistentCache)
            {
                _hasPendingActivation = false;
                return;
            }
            if (_fileSystem.FileExists(PendingPointerPath)) _fileSystem.DeleteFile(PendingPointerPath);
            _hasPendingActivation = false;
        }
    }

    public BValueTask<AssetBundleHandle<byte[]>> LoadBytesAsync(string address, BCancellationToken cancellationToken = default) =>
        LoadBytesTrackedAsync(address, cancellationToken, null);

    internal async BValueTask<AssetBundleHandle<byte[]>> LoadBytesTrackedAsync(
        string address, BCancellationToken cancellationToken, AssetBundleLoadTracker? progress)
    {
        EnsureInitialized();
        ThrowIfDisposed();
        var state = _active ??
                    throw new InvalidOperationException("No asset bundle catalog is active.");
        var canonicalAddress = AssetBundleValidation.NormalizeAddress(address);
        if (!state.Index.TryGetAsset(canonicalAddress, out var asset))
            throw new KeyNotFoundException($"Asset bundle address '{canonicalAddress}' was not found.");
        progress?.ConfigureAsset(asset, state.Index.GetBundleClosure(asset.Bundle).Select(item => GetBundleLoadTracker(item.Descriptor)));
        var leases = await AcquireBundleClosureAsync(state.Index, asset.Bundle, cancellationToken);
        try
        {
            var owner = leases[^1];
            var bytes = await ReadAssetWithProgressAsync(owner.Bundle, asset, cancellationToken, progress);
            return new AssetBundleHandle<byte[]>(
                asset.Address, (byte[])bytes.Clone(), () => ReleaseBundles(leases));
        }
        catch
        {
            ReleaseBundles(leases);
            throw;
        }
    }

    public BValueTask<AssetBundleHandle<string>> LoadTextAsync(string address, BCancellationToken cancellationToken = default) =>
        LoadTextTrackedAsync(address, cancellationToken, null);

    internal async BValueTask<AssetBundleHandle<string>> LoadTextTrackedAsync(
        string address, BCancellationToken cancellationToken, AssetBundleLoadTracker? progress)
    {
        EnsureInitialized();
        ThrowIfDisposed();
        var state = _active ??
                    throw new InvalidOperationException("No asset bundle catalog is active.");
        var canonicalAddress = AssetBundleValidation.NormalizeAddress(address);
        if (!state.Index.TryGetAsset(canonicalAddress, out var asset))
            throw new KeyNotFoundException($"Asset bundle address '{canonicalAddress}' was not found.");
        progress?.ConfigureAsset(asset, state.Index.GetBundleClosure(asset.Bundle).Select(item => GetBundleLoadTracker(item.Descriptor)));
        var leases = await AcquireBundleClosureAsync(state.Index, asset.Bundle, cancellationToken);
        try
        {
            var owner = leases[^1];
            var bytes = await ReadAssetWithProgressAsync(owner.Bundle, asset, cancellationToken, progress);
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return new AssetBundleHandle<string>(asset.Address, text, () => ReleaseBundles(leases));
        }
        catch
        {
            ReleaseBundles(leases);
            throw;
        }
    }

    public bool TryLoadBytes(string address, out byte[] bytes)
    {
        bytes = [];
        if (!IsInitialized || _disposed || _active is null) return false;
        AssetBundleAsset asset;
        try
        {
            var canonical = AssetBundleValidation.NormalizeAddress(address);
            if (!_active.Index.TryGetAsset(canonical, out asset!)) return false;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return false;
        }

        var leases = new List<BundleLease>();
        try
        {
            foreach (var indexedBundle in _active.Index.GetBundleClosure(asset.Bundle))
            {
                if (!TryAcquireBundleSynchronously(
                        indexedBundle.Descriptor, indexedBundle.Assets, out var bundle))
                    return false;
                leases.Add(new BundleLease(indexedBundle.Descriptor, bundle));
            }
            bytes = (byte[])leases[^1].Bundle.ReadAsset(asset, _options.MaximumAssetSize).Clone();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            ReleaseBundles(leases);
        }
    }

    public IReadOnlyList<string> EnumerateAddresses(string prefix = "")
    {
        EnsureInitialized();
        ThrowIfDisposed();
        var index = _active?.Index;
        if (index is null) return [];
        var normalizedPrefix = NormalizePrefix(prefix);
        return index.Addresses
            .Where(address => address.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public int UnloadUnused()
    {
        ThrowIfDisposed();
        var unloaded = 0;
        var snapshot = _loadedBundles.ToArray();
        foreach (var (hash, task) in snapshot)
        {
            if (!task.IsCompleted)
                continue;
            if (!task.IsCompletedSuccessfully)
            {
                RemoveLoadedBundle(hash, task);
                continue;
            }
            if (!task.Result.TryDisposeIfUnused()) continue;
            RemoveLoadedBundle(hash, task);
            unloaded++;
        }
        return unloaded;
    }

    public async BValueTask<int> CleanupAsync(BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using (await EnterStateGateAsync(cancellationToken))
        {
            EnsureInitialized();
            ThrowIfDisposed();
            _ = UnloadUnused();
            if (!_options.UsePersistentCache) return 0;
            var keepBundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keepCatalogs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddState(_active, keepBundles, keepCatalogs);
            try
            {
                AddState(await LoadCachedStateAsync(PreviousPointerPath, cancellationToken),
                    keepBundles, keepCatalogs);
            }
            catch (Exception exception) when (IsRecoverableContentFailure(exception))
            {
            }
            var loadedSnapshot = _loadedBundles.ToArray();
            foreach (var (hash, task) in loadedSnapshot)
                if (task.IsCompletedSuccessfully && task.Result.ReferenceCount > 0)
                    keepBundles.Add(hash);

            var deleted = 0;
            foreach (var path in _fileSystem.EnumerateFiles(ObjectsDirectory, "*.bassetbundle"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hash = _pathService.GetFileNameWithoutExtension(path);
                if (keepBundles.Contains(hash)) continue;
                try { _fileSystem.DeleteFile(path); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (var path in _fileSystem.EnumerateFiles(CatalogsDirectory, "*.yaml"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hash = _pathService.GetFileNameWithoutExtension(path);
                if (keepCatalogs.Contains(hash)) continue;
                try { _fileSystem.DeleteFile(path); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            CleanupStagingFiles();
            return deleted;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpLoadsCancellation.Cancel();
        _httpLoadsCancellation.Dispose();
        _initialized = false;
        var loaded = _loadedBundles.Values.ToArray();
        _loadedBundles.Clear();
        _bundleLoadTrackers.Clear();
        foreach (var pending in loaded)
        {
            try
            {
                if (pending.IsCompletedSuccessfully) pending.Result.Dispose();
            }
            catch { }
        }
    }

    public BValueTask DisposeAsync()
    {
        Dispose();
        return BValueTask.CompletedTask;
    }

    internal bool TryGetActiveAsset(string canonicalAddress, out AssetBundleAsset asset)
    {
        var state = _active;
        if (state is not null) return state.Index.TryGetAsset(canonicalAddress, out asset!);
        asset = null!;
        return false;
    }

    internal bool TryGetActiveMainAsset(Guid ownerGuid, out AssetBundleAsset asset)
    {
        var state = _active;
        if (state is not null) return state.Index.TryGetMainAsset(ownerGuid, out asset!);
        asset = null!;
        return false;
    }

    private async BValueTask<List<BundleLease>> AcquireBundleClosureAsync(
        AssetBundleCatalogIndex index,
        string bundleName,
        BCancellationToken cancellationToken)
    {
        var ordered = index.GetBundleClosure(bundleName);
        var leases = new List<BundleLease>(ordered.Count);
        try
        {
            foreach (var indexedBundle in ordered)
            {
                var bundle = await AcquireBundleAsync(
                    indexedBundle.Descriptor, indexedBundle.Assets, cancellationToken);
                leases.Add(new BundleLease(indexedBundle.Descriptor, bundle));
            }
            return leases;
        }
        catch
        {
            ReleaseBundles(leases);
            throw;
        }
    }

    private async BValueTask<LoadedAssetBundle> AcquireBundleAsync(
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        BCancellationToken cancellationToken)
    {
        var hash = descriptor.Sha256.ToLowerInvariant();
        while (true)
        {
            ThrowIfDisposed();
            if (_localInstallInProgress)
                throw new InvalidOperationException("AssetBundles cannot be loaded during content installation or repair.");
            if (!_loadedBundles.TryGetValue(hash, out var task))
            {
                task = OpenBundleAsync(
                    descriptor, assets, _httpLoadsCancellation.Token, GetBundleLoadTracker(descriptor)).AsBTask();
                _loadedBundles.Add(hash, task);
            }
            LoadedAssetBundle bundle;
            try
            {
                while (!task.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfDisposed();
                    await BValueTask.Yield();
                }
                bundle = await task;
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                if (task.IsCompleted && !task.IsCompletedSuccessfully) RemoveLoadedBundle(hash, task);
                throw;
            }
            if (bundle.TryAcquire()) return bundle;
            RemoveLoadedBundle(hash, task);
        }
    }

    private bool TryAcquireBundleSynchronously(
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        out LoadedAssetBundle bundle)
    {
        var hash = descriptor.Sha256.ToLowerInvariant();
        if (_localInstallInProgress)
        {
            bundle = null!;
            return false;
        }
        while (true)
        {
            if (!_loadedBundles.TryGetValue(hash, out var task))
            {
                // A browser cannot synchronously wait for fetch. Async loading/preloading warms this path.
                if (_options.BuiltInBaseUri is not null)
                {
                    bundle = null!;
                    return false;
                }
                var path = ResolveBundlePath(descriptor);
                bundle = LoadedAssetBundle.Open(
                    _fileSystem, path, descriptor, assets, _options);
                task = BTask<LoadedAssetBundle>.FromResult(bundle);
                _loadedBundles.Add(hash, task);
            }
            if (!task.IsCompleted)
            {
                bundle = null!;
                return false;
            }
            if (!task.IsCompletedSuccessfully)
            {
                RemoveLoadedBundle(hash, task);
                bundle = null!;
                return false;
            }
            bundle = task.Result;
            if (bundle.TryAcquire()) return true;
            RemoveLoadedBundle(hash, task);
        }
    }

    private async BValueTask<LoadedAssetBundle> OpenBundleAsync(
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        BCancellationToken cancellationToken, AssetBundleLoadTracker progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadedAssetBundle bundle;
        progress.Report(new(AssetBundleLoadKind.Bundle, AssetBundleLoadPhase.Opening, descriptor.Name, 0, descriptor.Size));
        try
        {
            if (_builtInBundleUris.TryGetValue(descriptor.Sha256, out var uri))
            {
                var stream = await ReadHttpBundleAsync(uri, descriptor, cancellationToken, progress);
                progress.Report(new(AssetBundleLoadKind.Bundle, AssetBundleLoadPhase.Opening, descriptor.Name, descriptor.Size, descriptor.Size));
                bundle = LoadedAssetBundle.OpenVerified(stream, descriptor, assets, _options);
            }
            else
            {
                var path = ResolveBundlePath(descriptor);
                bundle = await LoadedAssetBundle.OpenAsync(
                    _fileSystem, path, descriptor, assets, _options, cancellationToken,
                    count => progress.Report(new(
                        AssetBundleLoadKind.Bundle, AssetBundleLoadPhase.Verifying, descriptor.Name, count, descriptor.Size)));
            }
            if (_disposed)
            {
                bundle.Dispose();
                throw new ObjectDisposedException(nameof(AssetBundleManager));
            }
            progress.Report(new(AssetBundleLoadKind.Bundle, AssetBundleLoadPhase.Completed, descriptor.Name, descriptor.Size, descriptor.Size));
            return bundle;
        }
        catch
        {
            progress.Report(progress.Snapshot with { Phase = AssetBundleLoadPhase.Failed });
            throw;
        }
    }

    private string ResolveBundlePath(AssetBundleDescriptor descriptor)
    {
        var objectPath = GetObjectPath(descriptor);
        if (_options.UsePersistentCache && _fileSystem.FileExists(objectPath))
        {
            return objectPath;
        }
        if (_builtInBundlePaths.TryGetValue(descriptor.Sha256, out var builtInPath) &&
            _fileSystem.FileExists(builtInPath))
        {
            return builtInPath;
        }
        throw new FileNotFoundException(
            $"Asset bundle '{descriptor.Name}' ({descriptor.Sha256}) is not installed.", objectPath);
    }

    private async BValueTask<bool> HasValidBundleAsync(
        AssetBundleDescriptor descriptor,
        BCancellationToken cancellationToken)
    {
        if (_builtInBundleUris.TryGetValue(descriptor.Sha256, out var uri))
        {
            try
            {
                using var stream = await ReadHttpBundleAsync(uri, descriptor, cancellationToken);
                return true;
            }
            catch (Exception exception) when (IsRecoverableContentFailure(exception)) { return false; }
        }
        foreach (var path in BundleCandidates(descriptor))
        {
            if (!_fileSystem.FileExists(path)) continue;
            try
            {
                await AssetBundleFileVerifier.VerifyAsync(
                    _fileSystem, path, descriptor.Size, descriptor.Sha256,
                    _options.MaximumBundleSize, cancellationToken);
                return true;
            }
            catch (Exception exception) when (IsRecoverableContentFailure(exception))
            {
            }
        }
        return false;
    }

    private IEnumerable<string> BundleCandidates(AssetBundleDescriptor descriptor)
    {
        if (_options.UsePersistentCache) yield return GetObjectPath(descriptor);
        if (_builtInBundlePaths.TryGetValue(descriptor.Sha256, out var path)) yield return path;
    }

    private async BValueTask<bool> DownloadBundleAsync(
        AssetBundleVersion version,
        AssetBundleDescriptor descriptor,
        BCancellationToken cancellationToken)
    {
        if (await HasValidBundleAsync(descriptor, cancellationToken)) return false;
        var uri = BuildRemoteUri(string.IsNullOrEmpty(descriptor.SourceVersion)
            ? version.Version : descriptor.SourceVersion, "bundles", descriptor.FileName);
        var destination = GetObjectPath(descriptor);
        _fileSystem.CreateDirectory(StagingDirectory);
        var part = _pathService.Combine(StagingDirectory, descriptor.Sha256.ToLowerInvariant() + ".part");
        Exception? failure = null;
        try
        {
            for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (_fileSystem.FileExists(part)) _fileSystem.DeleteFile(part);
                    await DownloadOnceAsync(
                        uri, part, descriptor, attempt + 1, cancellationToken);
                    if (_fileSystem.FileExists(destination))
                    {
                        if (await HasValidBundleAsync(descriptor, cancellationToken))
                        {
                            _fileSystem.DeleteFile(part);
                            return false;
                        }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfDisposed();
                    _fileSystem.MoveFile(part, destination, overwrite: true);
                    return true;
                }
                catch (Exception exception) when (IsRetryable(exception, cancellationToken) &&
                                                  attempt < _options.MaxRetries)
                {
                    failure = exception;
                    if (_fileSystem.FileExists(part)) _fileSystem.DeleteFile(part);
                    await BValueTask.Delay(RetryDelay(attempt), cancellationToken);
                }
                catch (Exception exception)
                {
                    failure = exception;
                    break;
                }
            }
        }
        finally
        {
            if (_fileSystem.FileExists(part)) _fileSystem.DeleteFile(part);
            DeleteStagingDirectoryIfEmpty();
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is InvalidDataException invalidData)
            throw new InvalidDataException(invalidData.Message, invalidData);
        throw new IOException($"Failed to download asset bundle '{descriptor.Name}'.", failure);
    }

    private async BValueTask DownloadOnceAsync(
        Uri uri,
        string destination,
        AssetBundleDescriptor descriptor,
        int attempt,
        BCancellationToken cancellationToken)
    {
        using var handler = new BundleDownloadHandler(
            destination, descriptor, _options.MaximumBundleSize, _fileSystem);
        await SendHttpStreamingAsync(
            uri, AssetBundleHttpResourceKind.Bundle, attempt, handler, cancellationToken);
        handler.VerifyCompleted();
    }

    private async BValueTask<AssetBundleVersion> FetchRemoteVersionAsync(
        BCancellationToken cancellationToken)
    {
        var latestBytes = await FetchBytesWithRetryAsync(
            BuildRemoteUri(LatestFileName), AssetBundleHttpResourceKind.VersionPointer,
            _options.MaximumCatalogSize, cancellationToken);

        // Existing servers publish a complete version manifest as latest.yaml.
        // Keep accepting that contract while new servers use the small mutable pointer.
        if (TryDeserializeVersion(latestBytes, out var legacyVersion))
        {
            ValidatePackage(legacyVersion.PackageName);
            return legacyVersion;
        }

        var pointer = AssetBundleCatalogSerializer.DeserializeLatestPointer(latestBytes);
        var versionBytes = await FetchBytesWithRetryAsync(
            BuildRemoteUri(pointer.Version, VersionFileName),
            AssetBundleHttpResourceKind.VersionMetadata,
            _options.MaximumCatalogSize,
            cancellationToken);
        var version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
        ValidatePackage(version.PackageName);
        if (!version.Version.Equals(pointer.Version, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Asset bundle latest pointer and version metadata versions do not match.");
        return version;
    }

    private static bool IsMetadataDeserializationFailure(Exception exception) =>
        exception is InvalidDataException or YamlException or NotSupportedException;

    private static bool TryDeserializeVersion(
        ReadOnlySpan<byte> bytes,
        out AssetBundleVersion version)
    {
        try
        {
            version = AssetBundleCatalogSerializer.DeserializeVersion(bytes);
            return true;
        }
        catch (Exception exception) when (IsMetadataDeserializationFailure(exception))
        {
            version = null!;
            return false;
        }
    }

    private async BValueTask<byte[]> FetchBytesWithRetryAsync(
        Uri uri,
        AssetBundleHttpResourceKind resourceKind,
        long maximumSize,
        BCancellationToken cancellationToken, AssetBundleLoadTracker? progress = null)
    {
        Exception? failure = null;
        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var handler = new LimitedMemoryDownloadHandler(uri, maximumSize);
                await SendHttpStreamingAsync(
                    uri, resourceKind, attempt + 1, handler, cancellationToken, progress: progress);
                return handler.ToArray();
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                throw;
            }
            catch (Exception exception) when (IsRetryable(exception, cancellationToken) &&
                                              attempt < _options.MaxRetries)
            {
                failure = exception;
                await BValueTask.Delay(RetryDelay(attempt), cancellationToken);
            }
            catch (Exception exception)
            {
                failure = exception;
                break;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new IOException($"Failed to fetch remote asset bundle content '{uri}'.", failure);
    }

    private async BValueTask SendHttpStreamingAsync(
        Uri uri,
        AssetBundleHttpResourceKind resourceKind,
        int attempt,
        IHttpDownloadHandler downloadHandler,
        BCancellationToken cancellationToken,
        string? progressResource = null,
        long? expectedSize = null,
        AssetBundleLoadTracker? progress = null)
    {
        using var request = _httpRequestFactory.Create(
            uri, HttpRequestMethod.Get, _options.HttpTransport);
        using var disposalRegistration = _httpLoadsCancellation.Token.Register(request.Abort);
        request.Timeout = HttpNetworkRequest.InfiniteTimeout;
        request.InactivityTimeout = _options.HttpInactivityTimeout;
        request.MaximumDownloadBytes = resourceKind == AssetBundleHttpResourceKind.Bundle
            ? _options.MaximumBundleSize : _options.MaximumCatalogSize;
        if (_options.BuiltInBaseUri is not null)
        {
            request.BypassCache = true;
        }
        if (request.BypassCache || resourceKind == AssetBundleHttpResourceKind.VersionPointer)
        {
            request.SetRequestHeader("Cache-Control", "no-cache, no-store, max-age=0");
            request.SetRequestHeader("Pragma", "no-cache");
        }
        var kind = resourceKind == AssetBundleHttpResourceKind.Bundle ? AssetBundleLoadKind.Bundle
            : resourceKind == AssetBundleHttpResourceKind.Catalog ? AssetBundleLoadKind.Catalog : AssetBundleLoadKind.Version;
        var resource = progressResource ?? System.IO.Path.GetFileName(uri.AbsolutePath);
        progress?.Report(new(kind, AssetBundleLoadPhase.Waiting, resource, 0, expectedSize, attempt));
        var sending = request.SendAsync(downloadHandler, cancellationToken);
        long previous = -1;
        long? previousTotal = null;
        while (!sending.IsCompleted && progress is not null)
        {
            var total = expectedSize ?? request.ContentLength;
            if (previous != request.DownloadedBytes || previousTotal != total)
            {
                previous = request.DownloadedBytes;
                previousTotal = total;
                progress?.Report(new(kind, AssetBundleLoadPhase.Receiving, resource, previous, total, attempt));
            }
            await BTask.Yield();
        }
        await sending;
        progress?.Report(new(kind, request.IsSuccess ? AssetBundleLoadPhase.Verifying : AssetBundleLoadPhase.Failed,
            resource, request.DownloadedBytes, expectedSize ?? request.ContentLength, attempt));
        ReportHttpTransfer(new AssetBundleHttpTransferDiagnostic(
            resourceKind,
            RedactEndpoint(uri),
            attempt,
            request.ResponseCode is { } statusCode ? (int)statusCode : null,
            request.DownloadedBytes,
            request.Result));
        if (request.Result == NetworkRequestResult.Success) return;
        if (request.Result == NetworkRequestResult.ProtocolError)
            throw new HttpRequestException(request.Error, request.Exception, request.ResponseCode);
        if (request.Exception is not null)
            ExceptionDispatchInfo.Capture(request.Exception).Throw();
        throw new IOException(request.Error ?? $"HTTP request '{uri}' failed.");
    }

    private void ReportHttpTransfer(AssetBundleHttpTransferDiagnostic diagnostic)
    {
        try { _options.HttpTransferObserver?.Invoke(diagnostic); }
        catch
        {
            // Diagnostics must never change update behavior.
        }
    }

    private static string RedactEndpoint(Uri uri)
    {
        var endpoint = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
        return endpoint.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.UriEscaped);
    }

    private async BValueTask<RuntimeState?> LoadBuiltInStateAsync(
        BCancellationToken cancellationToken, AssetBundleLoadTracker? progress = null)
    {
        if (_options.BuiltInBaseUri is not null) return await LoadHttpBuiltInStateAsync(cancellationToken, progress);
        if (_builtInRoot is null || !_fileSystem.DirectoryExists(_builtInRoot)) return null;
        var metadataPointerPath = new[] { LatestFileName, VersionFileName }
            .Select(file => _pathService.Combine(_builtInRoot, file))
            .FirstOrDefault(_fileSystem.FileExists);
        if (metadataPointerPath is null) return null;
        var versionBytes = await ReadLimitedFileAsync(
            metadataPointerPath, _options.MaximumCatalogSize, cancellationToken, progress, AssetBundleLoadKind.Version);
        AssetBundleVersion version;
        if (TryDeserializeVersion(versionBytes, out version))
        {
            // Legacy built-in releases store the complete manifest in latest.yaml or version.yaml.
        }
        else if (_pathService.GetFileName(metadataPointerPath)
                 .Equals(LatestFileName, StringComparison.OrdinalIgnoreCase))
        {
            var latest = AssetBundleCatalogSerializer.DeserializeLatestPointer(versionBytes);
            var pointedVersionRoot = _pathService.Combine(
                _builtInRoot, NormalizeVersionSegment(latest.Version));
            var pointedVersionPath = ResolveInside(pointedVersionRoot, VersionFileName);
            var pointedVersionBytes = await ReadLimitedFileAsync(
                pointedVersionPath, _options.MaximumCatalogSize, cancellationToken, progress, AssetBundleLoadKind.Version);
            version = AssetBundleCatalogSerializer.DeserializeVersion(pointedVersionBytes);
            if (!version.Version.Equals(latest.Version, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Asset bundle latest pointer and version metadata versions do not match.");
        }
        else
        {
            version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
        }
        ValidatePackage(version.PackageName);
        progress?.Report(new(AssetBundleLoadKind.Version, AssetBundleLoadPhase.Completed, VersionFileName, versionBytes.Length, versionBytes.Length));
        var versionSegment = NormalizeVersionSegment(version.Version);
        var versionRoot = _pathService.Combine(_builtInRoot, versionSegment);
        var catalogPath = ResolveInside(versionRoot, version.CatalogFile);
        if (!_fileSystem.FileExists(catalogPath))
            catalogPath = ResolveInside(_builtInRoot, version.CatalogFile);
        var catalogBytes = await ReadLimitedFileAsync(
            catalogPath, _options.MaximumCatalogSize, cancellationToken, progress);
        VerifyPayload(catalogBytes, version.CatalogSize, version.CatalogSha256, "built-in catalog");
        progress?.Report(new(AssetBundleLoadKind.Catalog, AssetBundleLoadPhase.Opening, version.CatalogFile, catalogBytes.Length, version.CatalogSize));
        await BTask.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(catalogBytes);
        ValidateCatalogPair(version, catalog);
        ValidateCatalogLimits(catalog);
        progress?.Report(new(AssetBundleLoadKind.Catalog, AssetBundleLoadPhase.Completed, version.CatalogFile, catalogBytes.Length, version.CatalogSize));

        var requiredNames = GetRequiredBundles(catalog, includeInstalled: false)
            .Select(static bundle => bundle.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in catalog.Bundles)
        {
            var path = _pathService.Combine(versionRoot, "bundles", descriptor.FileName);
            if (!_fileSystem.FileExists(path))
                path = _pathService.Combine(_builtInRoot, "bundles", descriptor.FileName);
            if (!_fileSystem.FileExists(path) && !requiredNames.Contains(descriptor.Name)) continue;
            if (_options.UsePersistentCache)
                await AssetBundleFileVerifier.VerifyAsync(
                    _fileSystem, path, descriptor.Size, descriptor.Sha256,
                    _options.MaximumBundleSize, cancellationToken);
            else if (!_fileSystem.FileExists(path) || _fileSystem.GetFileLength(path) != descriptor.Size)
                throw new InvalidDataException($"Built-in bundle '{descriptor.Name}' is missing or has the wrong size.");
            // Immutable direct sources are hashed by LoadedAssetBundle on first
            // access, rather than reading the entire packaged game at startup.
            _builtInBundlePaths[descriptor.Sha256] = path;
        }
        return new RuntimeState(version, catalog, catalogBytes);
    }

    private async BValueTask<RuntimeState?> LoadCachedStateAsync(
        string pointerPath,
        BCancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(pointerPath)) return null;
        var versionBytes = await ReadLimitedFileAsync(
            pointerPath, _options.MaximumCatalogSize, cancellationToken);
        var version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
        ValidatePackage(version.PackageName);
        var catalogPath = GetCatalogCachePath(version.CatalogSha256);
        byte[] catalogBytes;
        if (_fileSystem.FileExists(catalogPath))
        {
            catalogBytes = await ReadLimitedFileAsync(
                catalogPath, _options.MaximumCatalogSize, cancellationToken);
        }
        else if (_builtIn is not null && _builtIn.Version.CatalogSha256.Equals(
                     version.CatalogSha256, StringComparison.OrdinalIgnoreCase))
        {
            catalogBytes = (byte[])_builtIn.CatalogBytes.Clone();
        }
        else
        {
            throw new FileNotFoundException("The cached asset bundle catalog is missing.", catalogPath);
        }
        VerifyPayload(catalogBytes, version.CatalogSize, version.CatalogSha256, "cached catalog");
        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(catalogBytes);
        ValidateCatalogPair(version, catalog);
        ValidateCatalogLimits(catalog);
        foreach (var descriptor in GetRequiredBundles(catalog, includeInstalled: false))
            if (!await HasValidBundleAsync(descriptor, cancellationToken))
                throw new FileNotFoundException(
                    $"Cached bundle '{descriptor.Name}' is missing or invalid.", descriptor.FileName);
        return new RuntimeState(version, catalog, catalogBytes);
    }

    private void ValidateCatalogPair(AssetBundleVersion version, AssetBundleCatalog catalog)
    {
        version.Validate();
        catalog.Validate();
        ValidatePackage(version.PackageName);
        if (!catalog.PackageName.Equals(version.PackageName, StringComparison.Ordinal))
            throw new InvalidDataException("Asset bundle version and catalog package names do not match.");
        if (!catalog.Version.Equals(version.Version, StringComparison.Ordinal))
            throw new InvalidDataException("Asset bundle version and catalog versions do not match.");
        _ = NormalizeVersionSegment(version.Version);
    }

    private void ValidateCatalogLimits(AssetBundleCatalog catalog)
    {
        if (catalog.Bundles.Count > _options.MaximumBundleCount)
            throw new InvalidDataException("Asset bundle catalog contains too many bundles.");
        if (catalog.Assets.Count > _options.MaximumAssetCount)
            throw new InvalidDataException("Asset bundle catalog contains too many assets.");
        foreach (var bundle in catalog.Bundles)
            if (bundle.Size > _options.MaximumBundleSize)
                throw new InvalidDataException($"Bundle '{bundle.Name}' exceeds its runtime size limit.");
        foreach (var asset in catalog.Assets)
            if (asset.Size > _options.MaximumAssetSize)
                throw new InvalidDataException($"Asset '{asset.Address}' exceeds its runtime size limit.");
    }

    private void ValidatePackage(string packageName)
    {
        if (!packageName.Equals(_options.PackageName, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Asset bundle package '{packageName}' does not match '{_options.PackageName}'.");
    }

    private static string NormalizeVersionSegment(string version)
    {
        AssetBundleVersionLabel.ValidatePortable(version, "asset bundle version");
        return version;
    }

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
        var normalized = prefix.Replace('\\', '/').Trim('/');
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)) return "Assets/";
        if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            normalized = "Assets/" + normalized;
        if (normalized.Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("Asset address prefix cannot leave Assets.", nameof(prefix));
        return normalized;
    }

    private static void ReleaseBundles(IReadOnlyList<BundleLease> leases)
    {
        for (var index = leases.Count - 1; index >= 0; index--) leases[index].Bundle.Release();
    }

    private void RemoveLoadedBundle(string hash, BTask<LoadedAssetBundle> task)
    {
        if (_loadedBundles.TryGetValue(hash, out var current) && ReferenceEquals(current, task))
        {
            _loadedBundles.Remove(hash);
            _bundleLoadTrackers.Remove(hash);
        }
    }

    private void CreateCacheLayout()
    {
        _fileSystem.CreateDirectory(_cacheRoot);
        _fileSystem.CreateDirectory(ObjectsDirectory);
        _fileSystem.CreateDirectory(CatalogsDirectory);
    }

    private void CleanupStagingFiles()
    {
        if (!_options.UsePersistentCache) return;
        if (!_fileSystem.DirectoryExists(StagingDirectory)) return;
        foreach (var path in _fileSystem.EnumerateFiles(StagingDirectory, "*.part"))
        {
            try { _fileSystem.DeleteFile(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        DeleteStagingDirectoryIfEmpty();
    }

    private void DeleteStagingDirectoryIfEmpty()
    {
        if (!_fileSystem.DirectoryExists(StagingDirectory)) return;
        try
        {
            if (_fileSystem.EnumerateFileSystemEntries(StagingDirectory).Any()) return;
            _fileSystem.DeleteDirectory(StagingDirectory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async BValueTask<bool> IsPendingActivationAsync(
        RuntimeState? active,
        BCancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(PendingPointerPath)) return false;
        if (active is null)
        {
            try { _fileSystem.DeleteFile(PendingPointerPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }
        try
        {
            var bytes = await ReadLimitedFileAsync(
                PendingPointerPath, _options.MaximumCatalogSize, cancellationToken);
            var pending = AssetBundleCatalogSerializer.DeserializeVersion(bytes);
            var matches = pending.PackageName.Equals(active.Version.PackageName, StringComparison.Ordinal) &&
                          pending.Version.Equals(active.Version.Version, StringComparison.Ordinal) &&
                          pending.CatalogSha256.Equals(
                              active.Version.CatalogSha256, StringComparison.OrdinalIgnoreCase);
            if (!matches) _fileSystem.DeleteFile(PendingPointerPath);
            return matches;
        }
        catch (Exception exception) when (IsRecoverableContentFailure(exception))
        {
            try { _fileSystem.DeleteFile(PendingPointerPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }
    }

    private string GetObjectPath(AssetBundleDescriptor descriptor) =>
        _options.UsePersistentCache
            ? _pathService.Combine(ObjectsDirectory, descriptor.Sha256.ToLowerInvariant() + ".bassetbundle")
            : string.Empty;

    private string GetCatalogCachePath(string hash) =>
        _pathService.Combine(CatalogsDirectory, hash.ToLowerInvariant() + ".yaml");

    private BValueTask PersistCatalogAsync(
        RuntimeState state,
        BCancellationToken cancellationToken) =>
        AtomicWriteAsync(GetCatalogCachePath(state.Version.CatalogSha256),
            state.CatalogBytes, cancellationToken);

    private BValueTask WritePointerAsync(
        string path,
        AssetBundleVersion version,
        BCancellationToken cancellationToken) =>
        AtomicWriteAsync(
            path, AssetBundleCatalogSerializer.SerializeVersion(version), cancellationToken);

    private async BValueTask AtomicWriteAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        BCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = _pathService.GetFullPath(path);
        var directory = _pathService.GetDirectoryName(fullPath) ??
                        throw new InvalidDataException($"Asset bundle path '{path}' has no parent directory.");
        _fileSystem.CreateDirectory(directory);
        var temporary = _pathService.Combine(
            directory, $".{_pathService.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = _fileSystem.OpenFile(
                       temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       16 * 1024, FileOptions.WriteThrough))
            {
                const int chunkSize = 64 * 1024;
                var offset = 0;
                while (offset < bytes.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = Math.Min(chunkSize, bytes.Length - offset);
                    stream.Write(bytes.Span.Slice(offset, count));
                    offset += count;
                    if (offset < bytes.Length) await BValueTask.Yield();
                }
                _fileSystem.Flush(stream, flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            _fileSystem.MoveFile(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (_fileSystem.FileExists(temporary)) _fileSystem.DeleteFile(temporary);
        }
    }

    private async BValueTask<byte[]> ReadLimitedFileAsync(
        string path,
        long maximumSize,
        BCancellationToken cancellationToken, AssetBundleLoadTracker? progress = null,
        AssetBundleLoadKind kind = AssetBundleLoadKind.Catalog)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_fileSystem.FileExists(path))
            throw new FileNotFoundException("Asset bundle metadata file was not found.", path);
        var fileLength = _fileSystem.GetFileLength(path);
        if (fileLength > maximumSize)
            throw new InvalidDataException($"Asset bundle metadata '{path}' exceeds its size limit.");
        if (fileLength > int.MaxValue)
            throw new InvalidDataException(
                $"Asset bundle metadata '{path}' is too large to load.");
        var bytes = new byte[(int)fileLength];
        using var stream = _fileSystem.OpenFile(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        var offset = 0;
        progress?.Report(new(kind, AssetBundleLoadPhase.Reading, _pathService.GetFileName(path), 0, fileLength));
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
            if (read == 0)
                throw new EndOfStreamException(
                    $"Asset bundle metadata '{path}' ended unexpectedly.");
            offset += read;
            progress?.Report(new(kind, AssetBundleLoadPhase.Reading, _pathService.GetFileName(path), offset, fileLength));
            if (offset < bytes.Length) await BValueTask.Yield();
        }
        return bytes;
    }

    private static void VerifyPayload(
        ReadOnlySpan<byte> bytes,
        long expectedSize,
        string expectedHash,
        string description)
    {
        if (bytes.Length != expectedSize)
            throw new InvalidDataException($"{description} size does not match its version metadata.");
        var actual = AssetBundleCatalogSerializer.ComputeSha256(bytes);
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{description} failed its SHA256 verification.");
    }

    private Uri BuildRemoteUri(params string[] segments)
    {
        var root = _options.RemoteBaseUri ??
                   throw new InvalidOperationException("No remote asset bundle source is configured.");
        var baseUri = root.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? root
            : new Uri(root.AbsoluteUri + "/", UriKind.Absolute);
        var relative = string.Join("/", segments.SelectMany(segment =>
            AssetBundleValidation.NormalizeRelativePath(segment, "remote asset bundle path").Split('/'))
            .Select(Uri.EscapeDataString));
        return new Uri(baseUri, relative);
    }

    private string ResolveInside(string root, string relativePath)
    {
        var fullRoot = _pathService.TrimEndingDirectorySeparator(_pathService.GetFullPath(root));
        var normalized = AssetBundleValidation.NormalizeRelativePath(relativePath, "asset bundle path");
        var path = _pathService.GetFullPath(_pathService.Combine(fullRoot,
            normalized.Replace('/', _pathService.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot + _pathService.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset bundle path escapes its root: '{relativePath}'.");
        return path;
    }

    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(2_000, 100 * (1 << Math.Min(attempt, 4))));

    private static bool IsRetryable(Exception exception, BCancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        exception is HttpRequestException or IOException or TimeoutException;

    private static bool IsRecoverableContentFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException;

    private static bool SameRelease(RuntimeState? left, RuntimeState? right) =>
        left is not null && right is not null &&
        left.Version.PackageName.Equals(right.Version.PackageName, StringComparison.Ordinal) &&
        left.Version.Version.Equals(right.Version.Version, StringComparison.Ordinal) &&
        left.Version.CatalogSha256.Equals(
            right.Version.CatalogSha256, StringComparison.OrdinalIgnoreCase);

    private static void AddState(
        RuntimeState? state,
        ISet<string> bundleHashes,
        ISet<string> catalogHashes)
    {
        if (state is null) return;
        catalogHashes.Add(state.Version.CatalogSha256);
        foreach (var bundle in state.Catalog.Bundles) bundleHashes.Add(bundle.Sha256);
    }

    private static AssetBundleCatalog CloneCatalog(AssetBundleCatalog catalog) =>
        AssetBundleCatalogSerializer.CloneCatalog(catalog);

    private static AssetBundleVersion CloneVersion(AssetBundleVersion version) => new()
    {
        Format = version.Format,
        SchemaVersion = version.SchemaVersion,
        PackageName = version.PackageName,
        Version = version.Version,
        CatalogFile = version.CatalogFile,
        CatalogSha256 = version.CatalogSha256.ToLowerInvariant(),
        CatalogSize = version.CatalogSize
    };

    private void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("AssetBundleManager.InitializeAsync must complete first.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AssetBundleManager));
    }

    private async BValueTask<StateGateLease> EnterStateGateAsync(
        BCancellationToken cancellationToken)
    {
        while (_stateBusy)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            await BValueTask.Yield();
        }
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        _stateBusy = true;
        return new StateGateLease(this);
    }

    private sealed class StateGateLease(AssetBundleManager owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._stateBusy = false;
        }
    }

    private sealed class LimitedMemoryDownloadHandler(
        Uri uri,
        long maximumSize) : IHttpDownloadHandler, IDisposable
    {
        private readonly MemoryStream _stream = new();
        private bool _acceptBody;

        public void ReceiveResponse(HttpTransportResponse response)
        {
            _acceptBody = IsSuccess(response.StatusCode);
            if (!_acceptBody) return;
            if (TryGetContentLength(response, out var length) && length > maximumSize)
                throw new InvalidDataException(
                    $"Remote content '{uri}' exceeds its size limit.");
        }

        public void ReceiveData(ReadOnlyMemory<byte> data)
        {
            if (!_acceptBody) return;
            if (checked(_stream.Length + data.Length) > maximumSize)
                throw new InvalidDataException(
                    $"Remote content '{uri}' exceeds its size limit.");
            _stream.Write(data.Span);
        }

        public byte[] ToArray() => _stream.ToArray();
        public void Dispose() => _stream.Dispose();
    }

    private sealed class BundleDownloadHandler : IHttpDownloadHandler, IDisposable
    {
        private readonly string _destination;
        private readonly AssetBundleDescriptor _descriptor;
        private readonly long _maximumSize;
        private readonly FileUtility _fileSystem;
        private readonly IncrementalHash _hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private Stream? _output;
        private bool _acceptBody;
        private long _total;

        internal BundleDownloadHandler(
            string destination,
            AssetBundleDescriptor descriptor,
            long maximumSize,
            FileUtility fileSystem)
        {
            _destination = destination;
            _descriptor = descriptor;
            _maximumSize = maximumSize;
            _fileSystem = fileSystem;
        }

        public void ReceiveResponse(HttpTransportResponse response)
        {
            _acceptBody = IsSuccess(response.StatusCode);
            if (!_acceptBody) return;
            if (TryGetContentLength(response, out var length) && length != _descriptor.Size)
                throw new InvalidDataException(
                    $"Remote bundle '{_descriptor.Name}' length does not match its catalog.");
            _output = _fileSystem.OpenFile(
                _destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.SequentialScan);
        }

        public void ReceiveData(ReadOnlyMemory<byte> data)
        {
            if (!_acceptBody || data.IsEmpty) return;
            _total = checked(_total + data.Length);
            if (_total > _descriptor.Size || _total > _maximumSize)
                throw new InvalidDataException(
                    $"Remote bundle '{_descriptor.Name}' exceeds its declared size.");
            _hash.AppendData(data.Span);
            (_output ?? throw new InvalidDataException(
                "Remote bundle data arrived before its response headers."))
                .Write(data.Span);
        }

        internal void VerifyCompleted()
        {
            if (!_acceptBody) return;
            if (_output is not null) _fileSystem.Flush(_output, flushToDisk: true);
            var actualHash = Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
            if (_total != _descriptor.Size ||
                !actualHash.Equals(_descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Remote bundle '{_descriptor.Name}' failed content verification.");
        }

        public void Dispose()
        {
            _output?.Dispose();
            _hash.Dispose();
        }
    }

    private static bool IsSuccess(HttpStatusCode statusCode) =>
        statusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices;

    private static bool TryGetContentLength(
        HttpTransportResponse response,
        out long contentLength)
    {
        contentLength = 0;
        // Fetch exposes decoded response bytes, while Content-Length still describes
        // the encoded wire body. Verify the decoded size and SHA-256 against the
        // catalog instead of comparing these different representations.
        if (response.Headers.TryGetValue("Content-Encoding", out var encodings) &&
            encodings.Any(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))))
            return false;
        return response.Headers.TryGetValue("Content-Length", out var values) &&
               values.Length == 1 &&
               long.TryParse(values[0], out contentLength) && contentLength >= 0;
    }

    private sealed class RuntimeState
    {
        internal RuntimeState(
            AssetBundleVersion version,
            AssetBundleCatalog catalog,
            byte[] catalogBytes)
        {
            Version = version;
            Catalog = catalog;
            CatalogBytes = catalogBytes;
            Index = new AssetBundleCatalogIndex(catalog, cloneCatalog: false, catalogValidated: true);
        }

        internal AssetBundleVersion Version { get; }
        internal AssetBundleCatalog Catalog { get; }
        internal byte[] CatalogBytes { get; }
        internal AssetBundleCatalogIndex Index { get; }
    }

    private sealed record BundleLease(
        AssetBundleDescriptor Descriptor,
        LoadedAssetBundle Bundle);
}
