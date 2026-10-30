using BEngine.AssetBundles;
using BEngine.Content;
using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.Player;

internal sealed class PlayerBuiltInResourceProvider : IAsyncResourceProvider, IResourceAssetProvider, IResourceObjectProvider, IDisposable
{
    private readonly BuiltInResourceArchive _archive;
    private readonly PathUtility _paths;
    private readonly ArchiveAssetBundleManager _manager;
    private readonly Dictionary<(string Address, Type Type), BObject> _decodedAssets = [];
    private bool _registered;
    private bool _disposed;

    internal PlayerBuiltInResourceProvider(
        string archivePath,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        ContentEncryptionKey? encryptionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        _paths = pathService ?? RuntimeServiceAccess.PathService;
        ArchivePath = _paths.GetFullPath(archivePath);
        _archive = BuiltInResourceArchive.Open(ArchivePath, fileSystem, _paths, encryptionKey);
        _manager = new ArchiveAssetBundleManager(_archive);
    }

    internal PlayerBuiltInResourceProvider(BuiltInResourceArchive archive, PathUtility paths)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        ArchivePath = archive.SourcePath;
        _manager = new ArchiveAssetBundleManager(archive);
    }

    private PlayerBuiltInResourceProvider(PlayerBuiltInResourceProvider source)
    {
        ArchivePath = source.ArchivePath;
        _archive = source._archive;
        _paths = source._paths;
        _manager = new ArchiveAssetBundleManager(_archive, source._manager.ActiveCatalog);
    }

    // Metadata and payload storage are immutable. Domain-owned decoded objects
    // must remain local so disposing AOT cannot invalidate game-domain assets.
    internal PlayerBuiltInResourceProvider CreateView()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PlayerBuiltInResourceProvider(this);
    }

    internal string ArchivePath { get; }
    internal IReadOnlyList<BuiltInResourceArchiveEntry> Entries => _archive.Entries;

    internal bool ContainsAddress(string address) => _archive.TryGetEntry(address, out _);

    internal IReadOnlyList<string> EnumerateAddresses(string prefix = "") =>
        _archive.EnumerateAddresses(prefix);

    internal byte[] ReadBytes(string address) => _archive.ReadBytes(address);

    internal BValueTask<byte[]> ReadBytesAsync(string address, BCancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _archive.ReadBytesAsync(address, cancellationToken);
    }

    internal void Activate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_registered) return;
        Resources.RegisterResourceProvider(this);
        _registered = true;
    }

    internal void Deactivate()
    {
        if (!_registered) return;
        Resources.UnregisterResourceProvider(this);
        _registered = false;
    }

    public bool TryLoad(string path, string folderName, out ResourceContent content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var address = ResolveAddress(path, folderName);
        if (address is null)
        {
            content = null!;
            return false;
        }

        content = new ResourceContent(address, _archive.ReadBytes(address));
        return true;
    }

    public bool TryLoadAsset(string path, string folderName, Type assetType, out BAsset asset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(assetType);
        asset = null!;
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract) return false;
        var address = ResolveAddress(path, folderName);
        if (address is null) return false;
        var key = (address.ToUpperInvariant(), assetType);
        if (_decodedAssets.TryGetValue(key, out var cached) && cached is BAsset typed && !cached.isDestroyed)
        {
            asset = typed;
            return true;
        }
        asset = AssetBundleAssetLoader.LoadAsset(_manager, address, assetType)!;
        if (asset is null) return false;
        _decodedAssets[key] = asset;
        return true;
    }

    public bool TryLoadObject(string path, string folderName, Type objectType, out BObject value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(objectType);
        value = null!;
        if (objectType != typeof(Sprite)) return false;
        var address = ResolveAddress(path, folderName, allowSyntheticSprite: true);
        if (address is null) return false;
        var cacheAddress = address;
        if (AssetBundleValidation.TryParseSubAssetAddress(address, out var ownerGuid, out var localIdentifier) &&
            localIdentifier == 21300000)
            cacheAddress = _archive.Entries.FirstOrDefault(entry => entry.LocalIdentifier == 0 &&
                (entry.OwnerGuid == Guid.Empty ? entry.Guid : entry.OwnerGuid) == ownerGuid)?.Address ?? address;
        var key = (cacheAddress.ToUpperInvariant(), objectType);
        if (_decodedAssets.TryGetValue(key, out var cached) && !cached.isDestroyed)
        {
            value = cached;
            return true;
        }
        value = AssetBundleAssetLoader.LoadSprite(_manager, address)!;
        if (value is null) return false;
        _decodedAssets[key] = value;
        return true;
    }

    public async BTask<ResourceLoadResult> LoadAsync(string path, string folderName, Type type, AsyncLoadContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        context.cancellationToken.ThrowIfCancellationRequested();
        if (type != typeof(byte[]) && type != typeof(string) &&
            (!typeof(BAsset).IsAssignableFrom(type) || type.IsAbstract)) return default;
        var address = ResolveAddress(path, folderName);
        if (address is null) return default;
        var key = (address.ToUpperInvariant(), type);
        if (_decodedAssets.TryGetValue(key, out var cached) && !cached.isDestroyed)
            return new(true, cached);
        if (!typeof(BAsset).IsAssignableFrom(type))
        {
            using var payload = await _manager.LoadBytesAsync(address, context.cancellationToken);
            ObjectDisposedException.ThrowIf(_disposed, this);
            object? content = null;
            await context.RunSteps(() =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                content = type == typeof(byte[]) ? payload.Asset : TextAsset.Decode(payload.Asset);
                return CooperativeWorkStatus.Complete;
            });
            return new(true, content);
        }
        var asset = await AssetBundleAssetLoader.LoadAssetAsync(_manager, address, type, context);
        if (_disposed)
        {
            if (asset is not null) BObject.DestroyImmediate(asset);
            throw new ObjectDisposedException(nameof(PlayerBuiltInResourceProvider));
        }
        if (asset is not null)
        {
            if (_decodedAssets.TryGetValue(key, out cached) && !cached.isDestroyed)
            {
                if (!ReferenceEquals(asset, cached)) BObject.DestroyImmediate(asset);
                return new(true, cached);
            }
            _decodedAssets[key] = asset;
        }
        return new(true, asset);
    }

    public IEnumerable<string> Enumerate(string path, string folderName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var requested = Normalize(path);
        if (requested.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            requested.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return _archive.EnumerateAddresses(requested);

        return ResourceEntries(folderName)
            .Where(item => requested.Length == 0 || item.ResourcePath.Equals(
                    requested, StringComparison.OrdinalIgnoreCase) ||
                item.ResourcePath.StartsWith(requested + "/", StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.ResourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string? ResolveAddress(string path, string folderName, bool allowSyntheticSprite = false)
    {
        var requested = Normalize(path);
        if (requested.StartsWith(AssetBundleResourceProvider.VirtualPathPrefix, StringComparison.OrdinalIgnoreCase))
            requested = requested[AssetBundleResourceProvider.VirtualPathPrefix.Length..];
        if (AssetBundleValidation.TryParseSubAssetAddress(requested, out var ownerGuid, out var localIdentifier))
        {
            var subAddress = AssetBundleValidation.CreateSubAssetAddress(ownerGuid, localIdentifier);
            if (_archive.TryGetEntry(subAddress, out var subAsset)) return subAsset.Address;
            if (allowSyntheticSprite && localIdentifier == 21300000 && _archive.Entries.Any(entry =>
                    entry.LocalIdentifier == 0 && (entry.OwnerGuid == Guid.Empty ? entry.Guid : entry.OwnerGuid) == ownerGuid))
                return subAddress;
            return null;
        }
        if (requested.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(requested.AsSpan(5), out var guid) || guid == Guid.Empty) return null;
            var entries = _archive.Entries.Where(entry => entry.LocalIdentifier == 0 &&
                    (entry.OwnerGuid == Guid.Empty ? entry.Guid : entry.OwnerGuid) == guid)
                .Take(2).ToArray();
            return entries.Length switch
            {
                0 => null,
                1 => entries[0].Address,
                _ => throw new InvalidDataException($"Asset GUID '{guid:N}' is duplicated in the built-in resource archive.")
            };
        }
        if (requested.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            _archive.TryGetEntry(AssetBundleValidation.NormalizeAddress(requested), out var direct))
            return direct.Address;

        var matches = ResourceEntries(folderName)
            .Where(item => item.ResourcePath.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                           MatchesWithoutExtension(item.ResourcePath, requested))
            .OrderBy(static item => item.Address, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0].Address,
            _ => throw new InvalidOperationException(
                $"Resource path '{path}' is ambiguous in the built-in AOT resource archive.")
        };
    }

    private IEnumerable<(string Address, string ResourcePath)> ResourceEntries(string folderName)
    {
        var marker = "/" + NormalizeFolder(folderName) + "/";
        foreach (var entry in _archive.Entries)
        {
            var index = entry.Address.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var relative = entry.Address[(index + marker.Length)..];
            if (relative.Length > 0) yield return (entry.Address, relative);
        }
    }

    private bool MatchesWithoutExtension(string candidate, string requested)
    {
        if (_paths.GetExtension(requested).Length != 0 || candidate.Length <= requested.Length + 1 ||
            !candidate.StartsWith(requested, StringComparison.OrdinalIgnoreCase) ||
            candidate[requested.Length] != '.') return false;
        return !candidate.AsSpan(requested.Length + 1).Contains('/');
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private static string NormalizeFolder(string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        var normalized = Normalize(folderName);
        if (normalized.Contains('/'))
            throw new ArgumentException(
                "A resource folder name cannot contain path separators.", nameof(folderName));
        return normalized;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var assets = _decodedAssets.Values.Distinct().ToArray();
        _decodedAssets.Clear();
        _manager.Dispose();
        if (_registered)
        {
            Resources.UnregisterResourceProvider(this);
            _registered = false;
        }
        foreach (var asset in assets) BObject.DestroyImmediate(asset);
    }

    /// <summary>Reuses the same codecs and import settings as AB without creating a second payload/cache.</summary>
    private sealed class ArchiveAssetBundleManager : IAssetBundleManager
    {
        private readonly BuiltInResourceArchive _archive;
        private bool _disposed;

        internal ArchiveAssetBundleManager(BuiltInResourceArchive archive, AssetBundleCatalog? catalog = null)
        {
            _archive = archive;
            ActiveCatalog = catalog ?? new AssetBundleCatalog
            {
                PackageName = "builtin", Version = "builtin",
                Assets = archive.Entries.Select(static entry => new AssetBundleAsset
                {
                    Address = entry.Address, Guid = entry.Guid, OwnerGuid = entry.OwnerGuid,
                    LocalIdentifier = entry.LocalIdentifier, AssetType = entry.AssetType,
                    Importer = entry.Importer, ImporterSettings = new(entry.ImporterSettings),
                    Size = entry.Size, Sha256 = entry.Sha256, Entry = entry.Address
                }).ToList()
            };
        }

        public AssetBundleCatalog ActiveCatalog { get; }
        public AssetBundleVersion? ActiveVersion => null;
        public bool IsInitialized => !_disposed;
        public BValueTask InitializeAsync(BCancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            return BValueTask.CompletedTask;
        }
        public async BValueTask<AssetBundleHandle<byte[]>> LoadBytesAsync(string address, BCancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var bytes = await _archive.ReadBytesAsync(address, cancellationToken);
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new AssetBundleHandle<byte[]>(address, bytes, static () => { });
        }
        public async BValueTask<AssetBundleHandle<string>> LoadTextAsync(string address, BCancellationToken cancellationToken = default)
        {
            using var bytes = await LoadBytesAsync(address, cancellationToken);
            return new AssetBundleHandle<string>(address, TextAsset.Decode(bytes.Asset), static () => { });
        }
        public bool TryLoadBytes(string address, out byte[] bytes)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bytes = null!;
            if (!_archive.TryGetEntry(address, out var entry)) return false;
            bytes = _archive.ReadBytes(entry.Address);
            return true;
        }
        public IReadOnlyList<string> EnumerateAddresses(string prefix = "") => _archive.EnumerateAddresses(prefix);
        public int UnloadUnused() => 0;
        public BValueTask<int> CleanupAsync(BCancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BValueTask<int>.FromResult(0);
        }
        public BValueTask<AssetBundleUpdatePlan> CheckForUpdatesAsync(BCancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Built-in resources are updated with the Player.");
        public BValueTask<AssetBundleUpdateResult> ApplyUpdateAsync(AssetBundleUpdatePlan plan,
            IProgress<AssetBundleUpdateProgress>? progress = null, BCancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Built-in resources are updated with the Player.");
        public BValueTask<bool> RollbackAsync(BCancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Built-in resources are updated with the Player.");
        public void Dispose() => _disposed = true;
        public BValueTask DisposeAsync() { Dispose(); return BValueTask.CompletedTask; }
    }
}
