namespace BEngine.AssetBundles;

/// <summary>Exposes an active asset bundle catalog through the Resources API.</summary>
public sealed class AssetBundleResourceProvider : IAsyncResourceProvider, IAsyncResourceReferenceProvider, IResourceAssetProvider, IResourceObjectProvider
{
    internal const string VirtualPathPrefix = "@bundle/";
    private readonly IAssetBundleManager _manager;
    private readonly Dictionary<ProviderObjectCacheKey, WeakReference<BObject>> _objectCache = [];
    private AssetBundleResourceRevision _cacheRevision;
    private const int MaximumAddressCacheEntries = 256;
    private readonly Dictionary<AddressCacheKey, string?> _addressCache = new(AddressCacheKeyComparer.Instance);
    private readonly Queue<AddressCacheKey> _addressCacheOrder = new();
    private AssetBundleResourceRevision _addressCacheRevision;

    public AssetBundleResourceProvider(IAssetBundleManager manager) =>
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    public bool ContainsReference(string path, string folderName) =>
        _manager.IsInitialized && ResolveReferencePayloadAddress(path, folderName) is not null;

    public async BTask<bool> PrepareReferenceAsync(string path, string folderName, AsyncLoadContext context)
    {
        context.cancellationToken.ThrowIfCancellationRequested();
        if (!_manager.IsInitialized) return false;
        var address = ResolveReferencePayloadAddress(path, folderName);
        if (address is null) return false;
        using var payload = await _manager.LoadBytesAsync(address, context.cancellationToken);
        context.ReportProgress(1);
        return true;
    }

    private string? ResolveReferencePayloadAddress(string path, string folderName)
    {
        var requested = Normalize(path);
        if (requested.StartsWith(VirtualPathPrefix, StringComparison.OrdinalIgnoreCase)) requested = requested[VirtualPathPrefix.Length..];
        var address = AssetBundleValidation.TryParseSubAssetAddress(requested, out _, out _)
            ? requested : ResolveAddress(path, folderName);
        return address is null ? null : AssetBundleAssetLoader.ResolveReferencePayloadAddress(_manager, address);
    }

    private string? ResolveObjectAddress(string path, string folderName, Type objectType)
    {
        var requested = Normalize(path);
        if (requested.StartsWith(VirtualPathPrefix, StringComparison.OrdinalIgnoreCase)) requested = requested[VirtualPathPrefix.Length..];
        var address = AssetBundleValidation.TryParseSubAssetAddress(requested, out _, out _)
            ? requested : ResolveAddress(path, folderName);
        return address is null ? null : AssetBundleAssetLoader.ResolveObjectAddress(_manager, address, objectType);
    }

    public bool TryLoad(string path, string folderName, out ResourceContent content)
    {
        content = null!;
        if (!_manager.IsInitialized) return false;
        var address = ResolveAddress(path, folderName);
        if (address is null || !_manager.TryLoadBytes(address, out var bytes)) return false;
        content = new ResourceContent(address, bytes);
        return true;
    }

    public bool TryLoadAsset(string path, string folderName, Type assetType, out BAsset asset)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        asset = null!;
        if (!_manager.IsInitialized) return false;
        var address = ResolveAddress(path, folderName);
        if (address is null) return false;
        asset = LoadCached(address, assetType,
            static (manager, address, type) => AssetBundleAssetLoader.LoadAsset(manager, address, type))!;
        return asset is not null;
    }

    public async BTask<ResourceLoadResult> LoadAsync(string path, string folderName, Type type, AsyncLoadContext context)
    {
        context.cancellationToken.ThrowIfCancellationRequested();
        if (!_manager.IsInitialized || (type != typeof(byte[]) && type != typeof(string) &&
            !typeof(BObject).IsAssignableFrom(type))) return default;
        var address = typeof(BObject).IsAssignableFrom(type) ? ResolveObjectAddress(path, folderName, type) : ResolveAddress(path, folderName);
        if (address is null) return default;
        var revision = CurrentRevision();
        EnsureRevision(revision);
        var key = new ProviderObjectCacheKey(AssetBundleValidation.NormalizeAddress(address).ToUpperInvariant(), type);
        if (_objectCache.TryGetValue(key, out var reference) && reference.TryGetTarget(out var existing) && !existing.isDestroyed)
            return new(true, existing);
        if (!typeof(BObject).IsAssignableFrom(type))
        {
            using var payload = await _manager.LoadBytesAsync(address, context.cancellationToken);
            object? content = null;
            await context.RunSteps(() =>
            { content = type == typeof(byte[]) ? payload.Asset : RuntimeAssetDecoding.DecodeText(payload.Asset); return CooperativeWorkStatus.Complete; });
            return new(true, content);
        }
        var loaded = await AssetBundleAssetLoader.LoadObjectAsync(_manager, address, type, context);
        var current = CurrentRevision();
        EnsureRevision(current);
        if (loaded is not null && current.Equals(revision))
        {
            // Two async requests may finish in either order. Share one object identity.
            if (_objectCache.TryGetValue(key, out reference) && reference.TryGetTarget(out existing) && !existing.isDestroyed)
            {
                if (!ReferenceEquals(loaded, existing)) BObject.DestroyImmediate(loaded);
                return new(true, existing);
            }
            _objectCache[key] = new(loaded);
        }
        return new(true, loaded);
    }

    public bool TryLoadObject(string path, string folderName, Type objectType, out BObject value)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        value = null!;
        if (!_manager.IsInitialized || !typeof(BObject).IsAssignableFrom(objectType) || objectType.IsAbstract) return false;
        var address = ResolveObjectAddress(path, folderName, objectType);
        if (address is null) return false;
        value = LoadCached(address, objectType,
            static (manager, address, type) => AssetBundleAssetLoader.LoadObject(manager, address, type))!;
        return value is not null;
    }

    private T? LoadCached<T>(string address, Type objectType,
        Func<IAssetBundleManager, string, Type, T?> loader) where T : BObject
    {
        var revision = CurrentRevision();
        var key = new ProviderObjectCacheKey(
            AssetBundleValidation.NormalizeAddress(address).ToUpperInvariant(), objectType);
        EnsureRevision(revision);
        if (_objectCache.TryGetValue(key, out var reference) &&
            reference.TryGetTarget(out var cached) && cached is T typed && !cached.isDestroyed) return typed;

        var loaded = loader(_manager, address, objectType);
        if (loaded is null) return null;
        var currentRevision = CurrentRevision();
        EnsureRevision(currentRevision);
        if (!currentRevision.Equals(revision)) return loaded;
        if (_objectCache.TryGetValue(key, out reference) &&
            reference.TryGetTarget(out cached) && cached is T typedAfterLoad && !cached.isDestroyed)
        {
            if (!ReferenceEquals(loaded, typedAfterLoad)) BObject.DestroyImmediate(loaded);
            return typedAfterLoad;
        }
        _objectCache[key] = new WeakReference<BObject>(loaded);
        return loaded;
    }

    private AssetBundleResourceRevision CurrentRevision()
    {
        // The built-in managers own their active metadata. Reading three
        // immutable strings avoids a YAML round trip for every cache lookup.
        if (_manager is AssetBundleManager runtime) return runtime.ResourceRevision;
        if (_manager is VirtualAssetBundleManager snapshot) return snapshot.ResourceRevision;
        // Third-party managers retain their public getter semantics, including
        // catalog identity invalidation when they do not expose a version.
        if (_manager.ActiveVersion is { } version)
            return AssetBundleResourceRevision.FromVersion(version);
        return AssetBundleResourceRevision.FromCatalog(_manager.ActiveCatalog);
    }

    private void EnsureRevision(AssetBundleResourceRevision revision)
    {
        if (_cacheRevision.Equals(revision)) return;
        _objectCache.Clear();
        _cacheRevision = revision;
    }

    public IEnumerable<string> Enumerate(string path, string folderName)
    {
        if (!_manager.IsInitialized) return [];
        var prefix = Normalize(path);
        return ResourceEntries(folderName)
            .Where(item => prefix.Length == 0 || item.ResourcePath.Equals(prefix,
                StringComparison.OrdinalIgnoreCase) || item.ResourcePath.StartsWith(prefix + "/",
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ResourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string? ResolveAddress(string path, string folderName)
    {
        var requested = Normalize(path);
        if (requested.StartsWith(VirtualPathPrefix, StringComparison.OrdinalIgnoreCase))
            requested = requested[VirtualPathPrefix.Length..];
        if (AssetBundleValidation.TryParseSubAssetAddress(requested, out var ownerGuid, out var localIdentifier))
        {
            if (_manager is AssetBundleManager runtime)
                return runtime.TryGetActiveAsset(AssetBundleValidation.CreateSubAssetAddress(ownerGuid, localIdentifier), out var indexedRuntime)
                    ? indexedRuntime.Address : null;
            if (_manager is VirtualAssetBundleManager snapshot)
                return snapshot.TryGetActiveAsset(AssetBundleValidation.CreateSubAssetAddress(ownerGuid, localIdentifier), out var indexed)
                    ? indexed.Address : null;
            var matches = _manager.ActiveCatalog?.Assets.Where(asset =>
                    asset.OwnerGuid == ownerGuid && asset.LocalIdentifier == localIdentifier)
                .Take(2)
                .ToArray() ?? [];
            return matches.Length switch
            {
                0 => null,
                1 => matches[0].Address,
                _ => throw new InvalidDataException(
                    $"Sub-asset identity '{ownerGuid:N}/{localIdentifier}' is duplicated in the active catalog.")
            };
        }
        if (requested.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(requested.AsSpan(5), out var guid) || guid == Guid.Empty) return null;
            if (_manager is AssetBundleManager runtime)
                return runtime.TryGetActiveMainAsset(guid, out var indexed) ? indexed.Address : null;
            if (_manager is VirtualAssetBundleManager snapshot)
                return snapshot.TryGetActiveMainAsset(guid, out var indexedSnapshot) ? indexedSnapshot.Address : null;
            var matches = _manager.ActiveCatalog?.Assets.Where(asset =>
                    asset.LocalIdentifier == 0 &&
                    (asset.OwnerGuid == Guid.Empty ? asset.Guid : asset.OwnerGuid) == guid)
                .Take(2).ToArray() ?? [];
            return matches.Length switch
            {
                0 => null,
                1 => matches[0].Address,
                _ => throw new InvalidDataException($"Asset GUID '{guid:N}' is duplicated in the active catalog.")
            };
        }
        if (requested.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            var direct = AssetBundleValidation.NormalizeAddress(requested);
            if (_manager is AssetBundleManager indexedManager)
            {
                if (indexedManager.TryGetActiveAsset(direct, out _)) return direct;
            }
            else if (_manager is VirtualAssetBundleManager snapshot)
            {
                if (snapshot.TryGetActiveAsset(direct, out _)) return direct;
            }
            else if (_manager.EnumerateAddresses(direct).Any(address =>
                         address.Equals(direct, StringComparison.OrdinalIgnoreCase))) return direct;
        }
        // Built-in managers own immutable catalogs and publish a new revision on
        // install/update/rollback. Custom managers may mutate their catalog or
        // address list without changing a version, so keep their original scan.
        var stableCatalog = _manager is AssetBundleManager or VirtualAssetBundleManager;
        var folder = stableCatalog ? NormalizeFolder(folderName) : folderName;
        var cacheable = stableCatalog && requested.Length <= 1024 && folder.Length <= 128;
        var key = new AddressCacheKey(requested, folder);
        if (cacheable)
        {
            var revision = CurrentRevision();
            if (!_addressCacheRevision.Equals(revision))
            {
                _addressCache.Clear();
                _addressCacheOrder.Clear();
                _addressCacheRevision = revision;
            }
            if (_addressCache.TryGetValue(key, out var cachedAddress)) return cachedAddress;
        }
        var resolved = ScanResourceAddress(path, requested, folderName);
        if (cacheable)
        {
            // Evict one entry at a time; never clear all hot paths on capacity.
            // Request and folder lengths are bounded as well as the entry count.
            if (_addressCache.Count == MaximumAddressCacheEntries)
                _addressCache.Remove(_addressCacheOrder.Dequeue());
            _addressCache.Add(key, resolved);
            _addressCacheOrder.Enqueue(key);
        }
        return resolved;
    }

    private string? ScanResourceAddress(string path, string requested, string folderName)
    {
        var candidates = ResourceEntries(folderName).Where(item =>
                item.ResourcePath.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                MatchesWithoutExtension(item.ResourcePath, requested))
            .OrderBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0].Address,
            _ => throw new InvalidOperationException(
                $"Resource path '{path}' is ambiguous in the active asset bundle catalog.")
        };
    }

    private IEnumerable<(string Address, string ResourcePath)> ResourceEntries(string folderName)
    {
        var marker = "/" + NormalizeFolder(folderName) + "/";
        foreach (var address in _manager.EnumerateAddresses())
        {
            var index = address.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var relative = address[(index + marker.Length)..];
            if (relative.Length > 0) yield return (address, relative);
        }
    }

    private static bool MatchesWithoutExtension(string candidate, string requested)
    {
        if (HasAssetExtension(requested) || candidate.Length <= requested.Length + 1 ||
            !candidate.StartsWith(requested, StringComparison.OrdinalIgnoreCase) ||
            candidate[requested.Length] != '.') return false;
        return !candidate.AsSpan(requested.Length + 1).Contains('/');
    }

    private static bool HasAssetExtension(string path)
    {
        var fileNameStart = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1;
        return path.LastIndexOf('.') >= fileNameStart;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    internal static string ToVirtualPath(string address) =>
        VirtualPathPrefix + AssetBundleValidation.NormalizeAddress(address);

    private static string NormalizeFolder(string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        var normalized = Normalize(folderName);
        if (normalized.Contains('/'))
            throw new ArgumentException("A resource folder name cannot contain path separators.", nameof(folderName));
        return normalized;
    }

    private readonly record struct ProviderObjectCacheKey(string Address, Type ObjectType);

    private readonly record struct AddressCacheKey(string Requested, string Folder);

    private sealed class AddressCacheKeyComparer : IEqualityComparer<AddressCacheKey>
    {
        internal static readonly AddressCacheKeyComparer Instance = new();
        public bool Equals(AddressCacheKey left, AddressCacheKey right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left.Requested, right.Requested) &&
            StringComparer.OrdinalIgnoreCase.Equals(left.Folder, right.Folder);
        public int GetHashCode(AddressCacheKey key) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.Requested),
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.Folder));
    }
}
