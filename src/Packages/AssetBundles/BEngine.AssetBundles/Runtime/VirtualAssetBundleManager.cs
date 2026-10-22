using System.Text;

namespace BEngine.AssetBundles;

public sealed class VirtualAssetBundleManager : IAssetBundleManager
{
    private readonly AssetBundleCatalog _catalog;
    private readonly AssetBundleVersion _version;
    private readonly AssetBundleCatalogIndex _index;
    private readonly IReadOnlyCollection<string> _addresses;
    private readonly Func<string, byte[]> _read;
    private readonly Func<string, BCancellationToken, BValueTask<(byte[] Bytes, Action Release)>> _readAsync;
    private int _leases;
    private int _initialized;
    private int _disposed;

    public VirtualAssetBundleManager(VirtualAssetBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // The index and public snapshots must describe the same privately owned
        // metadata, even if a caller later edits the source snapshot objects.
        _catalog = AssetBundleCatalogSerializer.DeserializeCatalog(
            AssetBundleCatalogSerializer.SerializeCatalog(snapshot.Catalog));
        _version = AssetBundleCatalogSerializer.DeserializeVersion(
            AssetBundleCatalogSerializer.SerializeVersion(snapshot.Version));
        _index = CreateIndex(_catalog);
        _addresses = _index.Addresses;
        _read = address => FindEntry(address).Read();
        _readAsync = async (address, cancellationToken) =>
            (await FindEntry(address).ReadAsync(cancellationToken), static () => { });

        VirtualAssetBundleEntry FindEntry(string address) =>
            snapshot.Entries.TryGetValue(address, out var entry) ? entry :
                throw new FileNotFoundException($"Virtual AssetBundle address '{address}' was not found.", address);
    }

    private VirtualAssetBundleManager(
        byte[] catalogBytes,
        byte[] versionBytes,
        Func<string, byte[]> read,
        Func<string, BCancellationToken, BValueTask<(byte[] Bytes, Action Release)>> readAsync)
    {
        _catalog = AssetBundleCatalogSerializer.DeserializeCatalog(catalogBytes);
        _version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
        if (_catalog.PackageName != _version.PackageName || _catalog.Version != _version.Version ||
            catalogBytes.LongLength != _version.CatalogSize ||
            !AssetBundleCatalogSerializer.ComputeSha256(catalogBytes)
                .Equals(_version.CatalogSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Virtual AssetBundle bridge catalog does not match its version.");
        _index = CreateIndex(_catalog);
        _addresses = _index.Addresses;
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _readAsync = readAsync ?? throw new ArgumentNullException(nameof(readAsync));
    }

    /// <summary>Creates an independent view across managed-code domains using shared Core/BCL contracts.</summary>
    public static VirtualAssetBundleManager CreateSnapshotBridge(
        byte[] catalogBytes,
        byte[] versionBytes,
        Func<string, byte[]> read,
        Func<string, BCancellationToken, BValueTask<(byte[] Bytes, Action Release)>> readAsync) =>
        new(catalogBytes, versionBytes, read, readAsync);

    public AssetBundleCatalog? ActiveCatalog => _initialized == 0
        ? null
        : AssetBundleCatalogSerializer.DeserializeCatalog(
            AssetBundleCatalogSerializer.SerializeCatalog(_catalog));

    public AssetBundleVersion? ActiveVersion => _initialized == 0
        ? null
        : AssetBundleCatalogSerializer.DeserializeVersion(
            AssetBundleCatalogSerializer.SerializeVersion(_version));

    internal AssetBundleResourceRevision ResourceRevision => _initialized == 0
        ? default
        : AssetBundleResourceRevision.FromVersion(_version);

    // Internal readers use the manager-owned index. Public callers still receive
    // defensive catalog snapshots rather than mutable indexed entries.
    internal bool TryGetActiveAsset(string canonicalAddress, out AssetBundleAsset asset)
    {
        if (_initialized != 0) return _index.TryGetAsset(canonicalAddress, out asset!);
        asset = null!;
        return false;
    }

    internal bool TryGetActiveMainAsset(Guid ownerGuid, out AssetBundleAsset asset)
    {
        if (_initialized != 0) return _index.TryGetMainAsset(ownerGuid, out asset!);
        asset = null!;
        return false;
    }

    private static AssetBundleCatalogIndex CreateIndex(AssetBundleCatalog catalog)
    {
        using var catalogBudget = LoopExecutionGuard.AllowIterations(Math.Max(catalog.Assets.Count, catalog.Bundles.Count));
        return new AssetBundleCatalogIndex(catalog, cloneCatalog: false);
    }

    public bool IsInitialized => _initialized != 0;
    public int ActiveLeaseCount => _leases;

    public BValueTask InitializeAsync(BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        _initialized = 1;
        return BValueTask.CompletedTask;
    }

    public BValueTask<AssetBundleUpdatePlan> CheckForUpdatesAsync(BCancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        var catalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(_catalog);
        return BValueTask<AssetBundleUpdatePlan>.FromResult(new AssetBundleUpdatePlan(
            _version, _catalog, catalogBytes, [], hasUpdates: false));
    }

    public BValueTask<AssetBundleUpdateResult> ApplyUpdateAsync(
        AssetBundleUpdatePlan plan,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.HasUpdates)
            throw new InvalidOperationException("A virtual AssetBundle snapshot cannot apply remote updates.");
        progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Completed, 0, 0, 0, 0));
        return BValueTask<AssetBundleUpdateResult>.FromResult(new AssetBundleUpdateResult(
            false, _version.Version, _version.Version, 0, 0));
    }

    public BValueTask<bool> RollbackAsync(BCancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        return BValueTask<bool>.FromResult(false);
    }

    public async BValueTask<AssetBundleHandle<byte[]>> LoadBytesAsync(
        string address,
        BCancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        var canonical = AssetBundleValidation.NormalizeAddress(address);
        var (bytes, release) = await _readAsync(canonical, cancellationToken);
        try
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            release();
            throw;
        }
        _leases++;
        return new AssetBundleHandle<byte[]>(canonical, bytes, () =>
        {
            try { release(); }
            finally { _leases--; }
        });
    }

    public async BValueTask<AssetBundleHandle<string>> LoadTextAsync(
        string address,
        BCancellationToken cancellationToken = default)
    {
        var bytes = await LoadBytesAsync(address, cancellationToken);
        try
        {
            return new AssetBundleHandle<string>(bytes.Address, Encoding.UTF8.GetString(bytes.Value), bytes.Dispose);
        }
        catch
        {
            bytes.Dispose();
            throw;
        }
    }

    public bool TryLoadBytes(string address, out byte[] bytes)
    {
        try
        {
            EnsureInitialized();
            var canonical = AssetBundleValidation.NormalizeAddress(address);
            bytes = (byte[])_read(canonical).Clone();
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or ArgumentException)
        {
            bytes = [];
            return false;
        }
    }

    public IReadOnlyList<string> EnumerateAddresses(string prefix = "")
    {
        EnsureInitialized();
        var normalized = NormalizePrefix(prefix);
        return Array.AsReadOnly(_addresses
            .Where(address => normalized.Length == 0 || address.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public int UnloadUnused() => 0;

    public BValueTask<int> CleanupAsync(BCancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        return BValueTask<int>.FromResult(0);
    }

    public void Dispose()
    {
        if (_disposed != 0) return;
        _disposed = 1;
        _initialized = 0;
    }

    public BValueTask DisposeAsync()
    {
        Dispose();
        return BValueTask.CompletedTask;
    }

    private void EnsureInitialized()
    {
        ThrowIfDisposed();
        if (!IsInitialized)
            throw new InvalidOperationException("VirtualAssetBundleManager.InitializeAsync must complete first.");
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
        var normalized = prefix.Replace('\\', '/').Trim('/');
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)) return "Assets/";
        return AssetBundleValidation.NormalizeAddress(normalized);
    }
}
