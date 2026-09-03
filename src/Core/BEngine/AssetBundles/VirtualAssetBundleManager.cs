using System.Text;

namespace BEngine.AssetBundles;

public sealed class VirtualAssetBundleManager : IAssetBundleManager
{
    private readonly VirtualAssetBundleSnapshot _snapshot;
    private int _leases;
    private int _initialized;
    private int _disposed;

    public VirtualAssetBundleManager(VirtualAssetBundleSnapshot snapshot) =>
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public AssetBundleCatalog? ActiveCatalog => Volatile.Read(ref _initialized) == 0
        ? null
        : AssetBundleCatalogSerializer.DeserializeCatalog(
            AssetBundleCatalogSerializer.SerializeCatalog(_snapshot.Catalog));

    public AssetBundleVersion? ActiveVersion => Volatile.Read(ref _initialized) == 0
        ? null
        : AssetBundleCatalogSerializer.DeserializeVersion(
            AssetBundleCatalogSerializer.SerializeVersion(_snapshot.Version));

    public bool IsInitialized => Volatile.Read(ref _initialized) != 0;
    public int ActiveLeaseCount => Volatile.Read(ref _leases);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _initialized, 1);
        return Task.CompletedTask;
    }

    public Task<AssetBundleUpdatePlan> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        var catalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(_snapshot.Catalog);
        return Task.FromResult(new AssetBundleUpdatePlan(
            _snapshot.Version, _snapshot.Catalog, catalogBytes, [], hasUpdates: false));
    }

    public Task<AssetBundleUpdateResult> ApplyUpdateAsync(
        AssetBundleUpdatePlan plan,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.HasUpdates)
            throw new InvalidOperationException("A virtual AssetBundle snapshot cannot apply remote updates.");
        progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Completed, 0, 0, 0, 0));
        return Task.FromResult(new AssetBundleUpdateResult(
            false, _snapshot.Version.Version, _snapshot.Version.Version, 0, 0));
    }

    public Task<bool> RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public async Task<AssetBundleHandle<byte[]>> LoadBytesAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var canonical = AssetBundleValidation.NormalizeAddress(address);
        if (!_snapshot.Entries.TryGetValue(canonical, out var entry))
            throw new FileNotFoundException($"Virtual AssetBundle address '{canonical}' was not found.", canonical);
        var bytes = await entry.ReadAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _leases);
        return new AssetBundleHandle<byte[]>(canonical, bytes, () => Interlocked.Decrement(ref _leases));
    }

    public async Task<AssetBundleHandle<string>> LoadTextAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        await using var bytes = await LoadBytesAsync(address, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _leases);
        return new AssetBundleHandle<string>(bytes.Address, Encoding.UTF8.GetString(bytes.Value),
            () => Interlocked.Decrement(ref _leases));
    }

    public bool TryLoadBytes(string address, out byte[] bytes)
    {
        try
        {
            using var handle = LoadBytesAsync(address).ConfigureAwait(false).GetAwaiter().GetResult();
            bytes = (byte[])handle.Value.Clone();
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
        return Array.AsReadOnly(_snapshot.Entries.Keys
            .Where(address => normalized.Length == 0 || address.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public int UnloadUnused() => 0;

    public Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _initialized, 0);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void EnsureInitialized()
    {
        ThrowIfDisposed();
        if (!IsInitialized)
            throw new InvalidOperationException("VirtualAssetBundleManager.InitializeAsync must complete first.");
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
        var normalized = prefix.Replace('\\', '/').Trim('/');
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)) return "Assets/";
        return AssetBundleValidation.NormalizeAddress(normalized);
    }
}
