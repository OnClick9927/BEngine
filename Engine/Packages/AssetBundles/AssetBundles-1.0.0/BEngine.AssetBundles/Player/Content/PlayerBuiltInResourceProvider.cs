using BEngine.AssetBundles;
using BEngine.Content;
using BEngine.Player.Content;
using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.Player;

internal sealed class PlayerBuiltInResourceProvider : IAsyncResourceProvider, IResourceReferenceOwner, IResourceAssetProvider, IResourceObjectProvider, IDisposable
{
    private readonly BuiltInResourceArchive _archive;
    private readonly PathUtility _paths;
    private readonly ArchiveAssetBundleManager _manager;
    private readonly ArchiveAddressIndex _addressIndex;
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
        fileSystem ??= RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
        _paths = pathService ?? RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>();
        ArchivePath = _paths.GetFullPath(archivePath);
        _archive = BuiltInResourceArchive.Open(ArchivePath, fileSystem, _paths, encryptionKey);
        _manager = new ArchiveAssetBundleManager(_archive);
        _addressIndex = new ArchiveAddressIndex(_archive, _paths);
    }

    internal PlayerBuiltInResourceProvider(BuiltInResourceArchive archive, PathUtility paths)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        ArchivePath = archive.SourcePath;
        _manager = new ArchiveAssetBundleManager(archive);
        _addressIndex = new ArchiveAddressIndex(archive, paths);
    }

    private PlayerBuiltInResourceProvider(PlayerBuiltInResourceProvider source)
    {
        ArchivePath = source.ArchivePath;
        _archive = source._archive;
        _paths = source._paths;
        _manager = new ArchiveAssetBundleManager(_archive, source._manager.ActiveCatalog);
        _addressIndex = source._addressIndex;
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

    internal bool TryGetCanonicalAddress(string address, out string canonical)
    {
        if (_archive.TryGetEntry(address, out var entry))
        {
            canonical = entry.Address;
            return true;
        }
        canonical = string.Empty;
        return false;
    }

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

    public bool ContainsReference(string path, string folderName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var requested = Normalize(path);
        if (requested.StartsWith(AssetBundleResourceProvider.VirtualPathPrefix, StringComparison.OrdinalIgnoreCase))
            requested = requested[AssetBundleResourceProvider.VirtualPathPrefix.Length..];
        var address = AssetBundleValidation.TryParseSubAssetAddress(requested, out _, out _)
            ? requested : ResolveAddress(path, folderName);
        return address is not null && AssetBundleAssetLoader.ResolveReferencePayloadAddress(_manager, address) is not null;
    }

    public bool TryLoadAsset(string path, string folderName, Type assetType, out BAsset asset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(assetType);
        asset = null!;
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract) return false;
        var address = ResolveAddress(path, folderName);
        if (address is null) return false;
        // Resolution returns the archive's canonical address, including its casing.
        var key = (address, assetType);
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
        if (!typeof(BObject).IsAssignableFrom(objectType) || objectType.IsAbstract) return false;
        var address = ResolveObjectAddress(path, folderName, objectType);
        if (address is null) return false;
        var key = (address, objectType);
        if (_decodedAssets.TryGetValue(key, out var cached) && !cached.isDestroyed)
        {
            value = cached;
            return true;
        }
        value = AssetBundleAssetLoader.LoadObject(_manager, address, objectType)!;
        if (value is null) return false;
        _decodedAssets[key] = value;
        return true;
    }

    public async BTask<ResourceLoadResult> LoadAsync(string path, string folderName, Type type, AsyncLoadContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        context.cancellationToken.ThrowIfCancellationRequested();
        if (type != typeof(byte[]) && type != typeof(string) &&
            (!typeof(BObject).IsAssignableFrom(type) || type.IsAbstract)) return default;
        var address = typeof(BObject).IsAssignableFrom(type) ? ResolveObjectAddress(path, folderName, type) : ResolveAddress(path, folderName);
        if (address is null) return default;
        var key = (address, type);
        if (_decodedAssets.TryGetValue(key, out var cached) && !cached.isDestroyed)
            return new(true, cached);
        if (!typeof(BObject).IsAssignableFrom(type))
        {
            using var payload = await _manager.LoadBytesAsync(address, context.cancellationToken);
            ObjectDisposedException.ThrowIf(_disposed, this);
            object? content = null;
            await context.RunSteps(() =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                content = type == typeof(byte[]) ? payload.Asset : RuntimeAssetDecoding.DecodeText(payload.Asset);
                return CooperativeWorkStatus.Complete;
            });
            return new(true, content);
        }
        var asset = await AssetBundleAssetLoader.LoadObjectAsync(_manager, address, type, context);
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

        var prefix = requested + "/";
        return _addressIndex.GetResources(NormalizeFolder(folderName)).Entries
            .Where(item => requested.Length == 0 || item.ResourcePath.Equals(
                    requested, StringComparison.OrdinalIgnoreCase) ||
                item.ResourcePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.ResourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string? ResolveObjectAddress(string path, string folderName, Type objectType)
    {
        var requested = Normalize(path);
        if (requested.StartsWith(AssetBundleResourceProvider.VirtualPathPrefix, StringComparison.OrdinalIgnoreCase))
            requested = requested[AssetBundleResourceProvider.VirtualPathPrefix.Length..];
        var address = AssetBundleValidation.TryParseSubAssetAddress(requested, out _, out _)
            ? requested : ResolveAddress(path, folderName);
        return address is null ? null : AssetBundleAssetLoader.ResolveObjectAddress(_manager, address, objectType);
    }

    private string? ResolveAddress(string path, string folderName)
    {
        var requested = Normalize(path);
        if (requested.StartsWith(AssetBundleResourceProvider.VirtualPathPrefix, StringComparison.OrdinalIgnoreCase))
            requested = requested[AssetBundleResourceProvider.VirtualPathPrefix.Length..];
        if (AssetBundleValidation.TryParseSubAssetAddress(requested, out var ownerGuid, out var localIdentifier))
        {
            var subAddress = AssetBundleValidation.CreateSubAssetAddress(ownerGuid, localIdentifier);
            if (_archive.TryGetEntry(subAddress, out var subAsset)) return subAsset.Address;
            return null;
        }
        if (requested.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(requested.AsSpan(5), out var guid) || guid == Guid.Empty) return null;
            var match = _addressIndex.GetPrimaryAddress(guid);
            return match.Count switch
            {
                0 => null,
                1 => match.Address,
                _ => throw new InvalidDataException($"Asset GUID '{guid:N}' is duplicated in the built-in resource archive.")
            };
        }
        if (requested.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            _archive.TryGetEntry(AssetBundleValidation.NormalizeAddress(requested), out var direct))
            return direct.Address;

        var matches = _addressIndex.GetResources(NormalizeFolder(folderName)).Resolve(requested);
        return matches.Count switch
        {
            0 => null,
            1 => matches.Address,
            _ => throw new InvalidOperationException(
                $"Resource path '{path}' is ambiguous in the built-in AOT resource archive.")
        };
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
        var assets = (((global::System.Collections.Generic.IEnumerable<global::BEngine.BObject>)(_decodedAssets.Values)) switch
        {
            global::BEngine.BObject[] { Length: 0 } => global::System.Array.Empty<global::BEngine.BObject>(),
            var distinctItems11942 => distinctItems11942.ToHashSet<global::BEngine.BObject>().ToArray()});
        _decodedAssets.Clear();
        _manager.Dispose();
        if (_registered)
        {
            Resources.UnregisterResourceProvider(this);
            _registered = false;
        }
        foreach (var asset in assets) BObject.DestroyImmediate(asset);
    }

    // Only immutable address metadata is shared between AOT and game views. Decoded
    // assets remain owned by each provider, so disposing one domain cannot affect another.
    private sealed class ArchiveAddressIndex(BuiltInResourceArchive archive, PathUtility paths)
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, ResourceFolderIndex> _folders = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<Guid, AddressMatch>? _primaryAddresses;

        internal AddressMatch GetPrimaryAddress(Guid guid)
        {
            lock (_gate)
            {
                if (_primaryAddresses is null)
                {
                    _primaryAddresses = [];
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.LocalIdentifier != 0) continue;
                        var owner = entry.OwnerGuid == Guid.Empty ? entry.Guid : entry.OwnerGuid;
                        _primaryAddresses.TryGetValue(owner, out var match);
                        _primaryAddresses[owner] = match.Add(entry.Address);
                    }
                }
                return _primaryAddresses.GetValueOrDefault(guid);
            }
        }

        internal ResourceFolderIndex GetResources(string folder)
        {
            lock (_gate)
            {
                if (_folders.TryGetValue(folder, out var existing)) return existing;
                var marker = "/" + folder + "/";
                var entries = new List<(string Address, string ResourcePath)>();
                foreach (var entry in archive.Entries)
                {
                    var index = entry.Address.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (index < 0 || index + marker.Length == entry.Address.Length) continue;
                    entries.Add((entry.Address, entry.Address[(index + marker.Length)..]));
                }
                // Missing folders must not grow a cache from arbitrary caller input.
                if (entries.Count == 0) return ResourceFolderIndex.Empty;
                var created = new ResourceFolderIndex(entries.ToArray(), paths);
                _folders.Add(folder, created);
                return created;
            }
        }
    }

    private readonly struct AddressMatch(string? address, int count)
    {
        internal string? Address { get; } = address;
        internal int Count { get; } = count;
        internal AddressMatch Add(string address) => new(Address ?? address, Math.Min(Count + 1, 2));
    }

    private sealed class ResourceFolderIndex
    {
        internal static readonly ResourceFolderIndex Empty = new();
        private readonly Dictionary<string, AddressMatch> _addresses = new(StringComparer.OrdinalIgnoreCase);
        internal readonly (string Address, string ResourcePath)[] Entries;

        private ResourceFolderIndex() => Entries = [];

        internal ResourceFolderIndex((string Address, string ResourcePath)[] entries, PathUtility paths)
        {
            Entries = entries;
            _addresses.EnsureCapacity(entries.Length * 2);
            foreach (var (address, resourcePath) in entries)
            {
                Add(resourcePath, address);
                // Match every extension-free prefix accepted by the previous resolver,
                // including names such as "file..txt" queried as "file.".
                for (var index = resourcePath.LastIndexOf('/') + 1; index < resourcePath.Length - 1; index++)
                {
                    if (resourcePath[index] != '.') continue;
                    var prefix = resourcePath[..index];
                    if (paths.GetExtension(prefix).Length == 0) Add(prefix, address);
                }
            }
        }

        internal AddressMatch Resolve(string path) => _addresses.GetValueOrDefault(path);

        private void Add(string path, string address)
        {
            _addresses.TryGetValue(path, out var match);
            _addresses[path] = match.Add(address);
        }
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
                PackageName = "builtin",
                Version = "builtin",
                Assets = Gather17248(archive.Entries)
            };
            [global::BEngine.SkipLoopGuard]
            global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundleAsset> Gather17248(global::System.Collections.Generic.IEnumerable<global::BEngine.Content.BuiltInResourceArchiveEntry> items17248)
            {
                ArgumentNullException.ThrowIfNull(items17248, "source");
                global::BEngine.AssetBundles.AssetBundleAsset Evaluate17248(global::BEngine.Content.BuiltInResourceArchiveEntry entry) => new AssetBundleAsset
                {
                    Address = entry.Address,
                    Guid = entry.Guid,
                    OwnerGuid = entry.OwnerGuid,
                    LocalIdentifier = entry.LocalIdentifier,
                    AssetType = entry.AssetType,
                    Importer = entry.Importer,
                    ImporterSettings = new(entry.ImporterSettings),
                    Size = entry.Size,
                    Sha256 = entry.Sha256,
                    Entry = entry.Address
                };
                global::System.ReadOnlySpan<global::BEngine.Content.BuiltInResourceArchiveEntry> values;
                if (items17248.GetType() == typeof(global::BEngine.Content.BuiltInResourceArchiveEntry[]))
                    values = (global::BEngine.Content.BuiltInResourceArchiveEntry[])items17248;
                else if (items17248.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Content.BuiltInResourceArchiveEntry>))
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Content.BuiltInResourceArchiveEntry>)items17248);
                else
                {
                    return global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(items17248, Evaluate17248));
                }

                var result = new global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundleAsset>(values.Length);
                global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(result, values.Length);
                var destination = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result);
                for (var index = 0; index < values.Length; index++)
                    destination[index] = Evaluate17248(values[index]);
                return result;
            }
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
            return new AssetBundleHandle<string>(address, RuntimeAssetDecoding.DecodeText(bytes.Asset), static () => { });
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
