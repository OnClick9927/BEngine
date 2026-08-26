using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using BEngine.AssetBundles;
using BEngine.Documents;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;
using ProjectAssetMetaDocument = BEngine.ProjectSystem.Editor.AssetMetaDocument;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;

namespace BEngine.Editor;

public static class AssetBundleBuilder
{
    private static readonly DateTimeOffset StableArchiveTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static AssetBundleBuildResult Build(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var input = CaptureInput(workspace, assetDatabase, definitions, options, progress, cancellationToken);
        return BuildCore(input.Workspace, input.Definitions, input.Assets, input.Options, progress,
            cancellationToken);
    }

    public static Task<AssetBundleBuildResult> BuildAsync(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var input = CaptureInput(workspace, assetDatabase, definitions, options, progress, cancellationToken);
        return Task.Run(() => BuildCore(input.Workspace, input.Definitions, input.Assets, input.Options,
            progress, cancellationToken), cancellationToken);
    }

    private static (
        ProjectWorkspace Workspace,
        AssetBundleBuildDefinition[] Definitions,
        AssetBundleBuildAsset[] Assets,
        AssetBundleBuildOptions Options) CaptureInput(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetDatabase);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Preparing, 0, 1,
            "Capturing asset database"));

        ValidateIdentifier(options.PackageName, nameof(options.PackageName));
        ValidateVersion(options.Version);
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("Asset bundle output directory is required.", nameof(options));

        var sourceDefinitions = definitions.ToArray();
        if (sourceDefinitions.Length == 0)
            throw new ArgumentException("At least one asset bundle definition is required.", nameof(definitions));
        if (sourceDefinitions.Any(item => item is null))
            throw new ArgumentException("Asset bundle definitions cannot contain null entries.", nameof(definitions));

        var definitionsByName = new Dictionary<string, AssetBundleBuildDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in sourceDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateIdentifier(definition.Name, "bundle name");
            if (!definitionsByName.TryAdd(definition.Name, definition))
                throw new InvalidDataException($"Duplicate asset bundle name '{definition.Name}'.");
        }

        var normalizedDefinitions = sourceDefinitions.Select(definition => new AssetBundleBuildDefinition
        {
            Name = definition.Name,
            AssetPaths = definition.AssetPaths.Select(NormalizeSelectionPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Dependencies = definition.Dependencies.Select(dependency =>
            {
                ValidateIdentifier(dependency, $"dependency of '{definition.Name}'");
                if (!definitionsByName.TryGetValue(dependency, out var resolved))
                    throw new InvalidDataException(
                        $"Bundle '{definition.Name}' depends on missing bundle '{dependency}'.");
                return resolved.Name;
            }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal).ToArray()
        }).OrderBy(definition => definition.Name, StringComparer.Ordinal).ToArray();
        ValidateDependencyGraph(normalizedDefinitions);

        var assetsRoot = Path.GetFullPath(workspace.AssetsPath);
        var eligible = assetDatabase.assets.Where(IsRuntimeAsset)
            .OrderBy(record => record.AssetPath, StringComparer.Ordinal)
            .ToArray();
        var assignedAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var capturedAssets = new Dictionary<string, AssetBundleBuildAsset>(StringComparer.OrdinalIgnoreCase);
        var expandedDefinitions = new List<AssetBundleBuildDefinition>(normalizedDefinitions.Length);

        foreach (var definition in normalizedDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (definition.AssetPaths.Count == 0)
                throw new InvalidDataException($"Bundle '{definition.Name}' does not select any asset paths.");
            var selected = eligible.Where(record => definition.AssetPaths.Any(selection =>
                    IsSelected(record.AssetPath, selection)))
                .DistinctBy(record => record.AssetPath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(record => record.AssetPath, StringComparer.Ordinal)
                .ToArray();
            if (selected.Length == 0)
                throw new InvalidDataException(
                    $"Bundle '{definition.Name}' does not contain any runtime-compatible assets.");

            foreach (var record in selected)
            {
                if (assignedAssets.TryGetValue(record.AssetPath, out var owner))
                    throw new InvalidDataException(
                        $"Asset '{record.AssetPath}' is assigned to both '{owner}' and '{definition.Name}'.");
                assignedAssets.Add(record.AssetPath, definition.Name);
                capturedAssets.Add(record.AssetPath, CaptureAsset(record, assetsRoot));
            }
            expandedDefinitions.Add(new AssetBundleBuildDefinition
            {
                Name = definition.Name,
                AssetPaths = selected.Select(record => NormalizeAssetAddress(record.AssetPath)).ToArray(),
                Dependencies = definition.Dependencies.ToArray()
            });
        }

        var outputPath = Path.GetFullPath(options.OutputDirectory);
        if (IsSameOrChildPath(outputPath, assetsRoot))
            throw new InvalidDataException("Asset bundles cannot be built inside the project's Assets directory.");
        var capturedOptions = new AssetBundleBuildOptions
        {
            PackageName = options.PackageName,
            Version = options.Version,
            OutputDirectory = outputPath
        };
        progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Preparing, 1, 1,
            $"Captured {capturedAssets.Count} assets"));
        return (workspace, expandedDefinitions.ToArray(), capturedAssets.Values.ToArray(), capturedOptions);
    }

    private static AssetBundleBuildAsset CaptureAsset(ProjectAssetRecord record, string assetsRoot)
    {
        var sourcePath = Path.GetFullPath(record.SourcePath);
        if (!IsSameOrChildPath(sourcePath, assetsRoot) || sourcePath.Equals(assetsRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset source escapes the project Assets directory: {record.AssetPath}");
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Asset source does not exist: {record.AssetPath}", sourcePath);
        if (record.Guid == Guid.Empty)
            throw new InvalidDataException($"Asset '{record.AssetPath}' has an empty GUID.");
        ValidateSha256(record.SourceHash, $"source hash of '{record.AssetPath}'");
        if (string.IsNullOrWhiteSpace(record.AssetType))
            throw new InvalidDataException($"Asset '{record.AssetPath}' has no asset type.");
        if (!File.Exists(record.MetaPath))
            throw new FileNotFoundException($"Asset metadata does not exist: {record.AssetPath}", record.MetaPath);
        var meta = Document.Load<ProjectAssetMetaDocument>(record.MetaPath);
        if (!Guid.TryParse(meta.Guid, out var metaGuid) || metaGuid != record.Guid)
            throw new InvalidDataException($"Asset metadata GUID changed after the AssetDatabase snapshot: {record.AssetPath}");
        return new AssetBundleBuildAsset
        {
            Guid = record.Guid,
            AssetPath = NormalizeAssetAddress(record.AssetPath),
            SourcePath = sourcePath,
            AssetType = record.AssetType,
            Importer = meta.Importer?.Trim() ?? string.Empty,
            ImporterSettings = new Dictionary<string, string>(meta.Settings ?? [], StringComparer.Ordinal),
            SourceHash = record.SourceHash.ToLowerInvariant(),
            Size = new FileInfo(sourcePath).Length
        };
    }

    private static AssetBundleBuildResult BuildCore(
        ProjectWorkspace workspace,
        IReadOnlyList<AssetBundleBuildDefinition> definitions,
        IReadOnlyList<AssetBundleBuildAsset> assets,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        _ = workspace;
        cancellationToken.ThrowIfCancellationRequested();
        var packageDirectory = Path.Combine(options.OutputDirectory, options.PackageName);
        var versionDirectory = Path.Combine(packageDirectory, options.Version);
        Directory.CreateDirectory(packageDirectory);
        var stagingRoot = Path.Combine(packageDirectory, ".staging");
        Directory.CreateDirectory(stagingRoot);
        var stagingDirectory = Path.Combine(stagingRoot, $"{options.Version}.{Guid.NewGuid():N}");
        var stagingBundles = Path.Combine(stagingDirectory, "bundles");

        try
        {
            Directory.CreateDirectory(stagingBundles);
            var assetsByPath = assets.ToDictionary(asset => asset.AssetPath, StringComparer.OrdinalIgnoreCase);
            var catalog = new AssetBundleCatalog
            {
                PackageName = options.PackageName,
                Version = options.Version
            };
            for (var index = 0; index < definitions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = definitions[index];
                progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.BuildingBundles, index,
                    definitions.Count, definition.Name));
                var bundleAssets = definition.AssetPaths.Select(path => assetsByPath[path])
                    .OrderBy(asset => asset.AssetPath, StringComparer.Ordinal).ToArray();
                var descriptor = WriteBundle(stagingBundles, index, definition, bundleAssets, cancellationToken);
                catalog.Bundles.Add(descriptor);
                catalog.Assets.AddRange(bundleAssets.Select(asset => new AssetBundleAsset
                {
                    Address = asset.AssetPath,
                    Guid = asset.Guid,
                    Bundle = definition.Name,
                    Entry = asset.AssetPath,
                    AssetType = asset.AssetType,
                    Importer = asset.Importer,
                    ImporterSettings = new Dictionary<string, string>(asset.ImporterSettings,
                        StringComparer.Ordinal),
                    Sha256 = asset.SourceHash,
                    Size = asset.Size
                }));
            }
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.BuildingBundles,
                definitions.Count, definitions.Count));

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.WritingCatalog, 0, 1,
                "catalog.json"));
            var catalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(catalog);
            var catalogHash = AssetBundleCatalogSerializer.ComputeSha256(catalogBytes);
            File.WriteAllBytes(Path.Combine(stagingDirectory, "catalog.json"), catalogBytes);
            var version = new AssetBundleVersion
            {
                PackageName = options.PackageName,
                Version = options.Version,
                CatalogFile = "catalog.json",
                CatalogSha256 = catalogHash,
                CatalogSize = catalogBytes.LongLength
            };
            var versionBytes = AssetBundleCatalogSerializer.SerializeVersion(version);
            File.WriteAllBytes(Path.Combine(stagingDirectory, "version.json"), versionBytes);
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.WritingCatalog, 1, 1,
                "version.json"));

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Publishing, 0, 1,
                options.Version));
            bool reusedExistingVersion;
            using (AcquirePublishLock(packageDirectory, cancellationToken))
            {
                reusedExistingVersion = PublishVersion(stagingDirectory, versionDirectory, catalog, catalogBytes,
                    versionBytes, cancellationToken);
                PublishLatestPointer(packageDirectory, versionBytes);
            }
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Publishing, 1, 1,
                options.Version));
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Completed, 1, 1,
                versionDirectory));
            return new AssetBundleBuildResult(catalog, version, packageDirectory, versionDirectory,
                reusedExistingVersion);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static AssetBundleDescriptor WriteBundle(
        string bundleDirectory,
        int index,
        AssetBundleBuildDefinition definition,
        IReadOnlyList<AssetBundleBuildAsset> assets,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(bundleDirectory, $"{index:D6}.tmp");
        using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var asset in assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(asset.AssetPath, CompressionLevel.NoCompression);
                entry.LastWriteTime = StableArchiveTimestamp;
                entry.ExternalAttributes = 0;
                using var source = new FileStream(asset.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    64 * 1024, FileOptions.SequentialScan);
                using var destination = entry.Open();
                CopyAndVerifyAsset(source, destination, asset, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        string hash;
        using (var bundle = File.OpenRead(temporaryPath))
            hash = AssetBundleCatalogSerializer.ComputeSha256(bundle);
        var fileName = $"{hash}.bassetbundle";
        var finalPath = Path.Combine(bundleDirectory, fileName);
        if (File.Exists(finalPath))
        {
            using var existing = File.OpenRead(finalPath);
            if (!string.Equals(AssetBundleCatalogSerializer.ComputeSha256(existing), hash,
                    StringComparison.Ordinal))
                throw new InvalidDataException($"Content-addressed bundle collision for '{fileName}'.");
            File.Delete(temporaryPath);
        }
        else
        {
            File.Move(temporaryPath, finalPath);
        }
        return new AssetBundleDescriptor
        {
            Name = definition.Name,
            FileName = fileName,
            Sha256 = hash,
            Size = new FileInfo(finalPath).Length,
            Dependencies = definition.Dependencies.OrderBy(value => value, StringComparer.Ordinal).ToList()
        };
    }

    private static void CopyAndVerifyAsset(
        Stream source,
        Stream destination,
        AssetBundleBuildAsset asset,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long total = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                destination.Write(buffer, 0, read);
                total += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != asset.Size || !string.Equals(actualHash, asset.SourceHash, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Asset '{asset.AssetPath}' changed after the AssetDatabase snapshot. Refresh assets and rebuild.");
    }

    private static bool PublishVersion(
        string stagingDirectory,
        string versionDirectory,
        AssetBundleCatalog catalog,
        ReadOnlySpan<byte> catalogBytes,
        ReadOnlySpan<byte> versionBytes,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(versionDirectory))
        {
            VerifyPublishedVersion(versionDirectory, catalog, catalogBytes, versionBytes, cancellationToken);
            return true;
        }
        try
        {
            Directory.Move(stagingDirectory, versionDirectory);
            return false;
        }
        catch (IOException) when (Directory.Exists(versionDirectory))
        {
            VerifyPublishedVersion(versionDirectory, catalog, catalogBytes, versionBytes, cancellationToken);
            return true;
        }
    }

    private static void VerifyPublishedVersion(
        string versionDirectory,
        AssetBundleCatalog catalog,
        ReadOnlySpan<byte> catalogBytes,
        ReadOnlySpan<byte> versionBytes,
        CancellationToken cancellationToken)
    {
        VerifyExactFile(Path.Combine(versionDirectory, "catalog.json"), catalogBytes,
            "The published catalog differs from this immutable version.");
        VerifyExactFile(Path.Combine(versionDirectory, "version.json"), versionBytes,
            "The published version metadata differs from this immutable version.");
        var verifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in catalog.Bundles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!verifiedFiles.Add(descriptor.FileName)) continue;
            var bundlePath = Path.Combine(versionDirectory, "bundles", descriptor.FileName);
            if (!File.Exists(bundlePath) || new FileInfo(bundlePath).Length != descriptor.Size)
                throw new InvalidDataException($"Published bundle is missing or truncated: {descriptor.FileName}");
            using var stream = File.OpenRead(bundlePath);
            var hash = AssetBundleCatalogSerializer.ComputeSha256(stream);
            if (!string.Equals(hash, descriptor.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"Published bundle hash is invalid: {descriptor.FileName}");
        }
    }

    private static void VerifyExactFile(string path, ReadOnlySpan<byte> expected, string error)
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
            throw new InvalidDataException(error);
    }

    private static void PublishLatestPointer(string packageDirectory, ReadOnlySpan<byte> versionBytes)
    {
        var latestPath = Path.Combine(packageDirectory, "latest.json");
        var temporaryPath = Path.Combine(packageDirectory, $".latest.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                stream.Write(versionBytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(latestPath))
            {
                try { File.Replace(temporaryPath, latestPath, null, ignoreMetadataErrors: true); }
                catch (PlatformNotSupportedException) { File.Move(temporaryPath, latestPath, overwrite: true); }
            }
            else
            {
                File.Move(temporaryPath, latestPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static FileStream AcquirePublishLock(string packageDirectory, CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(packageDirectory, ".publish.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(25));
            }
        }
    }

    private static bool IsRuntimeAsset(ProjectAssetRecord record)
    {
        if (record.IsDirectory) return false;
        var path = record.AssetPath.Replace('\\', '/');
        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase)) return false;
        return !path.Split('/').Skip(1).Any(segment =>
            segment.Equals("Editor", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSelected(string assetPath, string selection)
    {
        var normalized = NormalizeAssetAddress(assetPath);
        return normalized.Equals(selection, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(selection.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSelectionPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)) return "Assets";
        return NormalizeAssetAddress(normalized);
    }

    private static string NormalizeAssetAddress(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').Trim('/');
        if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset bundle inputs must be inside Assets: {path}");
        var relative = normalized["Assets/".Length..];
        if (relative.Length == 0 || relative.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException($"Invalid asset path: {path}");
        return "Assets/" + relative;
    }

    private static void ValidateDependencyGraph(IReadOnlyList<AssetBundleBuildDefinition> definitions)
    {
        var byName = definitions.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions) VisitDependency(definition.Name, byName, states, []);
    }

    private static void VisitDependency(
        string name,
        IReadOnlyDictionary<string, AssetBundleBuildDefinition> definitions,
        IDictionary<string, byte> states,
        List<string> path)
    {
        if (states.TryGetValue(name, out var state))
        {
            if (state == 2) return;
            if (state == 1)
                throw new InvalidDataException(
                    $"Asset bundle dependency cycle: {string.Join(" -> ", path.Append(name))}.");
        }
        states[name] = 1;
        path.Add(name);
        foreach (var dependency in definitions[name].Dependencies)
            VisitDependency(dependency, definitions, states, path);
        path.RemoveAt(path.Count - 1);
        states[name] = 2;
    }

    private static void ValidateIdentifier(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value[0] is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') ||
            value.Any(character => character is not (>= 'a' and <= 'z') and
                not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '.' and not '_' and not '-'))
            throw new InvalidDataException(
                $"{fieldName} must use 1-128 ASCII letters, digits, '.', '_' or '-'.");
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version is "." or ".." ||
            version.Any(character => character > 127 || char.IsControl(character) || char.IsWhiteSpace(character) ||
                                     character is '/' or '\\' or ':' ||
                                     Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0) ||
            !version.Equals(version.TrimEnd(' ', '.'), StringComparison.Ordinal))
            throw new InvalidDataException("Asset bundle version must be a safe, non-empty path segment.");
    }

    private static void ValidateSha256(string value, string fieldName)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F')))
            throw new InvalidDataException($"{fieldName} must contain exactly 64 hexadecimal characters.");
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
