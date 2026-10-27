using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

public static partial class AssetBundleBuilder
{
    public static AssetBundleBuildResult BuildPackages(
        ProjectWorkspace workspace, ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundlePackageBuildDefinition> packages, AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Build(workspace, assetDatabase, FlattenPackageDefinitions(packages), options, progress, cancellationToken);

    public static Task<AssetBundleBuildResult> BuildPackagesAsync(
        ProjectWorkspace workspace, ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundlePackageBuildDefinition> packages, AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null, CancellationToken cancellationToken = default) =>
        BuildAsync(workspace, assetDatabase, FlattenPackageDefinitions(packages), options, progress, cancellationToken);

    internal static IReadOnlyList<AssetBundleBuildDefinition> FlattenPackageDefinitions(
        IEnumerable<AssetBundlePackageBuildDefinition> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var result = new List<AssetBundleBuildDefinition>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            ArgumentNullException.ThrowIfNull(package);
            ValidateIdentifier(package.Name, "assetbundle package name");
            if (!names.Add(package.Name))
                throw new InvalidDataException($"Duplicate assetbundle package '{package.Name}'.");
            if (package.Bundles is null || package.Bundles.Count == 0)
                throw new InvalidDataException($"Assetbundle package '{package.Name}' has no bundles.");
            if (package.Bundles.Any(static bundle => bundle is null))
                throw new InvalidDataException($"Assetbundle package '{package.Name}' contains a null bundle.");
            var localNames = package.Bundles.Select(static bundle => bundle.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in package.Bundles)
            {
                ValidateIdentifier(bundle.Name, "package-local bundle name");
                result.Add(new AssetBundleBuildDefinition
                {
                    Name = package.Name + "--" + bundle.Name,
                    PackageName = package.Name,
                    AssetPaths = bundle.AssetPaths,
                    Dependencies = bundle.Dependencies.Select(dependency => localNames.Contains(dependency)
                        ? package.Name + "--" + dependency : dependency).ToArray()
                });
            }
        }
        return result;
    }

    private static string ComputeBundleFingerprint(
        AssetBundleBuildDefinition definition, IReadOnlyList<AssetBundleBuildAsset> assets,
        AssetBundleBuildOptions options)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            // A length prefix makes arbitrary importer settings unambiguous.
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        Add("BEngine.AssetBundleBuildCache/v1");
        Add(typeof(AssetBundleBuilder).Assembly.ManifestModule.ModuleVersionId.ToString("D"));
        Add(typeof(AssetBundleCatalog).Assembly.ManifestModule.ModuleVersionId.ToString("D"));
        Add(Environment.Version.ToString());
        Add(options.TargetPlatform?.ToString() ?? "unspecified");
        Add(options.CompressionMode.ToString());
        Add(options.EncryptionKey?.Fingerprint ?? "unencrypted");
        Add(options.PackageName);
        Add(definition.PackageName);
        Add(definition.Name);
        Add(definition.Dependencies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var dependency in definition.Dependencies.Order(StringComparer.Ordinal)) Add(dependency);
        Add("assets");
        Add(assets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var asset in assets)
        {
            Add(asset.Address); Add(asset.Entry); Add(asset.Guid.ToString("D"));
            Add(asset.OwnerGuid.ToString("D"));
            Add(asset.LocalIdentifier.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Add(asset.ArtifactHash);
            Add(asset.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Add(asset.AssetType); Add(asset.Importer);
            Add(asset.ImporterSettings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var setting in asset.ImporterSettings.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            { Add(setting.Key); Add(setting.Value); }
            Add("end-asset");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static AssetBundleDescriptor? TryReuseCachedBundle(
        string cacheRoot, string fingerprint, string stagingBundles,
        AssetBundleBuildDefinition definition, FileUtility files, PathUtility paths,
        CancellationToken cancellationToken)
    {
        var recordPath = paths.Combine(cacheRoot, "records", fingerprint + ".yaml");
        if (!files.FileExists(recordPath)) return null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = YamlUtility.Deserialize<AssetBundleCachedBuildRecord>(files.ReadAllText(recordPath));
            if (record is null || record.Fingerprint != fingerprint) return null;
            var descriptor = record.Descriptor;
            if (descriptor is null || descriptor.Name != definition.Name || descriptor.Size <= 0 ||
                descriptor.Sha256 is null ||
                descriptor.Sha256.Length != 64 || !descriptor.Sha256.All(char.IsAsciiHexDigit) ||
                descriptor.FileName != descriptor.Sha256.ToLowerInvariant() + ".bassetbundle") return null;
            var cachedPath = paths.Combine(cacheRoot, "objects", descriptor.FileName);
            if (!files.FileExists(cachedPath) || files.GetFileLength(cachedPath) != descriptor.Size) return null;
            var stagedPath = paths.Combine(stagingBundles, descriptor.FileName);
            // Verify the staged bytes as well: cached input may change between checking and copying.
            CopyVerifiedBundle(cachedPath, stagedPath, descriptor, files, cancellationToken);
            descriptor.Dependencies = definition.Dependencies.Order(StringComparer.Ordinal).ToList();
            return descriptor;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            ArgumentException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static void CacheBuiltBundle(
        string cacheRoot, string fingerprint, string stagingBundles, AssetBundleDescriptor descriptor,
        FileUtility files, PathUtility paths, CancellationToken cancellationToken)
    {
        var objects = paths.Combine(cacheRoot, "objects");
        var records = paths.Combine(cacheRoot, "records");
        files.CreateDirectory(objects);
        files.CreateDirectory(records);
        var objectPath = paths.Combine(objects, descriptor.FileName);
        var temporaryObject = objectPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var recordPath = paths.Combine(records, fingerprint + ".yaml");
        var temporaryRecord = recordPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            CopyVerifiedBundle(paths.Combine(stagingBundles, descriptor.FileName), temporaryObject,
                descriptor, files, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            files.MoveFile(temporaryObject, objectPath, overwrite: true);
            files.WriteAllText(temporaryRecord, YamlUtility.Serialize(new AssetBundleCachedBuildRecord
                { Fingerprint = fingerprint, Descriptor = descriptor }));
            cancellationToken.ThrowIfCancellationRequested();
            files.MoveFile(temporaryRecord, recordPath, overwrite: true);
        }
        finally
        {
            if (files.FileExists(temporaryObject)) files.DeleteFile(temporaryObject);
            if (files.FileExists(temporaryRecord)) files.DeleteFile(temporaryRecord);
        }
    }

    private static void CopyVerifiedBundle(
        string sourcePath, string destinationPath, AssetBundleDescriptor descriptor,
        FileUtility files, CancellationToken cancellationToken)
    {
        try
        {
            using (var source = files.OpenRead(sourcePath))
            using (var destination = files.OpenFile(destinationPath, FileMode.Create,
                       FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);
                long total = 0;
                try
                {
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        total = checked(total + read);
                        if (total > descriptor.Size) throw new InvalidDataException("Cached bundle exceeds its declared size.");
                        destination.Write(buffer, 0, read);
                        hash.AppendData(buffer, 0, read);
                    }
                    if (total != descriptor.Size || !Convert.ToHexString(hash.GetHashAndReset())
                            .Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Cached bundle failed SHA256 verification.");
                }
                finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var copied = files.OpenRead(destinationPath);
            if (files.GetFileLength(destinationPath) != descriptor.Size ||
                !AssetBundleCatalogSerializer.ComputeSha256(copied).Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Copied bundle failed SHA256 verification.");
        }
        catch
        {
            if (files.FileExists(destinationPath)) files.DeleteFile(destinationPath);
            throw;
        }
    }

    private static string GetBuildBaselinePath(string cacheRoot, AssetBundleBuildOptions options, PathUtility paths)
    {
        var configuration = $"baseline-v2\0{typeof(AssetBundleBuilder).Assembly.ManifestModule.ModuleVersionId:D}\0" +
            $"{typeof(AssetBundleCatalog).Assembly.ManifestModule.ModuleVersionId:D}\0{Environment.Version}\0" +
            $"{options.TargetPlatform}\0{options.CompressionMode}\0{options.EncryptionKey?.Fingerprint}";
        return paths.Combine(cacheRoot, "baselines", AssetBundleCatalogSerializer.ComputeSha256(
            Encoding.UTF8.GetBytes(configuration)) + ".bin");
    }

    private static void ValidateUnselectedPackages(
        string baselinePath, AssetBundleCatalog catalog, HashSet<string>? selected, FileUtility files)
    {
        if (selected is null) return;
        if (!files.FileExists(baselinePath))
            throw new InvalidDataException("Build all assetbundle packages once before selecting individual packages.");
        var previous = AssetBundleCatalogSerializer.DeserializeCatalog(files.ReadAllBytes(baselinePath));
        var previousPackages = previous.Packages.ToDictionary(static package => package.Name, StringComparer.OrdinalIgnoreCase);
        var currentPackages = catalog.Packages.ToDictionary(static package => package.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in previousPackages.Keys.Concat(currentPackages.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (selected.Contains(name)) continue;
            if (!previousPackages.TryGetValue(name, out var before) || !currentPackages.TryGetValue(name, out var after) ||
                !before.Version.Equals(after.Version, StringComparison.Ordinal))
                throw new InvalidDataException($"Unselected assetbundle package '{name}' was added, removed or changed. " +
                    "Include it in this build, or build all packages.");
        }
    }

    private static void SaveBuildBaseline(string baselinePath, byte[] catalogBytes,
        FileUtility files, PathUtility paths, CancellationToken cancellationToken)
    {
        files.CreateDirectory(paths.GetDirectoryName(baselinePath)!);
        var temporaryPath = baselinePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            files.WriteAllBytes(temporaryPath, catalogBytes);
            cancellationToken.ThrowIfCancellationRequested();
            files.MoveFile(temporaryPath, baselinePath, overwrite: true);
        }
        finally { if (files.FileExists(temporaryPath)) files.DeleteFile(temporaryPath); }
    }
}

internal sealed class AssetBundleCachedBuildRecord
{
    public string Fingerprint { get; set; } = string.Empty;
    public AssetBundleDescriptor? Descriptor { get; set; }
}
