using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using BEngine.Networking;

namespace BEngine.AssetBundles;

public sealed class AssetBundleManager : IAssetBundleManager
{
    private const string LatestFileName = "latest.json";
    private const string VersionFileName = "version.json";
    private const string ActiveFileName = "active.json";
    private const string PreviousFileName = "previous.json";

    private readonly AssetBundleRuntimeOptions _options;
    private readonly string _cacheRoot;
    private readonly string? _builtInRoot;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly object _bundleCacheGate = new();
    private readonly Dictionary<string, Task<LoadedAssetBundle>> _loadedBundles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _builtInBundlePaths =
        new(StringComparer.OrdinalIgnoreCase);
    private RuntimeState? _active;
    private RuntimeState? _builtIn;
    private volatile bool _initialized;
    private volatile bool _disposed;

    public AssetBundleManager(AssetBundleRuntimeOptions options)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
        _cacheRoot = Path.GetFullPath(_options.CacheDirectory);
        _builtInRoot = string.IsNullOrWhiteSpace(_options.BuiltInDirectory)
            ? null
            : Path.GetFullPath(_options.BuiltInDirectory);
        _httpClient = _options.HttpClient ?? new HttpClient();
        _ownsHttpClient = _options.HttpClient is null;
    }

    public AssetBundleCatalog? ActiveCatalog
    {
        get
        {
            var catalog = Volatile.Read(ref _active)?.Catalog;
            return catalog is null ? null : CloneCatalog(catalog);
        }
    }

    public AssetBundleVersion? ActiveVersion
    {
        get
        {
            var version = Volatile.Read(ref _active)?.Version;
            return version is null ? null : CloneVersion(version);
        }
    }
    public bool IsInitialized => _initialized;
    public string CacheDirectory => _cacheRoot;
    public string ObjectsDirectory => Path.Combine(_cacheRoot, "objects");
    public string CatalogsDirectory => Path.Combine(_cacheRoot, "catalogs");
    public string StagingDirectory => Path.Combine(_cacheRoot, "staging");
    public string ActivePointerPath => Path.Combine(_cacheRoot, ActiveFileName);
    public string PreviousPointerPath => Path.Combine(_cacheRoot, PreviousFileName);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsInitialized) return;
        using var linked = CreateLinkedTokenSource(cancellationToken);
        await _stateGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsInitialized) return;
            CreateCacheLayout();
            CleanupStagingFiles();

            _builtIn = await LoadBuiltInStateAsync(linked.Token).ConfigureAwait(false);
            RuntimeState? selected = null;
            try
            {
                selected = await LoadCachedStateAsync(ActivePointerPath, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsRecoverableContentFailure(exception))
            {
                selected = null;
            }

            if (selected is null)
            {
                try
                {
                    selected = await LoadCachedStateAsync(PreviousPointerPath, linked.Token)
                        .ConfigureAwait(false);
                    if (selected is not null)
                        await WritePointerAsync(ActivePointerPath, selected.Version, linked.Token)
                            .ConfigureAwait(false);
                }
                catch (Exception exception) when (IsRecoverableContentFailure(exception))
                {
                    selected = null;
                }
            }
            selected ??= _builtIn;
            Volatile.Write(ref _active, selected);
            _initialized = true;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<AssetBundleUpdatePlan> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfDisposed();
        using var linked = CreateLinkedTokenSource(cancellationToken);
        var operationToken = linked.Token;
        if (_options.RemoteBaseUri is null)
        {
            var active = Volatile.Read(ref _active) ??
                         throw new InvalidOperationException(
                             "No active asset bundle catalog and no remote source are configured.");
            return new AssetBundleUpdatePlan(
                active.Version, active.Catalog, (byte[])active.CatalogBytes.Clone(), [], false);
        }

        var versionBytes = await FetchRemoteVersionAsync(operationToken).ConfigureAwait(false);
        var version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
        ValidatePackage(version.PackageName);
        var catalogUri = BuildRemoteUri(version.Version, version.CatalogFile);
        var catalogBytes = await FetchBytesWithRetryAsync(
            catalogUri, _options.MaximumCatalogSize, operationToken).ConfigureAwait(false);
        VerifyPayload(catalogBytes, version.CatalogSize, version.CatalogSha256, "remote catalog");
        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(catalogBytes);
        ValidateCatalogPair(version, catalog);
        ValidateCatalogLimits(catalog);

        var downloads = new List<AssetBundleDescriptor>();
        foreach (var descriptor in catalog.Bundles)
        {
            operationToken.ThrowIfCancellationRequested();
            if (!await HasValidBundleAsync(descriptor, operationToken).ConfigureAwait(false))
                downloads.Add(descriptor);
        }
        var activeState = Volatile.Read(ref _active);
        var hasUpdates = activeState is null ||
                         !activeState.Version.CatalogSha256.Equals(
                             version.CatalogSha256, StringComparison.OrdinalIgnoreCase) ||
                         !activeState.Version.Version.Equals(version.Version, StringComparison.Ordinal) ||
                         downloads.Count > 0;
        return new AssetBundleUpdatePlan(version, catalog, catalogBytes, downloads, hasUpdates);
    }

    public async Task<AssetBundleUpdateResult> ApplyUpdateAsync(
        AssetBundleUpdatePlan plan,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ThrowIfDisposed();
        using var linked = CreateLinkedTokenSource(cancellationToken);
        await _stateGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            ThrowIfDisposed();
            var targetVersion = plan.CreateTargetVersionSnapshot();
            VerifyPayload(plan.CatalogBytes.Span, targetVersion.CatalogSize,
                targetVersion.CatalogSha256, "update catalog");
            var targetCatalog = plan.CreateTargetCatalogSnapshot();
            ValidateCatalogPair(targetVersion, targetCatalog);
            ValidateCatalogLimits(targetCatalog);

            var downloads = new List<AssetBundleDescriptor>();
            foreach (var descriptor in targetCatalog.Bundles)
                if (!await HasValidBundleAsync(descriptor, linked.Token).ConfigureAwait(false))
                    downloads.Add(descriptor);

            var current = Volatile.Read(ref _active);
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
                        targetVersion, descriptor, linked.Token).ConfigureAwait(false);
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
            foreach (var descriptor in targetCatalog.Bundles)
                if (!await HasValidBundleAsync(descriptor, linked.Token).ConfigureAwait(false))
                    throw new InvalidDataException(
                        $"Bundle '{descriptor.Name}' is unavailable after the update download.");

            progress?.Report(new AssetBundleUpdateProgress(
                AssetBundleUpdatePhase.Activating,
                downloads.Count,
                downloads.Count,
                totalBytes,
                totalBytes));
            var catalogPath = GetCatalogCachePath(targetVersion.CatalogSha256);
            await AtomicWriteAsync(catalogPath, plan.CatalogBytes, linked.Token).ConfigureAwait(false);

            if (current is not null)
            {
                await PersistCatalogAsync(current, linked.Token).ConfigureAwait(false);
                await WritePointerAsync(PreviousPointerPath, current.Version, linked.Token)
                    .ConfigureAwait(false);
            }
            await WritePointerAsync(ActivePointerPath, targetVersion, linked.Token)
                .ConfigureAwait(false);
            var activated = new RuntimeState(
                targetVersion,
                targetCatalog,
                plan.CatalogBytes.ToArray());
            Volatile.Write(ref _active, activated);
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
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<bool> RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linked = CreateLinkedTokenSource(cancellationToken);
        await _stateGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            ThrowIfDisposed();
            RuntimeState? previous;
            try
            {
                previous = await LoadCachedStateAsync(PreviousPointerPath, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsRecoverableContentFailure(exception))
            {
                return false;
            }
            if (previous is null) return false;

            var current = Volatile.Read(ref _active);
            await WritePointerAsync(ActivePointerPath, previous.Version, linked.Token)
                .ConfigureAwait(false);
            if (current is not null)
            {
                await PersistCatalogAsync(current, linked.Token).ConfigureAwait(false);
                await WritePointerAsync(PreviousPointerPath, current.Version, linked.Token)
                    .ConfigureAwait(false);
            }
            else if (File.Exists(PreviousPointerPath))
            {
                File.Delete(PreviousPointerPath);
            }
            Volatile.Write(ref _active, previous);
            return true;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<AssetBundleHandle<byte[]>> LoadBytesAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfDisposed();
        var state = Volatile.Read(ref _active) ??
                    throw new InvalidOperationException("No asset bundle catalog is active.");
        var canonicalAddress = AssetBundleValidation.NormalizeAddress(address);
        if (!state.Index.TryGetAsset(canonicalAddress, out var asset))
            throw new KeyNotFoundException($"Asset bundle address '{canonicalAddress}' was not found.");
        var leases = await AcquireBundleClosureAsync(state.Index, asset.Bundle, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var owner = leases[^1];
            var bytes = await owner.Bundle.ReadAssetAsync(
                asset, _options.MaximumAssetSize, cancellationToken).ConfigureAwait(false);
            return new AssetBundleHandle<byte[]>(
                asset.Address, (byte[])bytes.Clone(), () => ReleaseBundles(leases));
        }
        catch
        {
            ReleaseBundles(leases);
            throw;
        }
    }

    public async Task<AssetBundleHandle<string>> LoadTextAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ThrowIfDisposed();
        var state = Volatile.Read(ref _active) ??
                    throw new InvalidOperationException("No asset bundle catalog is active.");
        var canonicalAddress = AssetBundleValidation.NormalizeAddress(address);
        if (!state.Index.TryGetAsset(canonicalAddress, out var asset))
            throw new KeyNotFoundException($"Asset bundle address '{canonicalAddress}' was not found.");
        var leases = await AcquireBundleClosureAsync(state.Index, asset.Bundle, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var owner = leases[^1];
            var bytes = await owner.Bundle.ReadAssetAsync(
                asset, _options.MaximumAssetSize, cancellationToken).ConfigureAwait(false);
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
        if (!IsInitialized || _disposed || !ContainsAddress(address)) return false;
        using var handle = LoadBytesAsync(address).ConfigureAwait(false).GetAwaiter().GetResult();
        bytes = handle.Value;
        return true;
    }

    public IReadOnlyList<string> EnumerateAddresses(string prefix = "")
    {
        EnsureInitialized();
        ThrowIfDisposed();
        var index = Volatile.Read(ref _active)?.Index;
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
        KeyValuePair<string, Task<LoadedAssetBundle>>[] snapshot;
        lock (_bundleCacheGate) snapshot = _loadedBundles.ToArray();
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

    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linked = CreateLinkedTokenSource(cancellationToken);
        await _stateGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            ThrowIfDisposed();
            _ = UnloadUnused();
            var keepBundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keepCatalogs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddState(Volatile.Read(ref _active), keepBundles, keepCatalogs);
            try
            {
                AddState(await LoadCachedStateAsync(PreviousPointerPath, linked.Token)
                    .ConfigureAwait(false), keepBundles, keepCatalogs);
            }
            catch (Exception exception) when (IsRecoverableContentFailure(exception))
            {
            }
            KeyValuePair<string, Task<LoadedAssetBundle>>[] loadedSnapshot;
            lock (_bundleCacheGate) loadedSnapshot = _loadedBundles.ToArray();
            foreach (var (hash, task) in loadedSnapshot)
                if (task.IsCompletedSuccessfully && task.Result.ReferenceCount > 0)
                    keepBundles.Add(hash);

            var deleted = 0;
            foreach (var path in Directory.EnumerateFiles(ObjectsDirectory, "*.bassetbundle"))
            {
                linked.Token.ThrowIfCancellationRequested();
                var hash = Path.GetFileNameWithoutExtension(path);
                if (keepBundles.Contains(hash)) continue;
                try { File.Delete(path); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (var path in Directory.EnumerateFiles(CatalogsDirectory, "*.json"))
            {
                linked.Token.ThrowIfCancellationRequested();
                var hash = Path.GetFileNameWithoutExtension(path);
                if (keepCatalogs.Contains(hash)) continue;
                try { File.Delete(path); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            CleanupStagingFiles();
            return deleted;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public void Dispose()
    {
        Task<LoadedAssetBundle>[] loaded;
        lock (_bundleCacheGate)
        {
            if (_disposed) return;
            _disposed = true;
            loaded = _loadedBundles.Values.ToArray();
            _loadedBundles.Clear();
        }
        _lifetime.Cancel();
        foreach (var task in loaded)
        {
            try { task.ConfigureAwait(false).GetAwaiter().GetResult().Dispose(); }
            catch { }
        }
        if (_ownsHttpClient) _httpClient.Dispose();
        _lifetime.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private bool ContainsAddress(string address)
    {
        try
        {
            var canonical = AssetBundleValidation.NormalizeAddress(address);
            return Volatile.Read(ref _active)?.Index.TryGetAsset(canonical, out _) == true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    internal bool TryGetActiveAsset(string canonicalAddress, out AssetBundleAsset asset)
    {
        var state = Volatile.Read(ref _active);
        if (state is not null) return state.Index.TryGetAsset(canonicalAddress, out asset!);
        asset = null!;
        return false;
    }

    internal bool TryGetActiveMainAsset(Guid ownerGuid, out AssetBundleAsset asset)
    {
        var state = Volatile.Read(ref _active);
        if (state is not null) return state.Index.TryGetMainAsset(ownerGuid, out asset!);
        asset = null!;
        return false;
    }

    private async Task<List<BundleLease>> AcquireBundleClosureAsync(
        AssetBundleCatalogIndex index,
        string bundleName,
        CancellationToken cancellationToken)
    {
        var ordered = index.GetBundleClosure(bundleName);
        var leases = new List<BundleLease>(ordered.Count);
        try
        {
            foreach (var indexedBundle in ordered)
            {
                var bundle = await AcquireBundleAsync(
                    indexedBundle.Descriptor, indexedBundle.Assets, cancellationToken).ConfigureAwait(false);
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

    private async Task<LoadedAssetBundle> AcquireBundleAsync(
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        CancellationToken cancellationToken)
    {
        var hash = descriptor.Sha256.ToLowerInvariant();
        while (true)
        {
            ThrowIfDisposed();
            Task<LoadedAssetBundle> task;
            lock (_bundleCacheGate)
            {
                ThrowIfDisposed();
                if (!_loadedBundles.TryGetValue(hash, out task!))
                {
                    task = OpenBundleAsync(descriptor, assets, _lifetime.Token);
                    _loadedBundles.Add(hash, task);
                }
            }
            LoadedAssetBundle bundle;
            try
            {
                bundle = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested &&
                                                     !task.IsCompleted)
            {
                throw;
            }
            catch
            {
                if (task.IsCompleted) RemoveLoadedBundle(hash, task);
                throw;
            }
            if (bundle.TryAcquire()) return bundle;
            RemoveLoadedBundle(hash, task);
        }
    }

    private async Task<LoadedAssetBundle> OpenBundleAsync(
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        CancellationToken cancellationToken)
    {
        var path = await ResolveBundlePathAsync(descriptor, cancellationToken).ConfigureAwait(false);
        return await LoadedAssetBundle.OpenAsync(
            path, descriptor, assets, _options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ResolveBundlePathAsync(
        AssetBundleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var objectPath = GetObjectPath(descriptor);
        if (File.Exists(objectPath))
        {
            await AssetBundleFileVerifier.VerifyAsync(objectPath, descriptor.Size, descriptor.Sha256,
                _options.MaximumBundleSize, cancellationToken).ConfigureAwait(false);
            return objectPath;
        }
        if (_builtInBundlePaths.TryGetValue(descriptor.Sha256, out var builtInPath) &&
            File.Exists(builtInPath))
        {
            await AssetBundleFileVerifier.VerifyAsync(builtInPath, descriptor.Size, descriptor.Sha256,
                _options.MaximumBundleSize, cancellationToken).ConfigureAwait(false);
            return builtInPath;
        }
        throw new FileNotFoundException(
            $"Asset bundle '{descriptor.Name}' ({descriptor.Sha256}) is not installed.", objectPath);
    }

    private async Task<bool> HasValidBundleAsync(
        AssetBundleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        foreach (var path in BundleCandidates(descriptor))
        {
            if (!File.Exists(path)) continue;
            try
            {
                await AssetBundleFileVerifier.VerifyAsync(path, descriptor.Size, descriptor.Sha256,
                    _options.MaximumBundleSize, cancellationToken).ConfigureAwait(false);
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
        yield return GetObjectPath(descriptor);
        if (_builtInBundlePaths.TryGetValue(descriptor.Sha256, out var path)) yield return path;
    }

    private async Task<bool> DownloadBundleAsync(
        AssetBundleVersion version,
        AssetBundleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (await HasValidBundleAsync(descriptor, cancellationToken).ConfigureAwait(false)) return false;
        var uri = BuildRemoteUri(version.Version, "bundles", descriptor.FileName);
        var destination = GetObjectPath(descriptor);
        var part = Path.Combine(StagingDirectory, descriptor.Sha256.ToLowerInvariant() + ".part");
        Exception? failure = null;
        try
        {
            for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(part)) File.Delete(part);
                    await DownloadOnceAsync(uri, part, descriptor, cancellationToken).ConfigureAwait(false);
                    if (File.Exists(destination))
                    {
                        if (await HasValidBundleAsync(descriptor, cancellationToken).ConfigureAwait(false))
                        {
                            File.Delete(part);
                            return false;
                        }
                        File.Delete(destination);
                    }
                    File.Move(part, destination);
                    return true;
                }
                catch (Exception exception) when (IsRetryable(exception, cancellationToken) &&
                                                  attempt < _options.MaxRetries)
                {
                    failure = exception;
                    if (File.Exists(part)) File.Delete(part);
                    await Task.Delay(RetryDelay(attempt), cancellationToken).ConfigureAwait(false);
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
            if (File.Exists(part)) File.Delete(part);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is InvalidDataException invalidData)
            throw new InvalidDataException(invalidData.Message, invalidData);
        throw new IOException($"Failed to download asset bundle '{descriptor.Name}'.", failure);
    }

    private async Task DownloadOnceAsync(
        Uri uri,
        string destination,
        AssetBundleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await SendHttpStreamingAsync(uri, async (response, token) =>
        {
            if (!response.IsSuccessStatusCode) return;
            if (response.Content.Headers.ContentLength is { } contentLength &&
                contentLength != descriptor.Size)
                throw new InvalidDataException(
                    $"Remote bundle '{descriptor.Name}' length does not match its catalog.");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var output = new FileStream(
                destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0) break;
                total = checked(total + read);
                if (total > descriptor.Size || total > _options.MaximumBundleSize)
                    throw new InvalidDataException(
                        $"Remote bundle '{descriptor.Name}' exceeds its declared size.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
            await output.FlushAsync(token).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (total != descriptor.Size ||
                !actualHash.Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Remote bundle '{descriptor.Name}' failed content verification.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> FetchRemoteVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await FetchBytesWithRetryAsync(
                BuildRemoteUri(LatestFileName), _options.MaximumCatalogSize, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return await FetchBytesWithRetryAsync(
                BuildRemoteUri(VersionFileName), _options.MaximumCatalogSize, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<byte[]> FetchBytesWithRetryAsync(
        Uri uri,
        long maximumSize,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var output = new MemoryStream();
                await SendHttpStreamingAsync(uri, async (response, token) =>
                {
                    if (!response.IsSuccessStatusCode) return;
                    if (response.Content.Headers.ContentLength is { } length && length > maximumSize)
                        throw new InvalidDataException($"Remote content '{uri}' exceeds its size limit.");
                    await using var source = await response.Content.ReadAsStreamAsync(token)
                        .ConfigureAwait(false);
                    var buffer = new byte[32 * 1024];
                    long total = 0;
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                        if (read == 0) break;
                        total = checked(total + read);
                        if (total > maximumSize)
                            throw new InvalidDataException(
                                $"Remote content '{uri}' exceeds its size limit.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    }
                }, cancellationToken).ConfigureAwait(false);
                return output.ToArray();
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                throw;
            }
            catch (Exception exception) when (IsRetryable(exception, cancellationToken) &&
                                              attempt < _options.MaxRetries)
            {
                failure = exception;
                await Task.Delay(RetryDelay(attempt), cancellationToken).ConfigureAwait(false);
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

    private async Task SendHttpStreamingAsync(
        Uri uri,
        Func<HttpResponseMessage, CancellationToken, Task> responseHandler,
        CancellationToken cancellationToken)
    {
        using var request = new HttpNetworkRequest(uri, HttpMethod.Get, _httpClient)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
        await request.SendAsync(responseHandler, cancellationToken).ConfigureAwait(false);
        if (request.Result == NetworkRequestResult.Success) return;
        if (request.Result == NetworkRequestResult.ProtocolError)
            throw new HttpRequestException(request.Error, request.Exception, request.ResponseCode);
        if (request.Exception is not null)
            ExceptionDispatchInfo.Capture(request.Exception).Throw();
        throw new IOException(request.Error ?? $"HTTP request '{uri}' failed.");
    }

    private async Task<RuntimeState?> LoadBuiltInStateAsync(CancellationToken cancellationToken)
    {
        if (_builtInRoot is null || !Directory.Exists(_builtInRoot)) return null;
        var pointer = new[] { LatestFileName, VersionFileName }
            .Select(file => Path.Combine(_builtInRoot, file))
            .FirstOrDefault(File.Exists);
        if (pointer is null) return null;
        var versionBytes = await ReadLimitedFileAsync(
            pointer, _options.MaximumCatalogSize, cancellationToken).ConfigureAwait(false);
        var version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
        ValidatePackage(version.PackageName);
        var versionSegment = NormalizeVersionSegment(version.Version);
        var versionRoot = Path.Combine(_builtInRoot, versionSegment);
        var catalogPath = ResolveInside(versionRoot, version.CatalogFile);
        if (!File.Exists(catalogPath))
            catalogPath = ResolveInside(_builtInRoot, version.CatalogFile);
        var catalogBytes = await ReadLimitedFileAsync(
            catalogPath, _options.MaximumCatalogSize, cancellationToken).ConfigureAwait(false);
        VerifyPayload(catalogBytes, version.CatalogSize, version.CatalogSha256, "built-in catalog");
        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(catalogBytes);
        ValidateCatalogPair(version, catalog);
        ValidateCatalogLimits(catalog);

        foreach (var descriptor in catalog.Bundles)
        {
            var path = Path.Combine(versionRoot, "bundles", descriptor.FileName);
            if (!File.Exists(path)) path = Path.Combine(_builtInRoot, "bundles", descriptor.FileName);
            await AssetBundleFileVerifier.VerifyAsync(path, descriptor.Size, descriptor.Sha256,
                _options.MaximumBundleSize, cancellationToken).ConfigureAwait(false);
            _builtInBundlePaths[descriptor.Sha256] = path;
        }
        return new RuntimeState(version, catalog, catalogBytes);
    }

    private async Task<RuntimeState?> LoadCachedStateAsync(
        string pointerPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(pointerPath)) return null;
        var versionBytes = await ReadLimitedFileAsync(
            pointerPath, _options.MaximumCatalogSize, cancellationToken).ConfigureAwait(false);
        var version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
        ValidatePackage(version.PackageName);
        var catalogPath = GetCatalogCachePath(version.CatalogSha256);
        byte[] catalogBytes;
        if (File.Exists(catalogPath))
        {
            catalogBytes = await ReadLimitedFileAsync(
                catalogPath, _options.MaximumCatalogSize, cancellationToken).ConfigureAwait(false);
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
        foreach (var descriptor in catalog.Bundles)
            if (!await HasValidBundleAsync(descriptor, cancellationToken).ConfigureAwait(false))
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
        var normalized = AssetBundleValidation.NormalizeRelativePath(version, "asset bundle version");
        if (normalized.Contains('/'))
            throw new InvalidDataException("Asset bundle version must be a single path segment.");
        return normalized;
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

    private void RemoveLoadedBundle(string hash, Task<LoadedAssetBundle> task)
    {
        lock (_bundleCacheGate)
            if (_loadedBundles.TryGetValue(hash, out var current) && ReferenceEquals(current, task))
                _loadedBundles.Remove(hash);
    }

    private void CreateCacheLayout()
    {
        Directory.CreateDirectory(_cacheRoot);
        Directory.CreateDirectory(ObjectsDirectory);
        Directory.CreateDirectory(CatalogsDirectory);
        Directory.CreateDirectory(StagingDirectory);
    }

    private void CleanupStagingFiles()
    {
        if (!Directory.Exists(StagingDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(StagingDirectory, "*.part"))
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string GetObjectPath(AssetBundleDescriptor descriptor) =>
        Path.Combine(ObjectsDirectory, descriptor.Sha256.ToLowerInvariant() + ".bassetbundle");

    private string GetCatalogCachePath(string hash) =>
        Path.Combine(CatalogsDirectory, hash.ToLowerInvariant() + ".json");

    private async Task PersistCatalogAsync(RuntimeState state, CancellationToken cancellationToken) =>
        await AtomicWriteAsync(GetCatalogCachePath(state.Version.CatalogSha256),
            state.CatalogBytes, cancellationToken).ConfigureAwait(false);

    private static async Task WritePointerAsync(
        string path,
        AssetBundleVersion version,
        CancellationToken cancellationToken) =>
        await AtomicWriteAsync(path, AssetBundleCatalogSerializer.SerializeVersion(version), cancellationToken)
            .ConfigureAwait(false);

    private static async Task AtomicWriteAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
                        throw new InvalidDataException($"Asset bundle path '{path}' has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<byte[]> ReadLimitedFileAsync(
        string path,
        long maximumSize,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Asset bundle metadata file was not found.", path);
        if (file.Length > maximumSize)
            throw new InvalidDataException($"Asset bundle metadata '{path}' exceeds its size limit.");
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
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

    private static string ResolveInside(string root, string relativePath)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalized = AssetBundleValidation.NormalizeRelativePath(relativePath, "asset bundle path");
        var path = Path.GetFullPath(Path.Combine(fullRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset bundle path escapes its root: '{relativePath}'.");
        return path;
    }

    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(2_000, 100 * (1 << Math.Min(attempt, 4))));

    private static bool IsRetryable(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        exception is HttpRequestException or IOException or TimeoutException or TaskCanceledException;

    private static bool IsRecoverableContentFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException;

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
        AssetBundleCatalogSerializer.DeserializeCatalog(
            AssetBundleCatalogSerializer.SerializeCatalog(catalog));

    private static AssetBundleVersion CloneVersion(AssetBundleVersion version) =>
        AssetBundleCatalogSerializer.DeserializeVersion(
            AssetBundleCatalogSerializer.SerializeVersion(version));

    private void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("AssetBundleManager.InitializeAsync must complete first.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AssetBundleManager));
    }

    private CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

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
            Index = new AssetBundleCatalogIndex(catalog, cloneCatalog: false);
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
