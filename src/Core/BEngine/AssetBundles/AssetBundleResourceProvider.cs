namespace BEngine.AssetBundles;

/// <summary>Exposes an active asset bundle catalog through the Resources API.</summary>
public sealed class AssetBundleResourceProvider : IResourceProvider, IResourceAssetProvider, IResourceObjectProvider
{
    internal const string VirtualPathPrefix = "@bundle/";
    private readonly IAssetBundleManager _manager;
    private readonly Lock _cacheGate = new();
    private readonly Dictionary<ProviderObjectCacheKey, WeakReference<BObject>> _objectCache = [];
    private string _cacheRevision = string.Empty;

    public AssetBundleResourceProvider(IAssetBundleManager manager) =>
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

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
            () => AssetBundleAssetLoader.LoadAsset(_manager, address, assetType))!;
        return asset is not null;
    }

    public bool TryLoadObject(string path, string folderName, Type objectType, out BObject value)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        value = null!;
        if (!_manager.IsInitialized || objectType != typeof(Sprite)) return false;
        var requested = Normalize(path);
        if (requested.StartsWith(VirtualPathPrefix, StringComparison.OrdinalIgnoreCase))
            requested = requested[VirtualPathPrefix.Length..];
        var address = AssetBundleValidation.TryParseSubAssetAddress(requested, out _, out _)
            ? requested
            : ResolveAddress(path, folderName);
        if (address is null) return false;
        value = LoadCached(address, objectType,
            () => AssetBundleAssetLoader.LoadSprite(_manager, address))!;
        return value is not null;
    }

    private T? LoadCached<T>(string address, Type objectType, Func<T?> loader) where T : BObject
    {
        var revision = CurrentRevision();
        var key = new ProviderObjectCacheKey(
            AssetBundleValidation.NormalizeAddress(address).ToUpperInvariant(), objectType);
        lock (_cacheGate)
        {
            EnsureRevision(revision);
            if (_objectCache.TryGetValue(key, out var reference) &&
                reference.TryGetTarget(out var cached) && cached is T typed) return typed;
        }

        var loaded = loader();
        if (loaded is null) return null;
        var currentRevision = CurrentRevision();
        lock (_cacheGate)
        {
            EnsureRevision(currentRevision);
            if (!currentRevision.Equals(revision, StringComparison.Ordinal)) return loaded;
            if (_objectCache.TryGetValue(key, out var reference) &&
                reference.TryGetTarget(out var cached) && cached is T typed) return typed;
            _objectCache[key] = new WeakReference<BObject>(loaded);
            return loaded;
        }
    }

    private string CurrentRevision()
    {
        if (_manager.ActiveVersion is { } version)
            return $"{version.PackageName}\0{version.Version}\0{version.CatalogSha256}";
        return _manager.ActiveCatalog is { } catalog
            ? $"{catalog.PackageName}\0{catalog.Version}\0" +
              System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(catalog)
            : "uninitialized";
    }

    private void EnsureRevision(string revision)
    {
        if (_cacheRevision.Equals(revision, StringComparison.Ordinal)) return;
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
        if (requested.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            var direct = AssetBundleValidation.NormalizeAddress(requested);
            if (_manager.EnumerateAddresses(direct).Any(address =>
                    address.Equals(direct, StringComparison.OrdinalIgnoreCase))) return direct;
        }
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
        if (Path.HasExtension(requested) || candidate.Length <= requested.Length + 1 ||
            !candidate.StartsWith(requested, StringComparison.OrdinalIgnoreCase) ||
            candidate[requested.Length] != '.') return false;
        return !candidate.AsSpan(requested.Length + 1).Contains('/');
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
}
