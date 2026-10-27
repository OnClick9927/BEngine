using System.Buffers.Binary;
using System.Text;

namespace BEngine.AssetBundles;

public static partial class AssetBundleCatalogSerializer
{
    public const string RuntimeCatalogFileName = "catalog.bin";
    private static ReadOnlySpan<byte> RuntimeCatalogMagic => "BECATBIN"u8;
    private static readonly UTF8Encoding CatalogUtf8 = new(false, true);
    private const int MaximumBinaryCatalogBytes = 64 * 1024 * 1024;
    private const int MaximumCatalogStrings = 1_000_000;
    private const int MaximumCatalogStringBytes = 64 * 1024;
    private const int MaximumCatalogBundles = 100_000;
    private const int MaximumCatalogAssets = 1_000_000;
    private const int MaximumCatalogReferences = 4_000_000;

    /// <summary>
    /// Writes the compact runtime catalog. YAML remains available through SerializeCatalog
    /// for authoring, and DeserializeCatalog accepts both formats.
    /// </summary>
    public static byte[] SerializeRuntimeCatalog(AssetBundleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.Validate();
        var canonical = Canonicalize(catalog);
        var strings = new Dictionary<string, int>(StringComparer.Ordinal);
        int Intern(string value)
        {
            if (strings.TryGetValue(value, out var index)) return index;
            if (strings.Count == MaximumCatalogStrings || CatalogUtf8.GetByteCount(value) > MaximumCatalogStringBytes)
                throw new InvalidDataException("Runtime catalog string table exceeds its supported limits.");
            index = strings.Count;
            strings.Add(value, index);
            return index;
        }

        Intern(canonical.PackageName);
        Intern(canonical.Version);
        foreach (var bundle in canonical.Bundles)
        {
            Intern(bundle.Name);
            Intern(bundle.SourceVersion);
            foreach (var dependency in bundle.Dependencies) Intern(dependency);
        }
        foreach (var asset in canonical.Assets)
        {
            Intern(asset.Address);
            Intern(asset.Bundle);
            if (!HasDerivedEntry(asset)) Intern(asset.Entry);
            Intern(asset.AssetType);
            Intern(asset.Importer);
            foreach (var setting in asset.ImporterSettings) { Intern(setting.Key); Intern(setting.Value); }
        }
        foreach (var package in canonical.Packages)
        {
            Intern(package.Name);
            Intern(package.Version);
            foreach (var bundle in package.Bundles) Intern(bundle);
        }

        if (canonical.Bundles.Count > MaximumCatalogBundles || canonical.Assets.Count > MaximumCatalogAssets ||
            canonical.Packages.Count > MaximumCatalogBundles)
            throw new InvalidDataException("Runtime catalog contains too many records.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, CatalogUtf8, leaveOpen: true))
        {
            writer.Write(RuntimeCatalogMagic);
            writer.Write((ushort)1); // Binary encoding version, independent of the catalog schema.
            writer.Write((ushort)0); // Reserved flags.
            writer.Write(canonical.SchemaVersion);
            writer.Write(strings.Count);
            foreach (var value in strings.Keys)
            {
                var encoded = CatalogUtf8.GetBytes(value);
                writer.Write(encoded.Length);
                writer.Write(encoded);
            }
            writer.Write(strings[canonical.PackageName]);
            writer.Write(strings[canonical.Version]);
            writer.Write(canonical.IsPackageComposite);
            writer.Write(canonical.Bundles.Count);
            long references = 0;
            void WriteBundles(List<string> names)
            {
                references += names.Count;
                if (references > MaximumCatalogReferences)
                    throw new InvalidDataException("Runtime catalog contains too many references.");
                writer.Write(names.Count);
                foreach (var name in names) writer.Write(strings[name]);
            }
            foreach (var bundle in canonical.Bundles)
            {
                writer.Write(strings[bundle.Name]);
                writer.Write(Convert.FromHexString(bundle.Sha256));
                writer.Write(bundle.Size);
                writer.Write(strings[bundle.SourceVersion]);
                WriteBundles(bundle.Dependencies);
            }
            writer.Write(canonical.Assets.Count);
            foreach (var asset in canonical.Assets)
            {
                writer.Write(strings[asset.Address]);
                writer.Write(asset.Guid.ToByteArray());
                var flags = (byte)((asset.OwnerGuid != asset.Guid ? 1 : 0) |
                    (asset.LocalIdentifier != 0 ? 2 : 0) | (HasDerivedEntry(asset) ? 4 : 0));
                writer.Write(flags);
                if ((flags & 1) != 0) writer.Write(asset.OwnerGuid.ToByteArray());
                if ((flags & 2) != 0) writer.Write(asset.LocalIdentifier);
                writer.Write(strings[asset.Bundle]);
                if ((flags & 4) == 0) writer.Write(strings[asset.Entry]);
                writer.Write(strings[asset.AssetType]);
                writer.Write(strings[asset.Importer]);
                writer.Write(Convert.FromHexString(asset.Sha256));
                writer.Write(asset.Size);
                writer.Write(asset.ImporterSettings.Count);
                foreach (var setting in asset.ImporterSettings)
                {
                    writer.Write(strings[setting.Key]);
                    writer.Write(strings[setting.Value]);
                }
            }
            writer.Write(canonical.Packages.Count);
            foreach (var package in canonical.Packages)
            {
                writer.Write(strings[package.Name]);
                writer.Write(strings[package.Version]);
                WriteBundles(package.Bundles);
            }
        }
        if (stream.Length > MaximumBinaryCatalogBytes)
            throw new InvalidDataException("Runtime catalog exceeds its supported byte limit.");
        return stream.ToArray();
    }

    private static bool HasDerivedEntry(AssetBundleAsset asset) =>
        string.Equals(asset.Entry, "objects/" + asset.Sha256 + ".bin", StringComparison.Ordinal);

    private static bool IsRuntimeCatalog(ReadOnlySpan<byte> bytes) => bytes.StartsWith(RuntimeCatalogMagic);

    private static AssetBundleCatalog DeserializeRuntimeCatalog(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumBinaryCatalogBytes)
            throw new InvalidDataException("Runtime catalog exceeds its supported byte limit.");
        var reader = new CatalogReader(bytes);
        reader.Take(RuntimeCatalogMagic.Length);
        if (reader.UInt16() != 1 || reader.UInt16() != 0)
            throw new InvalidDataException("Unsupported runtime catalog encoding or flags.");
        var schemaVersion = reader.Int32();
        if (schemaVersion < AssetBundleCatalog.MinimumSupportedSchemaVersion ||
            schemaVersion > AssetBundleCatalog.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported runtime catalog schema.");
        var strings = new string[reader.Count(MaximumCatalogStrings, 4)];
        for (var i = 0; i < strings.Length; i++)
        {
            var length = reader.Count(MaximumCatalogStringBytes, 1);
            try { strings[i] = CatalogUtf8.GetString(reader.Take(length)); }
            catch (DecoderFallbackException exception)
            { throw new InvalidDataException("Runtime catalog contains invalid UTF-8.", exception); }
        }
        var catalog = new AssetBundleCatalog
        {
            SchemaVersion = schemaVersion,
            PackageName = reader.String(strings),
            Version = reader.String(strings),
            IsPackageComposite = reader.Boolean()
        };
        var bundles = new AssetBundleDescriptor[reader.Count(MaximumCatalogBundles, 52)];
        var references = 0;
        for (var i = 0; i < bundles.Length; i++)
        {
            var name = reader.String(strings);
            var sha256 = reader.Hash();
            bundles[i] = new AssetBundleDescriptor
            {
                Name = name, Sha256 = sha256, FileName = sha256 + ".bassetbundle",
                Size = reader.Int64(), SourceVersion = reader.String(strings),
                Dependencies = reader.BundleReferences(strings, ref references)
            };
        }
        catalog.Bundles = bundles.ToList();
        var assetCount = reader.Count(MaximumCatalogAssets, 77);
        catalog.Assets = new List<AssetBundleAsset>(assetCount);
        for (var i = 0; i < assetCount; i++)
        {
            var address = reader.String(strings);
            var guid = reader.Guid();
            var flags = reader.Byte();
            if ((flags & ~7) != 0) throw new InvalidDataException("Unsupported runtime catalog asset flags.");
            var ownerGuid = (flags & 1) != 0 ? reader.Guid() : guid;
            var localIdentifier = (flags & 2) != 0 ? reader.Int64() : 0;
            var bundle = reader.String(strings);
            var entry = (flags & 4) != 0 ? null : reader.String(strings);
            var assetType = reader.String(strings);
            var importer = reader.String(strings);
            var hash = reader.Hash();
            var asset = new AssetBundleAsset
            {
                Address = address, Guid = guid, OwnerGuid = ownerGuid, LocalIdentifier = localIdentifier,
                Bundle = bundle, Entry = entry ?? "objects/" + hash + ".bin", AssetType = assetType,
                Importer = importer, Sha256 = hash, Size = reader.Int64()
            };
            var settingsCount = reader.Count(128, 8);
            for (var j = 0; j < settingsCount; j++)
                if (!asset.ImporterSettings.TryAdd(reader.String(strings), reader.String(strings)))
                    throw new InvalidDataException("Runtime catalog contains a duplicate importer setting.");
            catalog.Assets.Add(asset);
        }
        var packageCount = reader.Count(MaximumCatalogBundles, 12);
        catalog.Packages = new List<AssetBundlePackageDescriptor>(packageCount);
        for (var i = 0; i < packageCount; i++)
        {
            var name = reader.String(strings);
            var version = reader.String(strings);
            var packageBundles = reader.BundleReferences(strings, ref references);
            catalog.Packages.Add(new AssetBundlePackageDescriptor
            {
                Name = name, Version = version,
                Bundles = packageBundles
            });
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Runtime catalog contains trailing bytes.");
        return catalog;
    }

    private ref struct CatalogReader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;
        public int Remaining => _remaining.Length;
        public ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || length > _remaining.Length)
                throw new InvalidDataException("Runtime catalog is truncated or has an invalid length.");
            var value = _remaining[..length];
            _remaining = _remaining[length..];
            return value;
        }
        public byte Byte() => Take(1)[0];
        public bool Boolean() => Byte() switch
        {
            0 => false, 1 => true,
            _ => throw new InvalidDataException("Runtime catalog contains an invalid boolean.")
        };
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public Guid Guid() => new(Take(16));
        public string Hash() => Convert.ToHexString(Take(32)).ToLowerInvariant();
        public int Count(int maximum, int minimumRecordBytes)
        {
            var count = Int32();
            if (count < 0 || count > maximum || count > Remaining / minimumRecordBytes)
                throw new InvalidDataException("Runtime catalog contains an invalid or excessive count.");
            return count;
        }
        public int Index(int count)
        {
            var index = Int32();
            if ((uint)index >= (uint)count)
                throw new InvalidDataException("Runtime catalog contains an invalid table reference.");
            return index;
        }
        public string String(string[] strings) => strings[Index(strings.Length)];
        public List<string> BundleReferences(string[] strings, ref int references)
        {
            var count = Count(MaximumCatalogReferences - references, 4);
            references += count;
            var names = new List<string>(count);
            for (var i = 0; i < count; i++) names.Add(String(strings));
            return names;
        }
    }
}
