using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;

namespace BEngine.Content;

internal sealed class BuiltInResourceArchiveWriteEntry
{
    private readonly byte[]? _content;

    private BuiltInResourceArchiveWriteEntry(
        string address,
        string assetType,
        Guid guid,
        Guid ownerGuid,
        long localIdentifier,
        string importer,
        IReadOnlyDictionary<string, string>? importerSettings,
        string sha256,
        long size,
        string? filePath,
        byte[]? content)
    {
        Address = AssetBundleValidation.NormalizeAddress(address);
        AssetType = assetType?.Trim() ?? string.Empty;
        Guid = guid == Guid.Empty ? CreateStableGuid(Address) : guid;
        OwnerGuid = ownerGuid == Guid.Empty ? Guid : ownerGuid;
        LocalIdentifier = localIdentifier;
        Importer = importer?.Trim() ?? string.Empty;
        ImporterSettings = new Dictionary<string, string>(
            importerSettings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        Sha256 = sha256?.ToLowerInvariant() ?? string.Empty;
        Size = size;
        FilePath = filePath;
        _content = content;
    }

    internal string Address { get; }
    internal string AssetType { get; }
    internal Guid Guid { get; }
    internal Guid OwnerGuid { get; }
    internal long LocalIdentifier { get; }
    internal string Importer { get; }
    internal IReadOnlyDictionary<string, string> ImporterSettings { get; }
    internal string Sha256 { get; }
    internal long Size { get; }
    internal string? FilePath { get; }

    internal static BuiltInResourceArchiveWriteEntry FromFile(
        string address,
        string filePath,
        string assetType,
        string sha256,
        long size,
        Guid guid = default,
        Guid ownerGuid = default,
        long localIdentifier = 0,
        string importer = "BuiltInResource",
        IReadOnlyDictionary<string, string>? importerSettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return new BuiltInResourceArchiveWriteEntry(
            address, assetType, guid, ownerGuid, localIdentifier, importer, importerSettings,
            sha256, size, Path.GetFullPath(filePath), null);
    }

    internal static BuiltInResourceArchiveWriteEntry FromMemory(
        string address,
        ReadOnlySpan<byte> content,
        string assetType,
        Guid guid = default,
        Guid ownerGuid = default,
        long localIdentifier = 0,
        string importer = "BuiltInResource",
        IReadOnlyDictionary<string, string>? importerSettings = null)
    {
        var bytes = content.ToArray();
        return new BuiltInResourceArchiveWriteEntry(
            address, assetType, guid, ownerGuid, localIdentifier, importer, importerSettings,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength, null, bytes);
    }

    internal Stream OpenRead() => _content is not null
        ? new MemoryStream(_content, writable: false)
        : new FileStream(FilePath ?? throw new InvalidDataException(
                $"Built-in resource '{Address}' has no payload source."),
            FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);

    private static Guid CreateStableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
