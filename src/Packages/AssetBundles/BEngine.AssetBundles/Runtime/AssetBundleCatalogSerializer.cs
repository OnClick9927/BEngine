using System.Security.Cryptography;
using System.Text;
using BEngine.Serialization;

namespace BEngine.AssetBundles;

public static partial class AssetBundleCatalogSerializer
{

    public static byte[] Serialize(AssetBundleCatalog catalog) => SerializeCatalog(catalog);
    public static byte[] Serialize(AssetBundleVersion version) => SerializeVersion(version);
    public static byte[] Serialize(AssetBundleLatestPointer pointer) => SerializeLatestPointer(pointer);

    public static byte[] SerializeCatalog(AssetBundleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.Validate();
        return YamlUtility.SerializeToUtf8Bytes(Canonicalize(catalog));
    }

    public static byte[] SerializeVersion(AssetBundleVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        version.Validate();
        return YamlUtility.SerializeToUtf8Bytes(Canonicalize(version));
    }

    public static byte[] SerializeLatestPointer(AssetBundleLatestPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        pointer.Validate();
        return YamlUtility.SerializeToUtf8Bytes(Canonicalize(pointer));
    }

    public static AssetBundleCatalog DeserializeCatalog(string yaml) =>
        DeserializeCatalog(Encoding.UTF8.GetBytes(yaml ?? throw new ArgumentNullException(nameof(yaml))));

    public static AssetBundleCatalog DeserializeCatalog(ReadOnlySpan<byte> yaml)
    {
        var catalog = IsRuntimeCatalog(yaml)
            ? DeserializeRuntimeCatalog(yaml)
            : YamlUtility.Deserialize<AssetBundleCatalog>(yaml, strict: true);
        catalog.Validate();
        return catalog;
    }

    /// <summary>Creates a detached canonical snapshot without serializing or revalidating trusted state.</summary>
    public static AssetBundleCatalog CloneCatalog(AssetBundleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return Canonicalize(catalog);
    }

    /// <summary>Creates a detached version snapshot without serializing or revalidating trusted state.</summary>
    public static AssetBundleVersion CloneVersion(AssetBundleVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return Canonicalize(version);
    }

    public static AssetBundleVersion DeserializeVersion(string yaml) =>
        DeserializeVersion(Encoding.UTF8.GetBytes(yaml ?? throw new ArgumentNullException(nameof(yaml))));

    public static AssetBundleVersion DeserializeVersion(ReadOnlySpan<byte> yaml)
    {
        var version = YamlUtility.Deserialize<AssetBundleVersion>(yaml, strict: true);
        version.Validate();
        return version;
    }

    public static AssetBundleLatestPointer DeserializeLatestPointer(string yaml) =>
        DeserializeLatestPointer(Encoding.UTF8.GetBytes(
            yaml ?? throw new ArgumentNullException(nameof(yaml))));

    public static AssetBundleLatestPointer DeserializeLatestPointer(ReadOnlySpan<byte> yaml)
    {
        var pointer = YamlUtility.Deserialize<AssetBundleLatestPointer>(yaml, strict: true);
        pointer.Validate();
        return pointer;
    }

    public static string ComputeSha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string ComputeSha256(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static async BValueTask<string> ComputeSha256Async(
        Stream stream,
        BCancellationToken cancellationToken = default,
        int bytesPerFrame = 1024 * 1024)
        => await ComputeSha256WithProgressAsync(stream, cancellationToken, bytesPerFrame, null);

    internal static async BValueTask<string> ComputeSha256WithProgressAsync(
        Stream stream, BCancellationToken cancellationToken, int bytesPerFrame, Action<long>? progress)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (bytesPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(bytesPerFrame));
        if (BackgroundWorkScheduler.IsSupported)
        {
            // The stream is private to this verification request. Hashing and
            // synchronous file reads therefore stay off the frame thread; the
            // scheduler only publishes the result back on the owning thread.
            long processedBytes = 0;
            var hashing = BackgroundWorkScheduler.Main.Run(token =>
                ComputeSha256OnWorker(stream, bytesPerFrame, token,
                    progress is null ? null : count => Interlocked.Exchange(ref processedBytes, count)), cancellationToken);
            long previous = -1;
            while (progress is not null && !hashing.IsCompleted)
            {
                var count = Interlocked.Read(ref processedBytes);
                if (count != previous) { previous = count; progress(count); }
                await BTask.Yield();
            }
            var result = await hashing;
            progress?.Invoke(Interlocked.Read(ref processedBytes));
            return result;
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[Math.Min(64 * 1024, bytesPerFrame)];
        var processed = 0;
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, bytesPerFrame - processed));
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            processed += read;
            total += read;
            progress?.Invoke(total);
            if (processed >= bytesPerFrame)
            {
                await BValueTask.Yield();
                processed = 0;
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ComputeSha256OnWorker(Stream stream, int chunkSize, CancellationToken cancellationToken, Action<long>? progress = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[Math.Min(64 * 1024, chunkSize)];
        long processed = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            processed += read;
            progress?.Invoke(processed);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static AssetBundleCatalog Canonicalize(AssetBundleCatalog source) => new()
    {
        Format = source.Format,
        SchemaVersion = source.SchemaVersion,
        PackageName = source.PackageName,
        Version = source.Version,
        IsPackageComposite = source.IsPackageComposite,
        Bundles = source.Bundles.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item =>
            new AssetBundleDescriptor
            {
                Name = item.Name,
                FileName = item.Sha256.ToLowerInvariant() + ".bassetbundle",
                Sha256 = item.Sha256.ToLowerInvariant(),
                Size = item.Size,
                SourceVersion = item.SourceVersion,
                Dependencies = item.Dependencies.OrderBy(value => value, StringComparer.Ordinal).ToList()
            }).ToList(),
        Assets = source.Assets.OrderBy(item => item.Address, StringComparer.Ordinal).Select(item =>
            new AssetBundleAsset
            {
                Address = item.Address,
                Guid = item.Guid,
                OwnerGuid = source.SchemaVersion >= 3 && item.OwnerGuid == Guid.Empty
                    ? item.Guid
                    : item.OwnerGuid,
                LocalIdentifier = item.LocalIdentifier,
                Bundle = item.Bundle,
                Entry = item.Entry,
                AssetType = item.AssetType,
                Sha256 = item.Sha256.ToLowerInvariant(),
                Size = item.Size,
                Importer = item.Importer,
                ImporterSettings = item.ImporterSettings
                    .OrderBy(setting => setting.Key, StringComparer.Ordinal)
                    .ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal)
            }).ToList(),
        Packages = source.Packages.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item =>
            new AssetBundlePackageDescriptor
            {
                Name = item.Name,
                Version = item.Version,
                Bundles = item.Bundles.OrderBy(value => value, StringComparer.Ordinal).ToList()
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

    private static AssetBundleLatestPointer Canonicalize(AssetBundleLatestPointer source) => new()
    {
        Version = source.Version
    };

}
