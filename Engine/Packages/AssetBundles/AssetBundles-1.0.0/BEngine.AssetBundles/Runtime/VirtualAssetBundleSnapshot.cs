using System.Security.Cryptography;
using System.Text;
using BEngine.Platform;

namespace BEngine.AssetBundles;

public sealed class VirtualAssetBundleEntry
{
    private readonly byte[]? _content;
    private readonly string? _filePath;
    private readonly FileUtility? _fileSystem;

    private VirtualAssetBundleEntry(
        string address,
        string entry,
        Guid guid,
        Guid ownerGuid,
        long localIdentifier,
        string bundle,
        string assetType,
        string importer,
        IReadOnlyDictionary<string, string>? importerSettings,
        byte[]? content,
        string? filePath,
        FileUtility? fileSystem,
        string sha256,
        long size)
    {
        Address = AssetBundleValidation.NormalizeAddress(address);
        Entry = entry.Replace('\\', '/');
        Guid = guid == Guid.Empty ? CreateStableGuid(Address) : guid;
        OwnerGuid = ownerGuid == Guid.Empty ? Guid : ownerGuid;
        LocalIdentifier = localIdentifier;
        Bundle = bundle;
        AssetType = assetType;
        Importer = importer;
        ImporterSettings = new Dictionary<string, string>(
            importerSettings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        _content = content;
        _filePath = filePath;
        _fileSystem = fileSystem;
        Sha256 = sha256;
        Size = size;
    }

    public string Address { get; }
    public string Entry { get; }
    public Guid Guid { get; }
    public Guid OwnerGuid { get; }
    public long LocalIdentifier { get; }
    public string Bundle { get; }
    public string AssetType { get; }
    public string Importer { get; }
    public IReadOnlyDictionary<string, string> ImporterSettings { get; }
    public string Sha256 { get; }
    public long Size { get; }

    public static VirtualAssetBundleEntry FromMemory(
        string address,
        ReadOnlySpan<byte> content,
        string assetType,
        Guid guid = default,
        string bundle = "virtual",
        string? entry = null,
        Guid ownerGuid = default,
        long localIdentifier = 0,
        string importer = "VirtualAsset",
        IReadOnlyDictionary<string, string>? importerSettings = null)
    {
        var bytes = content.ToArray();
        return new VirtualAssetBundleEntry(
            address, entry ?? AssetBundleValidation.NormalizeAddress(address), guid, ownerGuid, localIdentifier,
            bundle, assetType, importer, importerSettings, bytes, null, null,
            AssetBundleCatalogSerializer.ComputeSha256(bytes), bytes.LongLength);
    }

    public static VirtualAssetBundleEntry FromFile(
        string address,
        string filePath,
        string assetType,
        Guid guid = default,
        string bundle = "virtual",
        string? entry = null,
        Guid ownerGuid = default,
        long localIdentifier = 0,
        string importer = "VirtualAsset",
        IReadOnlyDictionary<string, string>? importerSettings = null,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        var path = pathService.GetFullPath(filePath);
        if (!fileSystem.FileExists(path))
            throw new FileNotFoundException("Virtual AssetBundle input was not found.", path);
        using var stream = fileSystem.OpenRead(path);
        return new VirtualAssetBundleEntry(
            address, entry ?? AssetBundleValidation.NormalizeAddress(address), guid, ownerGuid, localIdentifier,
            bundle, assetType, importer, importerSettings, null, path, fileSystem,
            AssetBundleCatalogSerializer.ComputeSha256(stream), fileSystem.GetFileLength(path));
    }

    internal byte[] Read()
    {
        var bytes = _content is not null
            ? (byte[])_content.Clone()
            : _fileSystem!.ReadAllBytes(_filePath!);
        ValidateSnapshot(bytes);
        return bytes;
    }

    internal async BValueTask<byte[]> ReadAsync(BCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_content is not null) return Read();
        var path = _filePath!;
        var fileSystem = _fileSystem!;
        var bytes = new byte[checked((int)fileSystem.GetFileLength(path))];
        using var stream = fileSystem.OpenFile(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        var offset = 0;
        var platform = RuntimeServiceAccess.PlatformService;
        var sliceStarted = platform.MonotonicTimestamp;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
            if (read == 0) throw new EndOfStreamException("Virtual AssetBundle entry is truncated.");
            offset += read;
            // Yield by elapsed work, not by each 64 KB. The latter adds a whole
            // player-loop frame per block even when a DLL is already in the OS cache.
            if (offset < bytes.Length && platform.GetElapsedTime(sliceStarted).TotalMilliseconds >= 4)
            {
                await BValueTask.Yield();
                sliceStarted = platform.MonotonicTimestamp;
            }
        }
        ValidateSnapshot(bytes);
        return bytes;
    }

    private void ValidateSnapshot(byte[] bytes)
    {
        if (bytes.LongLength != Size ||
            !AssetBundleCatalogSerializer.ComputeSha256(bytes).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Virtual AssetBundle entry '{Address}' changed after its snapshot was created.");
    }

    private static Guid CreateStableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}

public sealed class VirtualAssetBundleSnapshot
{
    private readonly Dictionary<string, VirtualAssetBundleEntry> _entries;

    public VirtualAssetBundleSnapshot(
        string packageName,
        string releaseId,
        IEnumerable<VirtualAssetBundleEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseId);
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToDictionary(entry => entry.Address, StringComparer.OrdinalIgnoreCase);
        if (_entries.Count == 0) throw new ArgumentException("A virtual bundle requires at least one entry.", nameof(entries));
        Catalog = BuildCatalog(packageName, releaseId, _entries.Values);
        var catalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(Catalog);
        Version = new AssetBundleVersion
        {
            PackageName = packageName,
            Version = releaseId,
            CatalogFile = "catalog.yaml",
            CatalogSha256 = AssetBundleCatalogSerializer.ComputeSha256(catalogBytes),
            CatalogSize = catalogBytes.LongLength
        };
        Version.Validate();
    }

    public AssetBundleCatalog Catalog { get; }
    public AssetBundleVersion Version { get; }
    internal IReadOnlyDictionary<string, VirtualAssetBundleEntry> Entries => _entries;

    private static AssetBundleCatalog BuildCatalog(
        string packageName,
        string releaseId,
        IEnumerable<VirtualAssetBundleEntry> entries)
    {
        var catalog = new AssetBundleCatalog { PackageName = packageName, Version = releaseId };
        foreach (var group in entries.GroupBy(entry => entry.Bundle, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(group.Key));
            long size = 0;
            foreach (var entry in group.OrderBy(entry => entry.Address, StringComparer.Ordinal))
            {
                hash.AppendData(Convert.FromHexString(entry.Sha256));
                size = checked(size + entry.Size);
                catalog.Assets.Add(new AssetBundleAsset
                {
                    Address = entry.Address,
                    Entry = entry.Entry,
                    Guid = entry.Guid,
                    OwnerGuid = entry.OwnerGuid,
                    LocalIdentifier = entry.LocalIdentifier,
                    Bundle = group.Key,
                    AssetType = entry.AssetType,
                    Importer = entry.Importer,
                    ImporterSettings = new Dictionary<string, string>(entry.ImporterSettings, StringComparer.Ordinal),
                    Sha256 = entry.Sha256,
                    Size = entry.Size
                });
            }
            var bundleHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            catalog.Bundles.Add(new AssetBundleDescriptor
            {
                Name = group.Key,
                FileName = $"{bundleHash}.bassetbundle",
                Sha256 = bundleHash,
                Size = Math.Max(1, size)
            });
        }
        catalog.Validate();
        return catalog;
    }
}
