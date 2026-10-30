using System.Text;
using System.Buffers;
using System.Security.Cryptography;
using YamlDotNet.Serialization;

namespace BEngine.AssetBundles;

/// <summary>A build and maintenance unit. Asset addresses remain global across packages.</summary>
public sealed class AssetBundlePackageDescriptor
{
    public const string DefaultName = "default";

    [YamlMember(Order = 0)] public string Name { get; set; } = string.Empty;
    /// <summary>A content fingerprint, independent of the containing release version.</summary>
    [YamlMember(Order = 1)] public string Version { get; set; } = string.Empty;
    [YamlMember(Order = 2)] public List<string> Bundles { get; set; } = [];

    /// <summary>Computes a stable package fingerprint without including release download routes.</summary>
    public static string ComputeVersion(AssetBundleCatalog catalog, IEnumerable<string> bundleNames)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bundleNames);
        var names = bundleNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = catalog.Bundles.Where(bundle => names.Contains(bundle.Name))
            .OrderBy(bundle => bundle.Name, StringComparer.Ordinal).ToArray();
        if (selected.Length != names.Count)
            throw new InvalidDataException("A package fingerprint references a missing bundle.");
        using var writer = new FingerprintWriter();
        WriteFingerprint(writer, selected, catalog, names, null);
        return writer.Finish();
    }

    internal static string ComputeOwnedVersion(OwnedPackageFingerprint.Input input)
    {
        input.Claim();
        using var writer = new FingerprintWriter();
        WriteFingerprint(writer, null, null, null, input);
        return writer.Finish();
    }

    // A single protocol traversal serves both public mutable inputs and private
    // validated owned inputs. Only already proven ordering/lowercase facts differ.
    private static void WriteFingerprint(FingerprintWriter writer,
        IReadOnlyList<AssetBundleDescriptor>? selected, AssetBundleCatalog? catalog,
        HashSet<string>? names, OwnedPackageFingerprint.Input? input)
    {
        var ownedBundles = input?.OrderedBundles();
        var bundleCount = ownedBundles?.Count ?? selected!.Count;
        writer.Write("BEngine.AssetBundlePackage.v1");
        writer.Write(bundleCount);
        for (var index = 0; index < bundleCount; index++)
        {
            var bundle = ownedBundles is null ? selected![index] : ownedBundles[index].Bundle;
            writer.Write(bundle.Name);
            writer.Write(input?.HashesLowercase == true ? bundle.Sha256 : bundle.Sha256.ToLowerInvariant());
            writer.Write(bundle.Size);
            IReadOnlyList<string> dependencies = ownedBundles is not null && ownedBundles[index].DependenciesOrdered
                ? bundle.Dependencies : bundle.Dependencies.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            writer.Write(dependencies.Count);
            foreach (var dependency in dependencies) writer.Write(dependency);
        }
        // Preserve the public method's evaluation/exception order: asset inputs
        // are visited only after every selected bundle has been written.
        var ownedAssets = input?.OrderedAssets();
        var assets = ownedAssets is null ? catalog!.Assets.Where(asset => names!.Contains(asset.Bundle))
            .OrderBy(asset => asset.Address, StringComparer.Ordinal).ToArray() : null;
        var assetCount = ownedAssets?.Count ?? assets!.Length;
        writer.Write(assetCount);
        Span<byte> guidBytes = stackalloc byte[16];
        for (var index = 0; index < assetCount; index++)
        {
            var asset = ownedAssets is null ? assets![index] : ownedAssets[index].Asset;
            writer.Write(asset.Address);
            asset.Guid.TryWriteBytes(guidBytes);
            writer.Write(guidBytes);
            (asset.OwnerGuid == Guid.Empty ? asset.Guid : asset.OwnerGuid).TryWriteBytes(guidBytes);
            writer.Write(guidBytes);
            writer.Write(asset.LocalIdentifier);
            writer.Write(asset.Bundle);
            writer.Write(asset.Entry);
            writer.Write(asset.AssetType);
            writer.Write(input?.HashesLowercase == true ? asset.Sha256 : asset.Sha256.ToLowerInvariant());
            writer.Write(asset.Size);
            writer.Write(asset.Importer ?? string.Empty);
            writer.Write(asset.ImporterSettings.Count);
            IEnumerable<KeyValuePair<string, string>> settings = ownedAssets is not null && ownedAssets[index].SettingsOrdered
                ? asset.ImporterSettings : asset.ImporterSettings.OrderBy(item => item.Key, StringComparer.Ordinal);
            foreach (var setting in settings)
            {
                writer.Write(setting.Key);
                writer.Write(setting.Value);
            }
        }
    }


    private sealed class FingerprintWriter : IDisposable
    {
        private const int BufferSize = 64 * 1024;
        private readonly IncrementalHash? _hash;
        private readonly AssetBundleStreamingSha256? _browserHash;
        private byte[]? _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        private int _count;
        private bool _finished;
        internal FingerprintWriter()
        {
            if (OperatingSystem.IsBrowser()) _browserHash = new AssetBundleStreamingSha256();
            else _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }
        private Span<byte> Reserve(int count)
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            if (_finished) throw new InvalidOperationException("The package fingerprint has already completed.");
            if (BufferSize - _count < count) Flush();
            var result = _buffer.AsSpan(_count, count);
            _count += count;
            return result;
        }
        internal void Write(int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
        internal void Write(long value) => System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);
        internal void Write(ReadOnlySpan<byte> bytes)
        {
            while (!bytes.IsEmpty)
            {
                var copied = Math.Min(BufferSize - _count, bytes.Length);
                bytes[..copied].CopyTo(Reserve(copied));
                bytes = bytes[copied..];
                if (_count == BufferSize) Flush();
            }
        }
        internal void Write(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var byteCount = Encoding.UTF8.GetByteCount(value);
            var remaining = (uint)byteCount;
            while (remaining >= 0x80)
            {
                Reserve(1)[0] = (byte)(remaining | 0x80);
                remaining >>= 7;
            }
            Reserve(1)[0] = (byte)remaining;
            if (byteCount <= BufferSize)
            {
                Encoding.UTF8.GetBytes(value.AsSpan(), Reserve(byteCount));
                return;
            }
            // Public callers may supply strings beyond binary-catalog bounds.
            // Stateful conversion preserves surrogate replacement at boundaries.
            var encoder = Encoding.UTF8.GetEncoder();
            var chars = value.AsSpan();
            while (!chars.IsEmpty)
            {
                if (BufferSize - _count < 4) Flush();
                encoder.Convert(chars, _buffer.AsSpan(_count, BufferSize - _count), true,
                    out var charsUsed, out var bytesUsed, out _);
                _count += bytesUsed;
                chars = chars[charsUsed..];
                if (_count == BufferSize) Flush();
            }
        }
        private void Flush()
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            if (_count == 0) return;
            if (_browserHash is { } browserHash) browserHash.Append(_buffer.AsSpan(0, _count));
            else _hash!.AppendData(_buffer, 0, _count);
            _count = 0;
        }
        internal string Finish()
        {
            Flush(); _finished = true;
            if (_browserHash is { } browserHash)
            {
                Span<byte> digest = stackalloc byte[32];
                browserHash.FinalizeHash(digest);
                return Convert.ToHexString(digest).ToLowerInvariant();
            }
            return Convert.ToHexString(_hash!.GetHashAndReset()).ToLowerInvariant();
        }
        public void Dispose()
        {
            if (_buffer is { } buffer)
            {
                _buffer = null; _hash?.Dispose(); _browserHash?.Dispose(); ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

}

internal static class AssetBundlePackageUtility
{
    internal static IReadOnlyList<AssetBundlePackageDescriptor> GetPackages(AssetBundleCatalog catalog) =>
        catalog.Packages.Count > 0 ? catalog.Packages :
        [new AssetBundlePackageDescriptor
        {
            Name = AssetBundlePackageDescriptor.DefaultName,
            Version = AssetBundlePackageDescriptor.ComputeVersion(catalog,
                catalog.Bundles.Select(static bundle => bundle.Name)),
            Bundles = catalog.Bundles.Select(static bundle => bundle.Name)
                .OrderBy(static name => name, StringComparer.Ordinal).ToList()
        }];
}
