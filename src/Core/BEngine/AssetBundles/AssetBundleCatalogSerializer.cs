using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BEngine.AssetBundles;

public static class AssetBundleCatalogSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        MaxDepth = 64,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Serialize(AssetBundleCatalog catalog) => SerializeCatalog(catalog);
    public static byte[] Serialize(AssetBundleVersion version) => SerializeVersion(version);

    public static byte[] SerializeCatalog(AssetBundleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(Canonicalize(catalog), Options);
    }

    public static byte[] SerializeVersion(AssetBundleVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        version.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(Canonicalize(version), Options);
    }

    public static AssetBundleCatalog DeserializeCatalog(string json) =>
        DeserializeCatalog(Encoding.UTF8.GetBytes(json ?? throw new ArgumentNullException(nameof(json))));

    public static AssetBundleCatalog DeserializeCatalog(ReadOnlySpan<byte> json)
    {
        RejectDuplicateProperties(json);
        var catalog = JsonSerializer.Deserialize<AssetBundleCatalog>(json, Options) ??
                      throw new InvalidDataException("Asset bundle catalog JSON is empty.");
        catalog.Validate();
        return catalog;
    }

    public static AssetBundleVersion DeserializeVersion(string json) =>
        DeserializeVersion(Encoding.UTF8.GetBytes(json ?? throw new ArgumentNullException(nameof(json))));

    public static AssetBundleVersion DeserializeVersion(ReadOnlySpan<byte> json)
    {
        RejectDuplicateProperties(json);
        var version = JsonSerializer.Deserialize<AssetBundleVersion>(json, Options) ??
                      throw new InvalidDataException("Asset bundle version JSON is empty.");
        version.Validate();
        return version;
    }

    public static string ComputeSha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string ComputeSha256(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static async Task<string> ComputeSha256Async(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static AssetBundleCatalog Canonicalize(AssetBundleCatalog source) => new()
    {
        Format = source.Format,
        SchemaVersion = source.SchemaVersion,
        PackageName = source.PackageName,
        Version = source.Version,
        Bundles = source.Bundles.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item =>
            new AssetBundleDescriptor
            {
                Name = item.Name,
                FileName = item.Sha256.ToLowerInvariant() + ".bassetbundle",
                Sha256 = item.Sha256.ToLowerInvariant(),
                Size = item.Size,
                Dependencies = item.Dependencies.OrderBy(value => value, StringComparer.Ordinal).ToList()
            }).ToList(),
        Assets = source.Assets.OrderBy(item => item.Address, StringComparer.Ordinal).Select(item =>
            new AssetBundleAsset
            {
                Address = item.Address,
                Guid = item.Guid,
                Bundle = item.Bundle,
                Entry = item.Entry,
                AssetType = item.AssetType,
                Sha256 = item.Sha256.ToLowerInvariant(),
                Size = item.Size
            }).ToList()
    };

    private static AssetBundleVersion Canonicalize(AssetBundleVersion source) => new()
    {
        Format = source.Format,
        SchemaVersion = source.SchemaVersion,
        PackageName = source.PackageName,
        Version = source.Version,
        CatalogFile = source.CatalogFile,
        CatalogSha256 = source.CatalogSha256.ToLowerInvariant(),
        CatalogSize = source.CatalogSize
    };

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            Inspect(document.RootElement, "$", 0);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Asset bundle JSON is invalid.", exception);
        }
    }

    private static void Inspect(JsonElement element, string path, int depth)
    {
        if (depth > 64) throw new InvalidDataException("Asset bundle JSON exceeds its maximum depth.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException(
                        $"Asset bundle JSON contains duplicate property '{path}.{property.Name}'.");
                Inspect(property.Value, $"{path}.{property.Name}", depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                Inspect(item, $"{path}[{index++}]", depth + 1);
        }
    }
}
