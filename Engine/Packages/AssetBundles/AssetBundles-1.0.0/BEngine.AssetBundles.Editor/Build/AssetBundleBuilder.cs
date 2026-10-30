using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Serialization;
using BEngine.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;
using ProjectAssetMetaDocument = BEngine.ProjectSystem.Editor.AssetMetaDocument;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;

namespace BEngine.Editor;

public static partial class AssetBundleBuilder
{
    /// <summary>Collects runtime assets reachable from roots, including referenced sub-asset owners.</summary>
    public static IReadOnlyList<string> CollectDependencies(
        ProjectWorkspace workspace, ProjectAssetDatabase database, IReadOnlyList<string> assetPaths,
        FileUtility fileSystem, PathUtility pathService, bool includeImplicitRuntimeRoots = true,
        CancellationToken cancellationToken = default) =>
        AssetDependencyCollector.Collect(workspace, database, assetPaths, fileSystem, pathService,
            includeImplicitRuntimeRoots, cancellationToken);

    /// <summary>Collects runtime assets, excluding scripts, editor assets and import metadata.</summary>
    public static IReadOnlyList<string> CollectAllAssets(
        ProjectAssetDatabase database, FileUtility fileSystem, PathUtility pathService,
        Func<string, bool>? predicate = null, CancellationToken cancellationToken = default) =>
        AssetDependencyCollector.CollectAll(database, fileSystem, pathService, predicate, cancellationToken);

    private static readonly DateTimeOffset StableArchiveTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static AssetBundleBuildResult Build(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => EditorServiceAccess.GetRequiredService<IAssetBundleBuildPipeline>().Build(
            workspace, assetDatabase, definitions, options, progress, cancellationToken);

    public static Task<AssetBundleBuildResult> BuildAsync(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => EditorServiceAccess.GetRequiredService<IAssetBundleBuildPipeline>().BuildAsync(
            workspace, assetDatabase, definitions, options, progress, cancellationToken);

    internal static AssetBundleBuildResult BuildCore(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        FileUtility fileSystem,
        PathUtility pathService,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var input = CaptureInput(workspace, assetDatabase, definitions, options, fileSystem, pathService,
            progress, cancellationToken);
        return BuildCapturedInput(input.Workspace, input.Definitions, input.Assets, input.Options, fileSystem,
            pathService, progress, cancellationToken);
    }

    internal static Task<AssetBundleBuildResult> BuildCoreAsync(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        FileUtility fileSystem,
        PathUtility pathService,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var input = CaptureInput(workspace, assetDatabase, definitions, options, fileSystem, pathService,
            progress, cancellationToken);
        return Task.Run(() => BuildCapturedInput(input.Workspace, input.Definitions, input.Assets, input.Options,
            fileSystem, pathService, progress, cancellationToken), cancellationToken);
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
        FileUtility fileSystem,
        PathUtility pathService,
        IProgress<AssetBundleBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetDatabase);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Preparing, 0, 1,
            "Capturing asset database"));
        if (options.EncryptionKey is not null && options.TargetPlatform == BEngine.Build.BuildTargetPlatform.Web)
            throw new NotSupportedException("Encrypted AssetBundles currently require a native Player.");

        ValidateIdentifier(options.PackageName, nameof(options.PackageName));
        ValidateVersion(options.Version);
        ValidateCompressionMode(options.CompressionMode);
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("Asset bundle output directory is required.", nameof(options));

        var sourceDefinitions = definitions.ToArray();
        if (sourceDefinitions.Any(item => item is null))
            throw new ArgumentException("Asset bundle definitions cannot contain null entries.", nameof(definitions));

        var definitionsByName = new Dictionary<string, AssetBundleBuildDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in sourceDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateIdentifier(definition.Name, "bundle name");
            ValidateIdentifier(definition.PackageName, "assetbundle package name");
            if (definition.PackageName.Equals("bengine", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Assetbundle package 'bengine' is reserved for generated engine content.");
            if (!definitionsByName.TryAdd(definition.Name, definition))
                throw new InvalidDataException($"Duplicate asset bundle name '{definition.Name}'.");
        }

        var normalizedDefinitions = sourceDefinitions.Select(definition => new AssetBundleBuildDefinition
        {
            Name = definition.Name,
            PackageName = definition.PackageName,
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

        var assetsRoot = pathService.GetFullPath(workspace.AssetsPath);
        var artifactsRoot = pathService.GetFullPath(workspace.AssetArtifactsPath);
        var eligible = assetDatabase.assets.Where(IsRuntimeAsset)
            .OrderBy(record => record.AssetPath, StringComparer.Ordinal)
            .ThenBy(record => record.ParentGuid.HasValue ? 1 : 0)
            .ThenBy(record => record.LocalIdentifier)
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
                .DistinctBy(AssetIdentityKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(record => record.AssetPath, StringComparer.Ordinal)
                .ThenBy(record => record.ParentGuid.HasValue ? 1 : 0)
                .ThenBy(record => record.LocalIdentifier)
                .ToArray();
            if (selected.Length == 0)
                throw new InvalidDataException(
                    $"Bundle '{definition.Name}' does not contain any runtime-compatible assets.");

            var definitionAssets = new List<AssetBundleBuildAsset>(selected.Length);
            foreach (var record in selected)
            {
                var asset = CaptureAsset(record, artifactsRoot, fileSystem, pathService, options.TargetPlatform, cancellationToken);
                if (assignedAssets.TryGetValue(asset.Address, out var owner))
                    throw new InvalidDataException(
                        $"Asset '{asset.Address}' is assigned to both '{owner}' and '{definition.Name}'.");
                assignedAssets.Add(asset.Address, definition.Name);
                capturedAssets.Add(asset.Address, asset);
                definitionAssets.Add(asset);
            }
            expandedDefinitions.Add(new AssetBundleBuildDefinition
            {
                Name = definition.Name,
                PackageName = definition.PackageName,
                AssetPaths = definitionAssets.Select(asset => asset.Address).ToArray(),
                Dependencies = definition.Dependencies.ToArray()
            });
        }

        var assetDependencies = AssetDependencyCollector.CollectDirectDependencies(
            assetDatabase, fileSystem, pathService, cancellationToken);
        for (var index = 0; index < expandedDefinitions.Count; index++)
        {
            var definition = expandedDefinitions[index];
            var dependencies = new HashSet<string>(definition.Dependencies, StringComparer.OrdinalIgnoreCase);
            foreach (var path in definition.AssetPaths)
            {
                if (!assetDependencies.TryGetValue(path, out var references)) continue;
                foreach (var reference in references)
                    if (assignedAssets.TryGetValue(reference, out var owner) &&
                        !owner.Equals(definition.Name, StringComparison.OrdinalIgnoreCase)) dependencies.Add(owner);
            }
            expandedDefinitions[index] = new AssetBundleBuildDefinition
            {
                Name = definition.Name,
                PackageName = definition.PackageName,
                AssetPaths = definition.AssetPaths,
                Dependencies = dependencies.OrderBy(static value => value, StringComparer.Ordinal).ToArray()
            };
        }
        ValidateDependencyGraph(expandedDefinitions.ToArray());

        var releaseInputs = options.ReleaseInputs;
        if ((options.IncludeHotUpdateAssemblies || options.IncludePackageRuntimeResources) &&
            releaseInputs is null)
            releaseInputs = RuntimeManagedCodeReleaseInputCollector.Collect(
                workspace, EditorInstanceContext.current?.scriptAssembliesPath, fileSystem, pathService);
        if (options.IncludePackageRuntimeResources)
        {
            const string packageBundleName = "bengine-packages";
            if (definitionsByName.ContainsKey(packageBundleName))
                throw new InvalidDataException(
                    $"Asset bundle name '{packageBundleName}' is reserved for package runtime resources.");
            var packageAssets = CapturePackageResourceAssets(
                releaseInputs!.PackageResources, cancellationToken);
            if (packageAssets.Count != 0)
            {
                foreach (var asset in packageAssets)
                {
                    if (!assignedAssets.TryAdd(asset.Address, packageBundleName) ||
                        !capturedAssets.TryAdd(asset.Address, asset))
                        throw new InvalidDataException(
                            $"Package resource address '{asset.Address}' collides with another runtime asset.");
                }
                expandedDefinitions.Add(new AssetBundleBuildDefinition
                {
                    Name = packageBundleName,
                    PackageName = "bengine",
                    AssetPaths = packageAssets.Select(asset => asset.Address).ToArray(),
                    Dependencies = []
                });
            }
        }

        if (options.IncludeHotUpdateAssemblies)
        {
            const string codeBundleName = "bengine-hotupdate";
            if (definitionsByName.ContainsKey(codeBundleName))
                throw new InvalidDataException(
                    $"Asset bundle name '{codeBundleName}' is reserved for managed-code hot updates.");
            var codeAssets = CaptureHotUpdateAssets(
                releaseInputs!.Assemblies, options.IncludeManagedSymbols, fileSystem, cancellationToken);
            if (codeAssets.Count != 0)
            {
                foreach (var asset in codeAssets)
                {
                    if (!assignedAssets.TryAdd(asset.Address, codeBundleName) ||
                        !capturedAssets.TryAdd(asset.Address, asset))
                        throw new InvalidDataException(
                            $"HotUpdate asset address '{asset.Address}' collides with a project asset.");
                }
                expandedDefinitions.Add(new AssetBundleBuildDefinition
                {
                    Name = codeBundleName,
                    PackageName = "bengine",
                    AssetPaths = codeAssets.Select(asset => asset.Address).ToArray(),
                    Dependencies = []
                });
            }
        }

        // A Player can keep every project asset in main-package Resources while
        // still publishing its game assemblies/package resources as hot content.
        // Validate the effective input after those generated bundles are captured.
        if (expandedDefinitions.Count == 0)
            throw new ArgumentException(
                "At least one runtime asset or managed-code input is required.", nameof(definitions));

        var outputPath = pathService.GetFullPath(options.OutputDirectory);
        if (IsSameOrChildPath(outputPath, assetsRoot, pathService))
            throw new InvalidDataException("Asset bundles cannot be built inside the project's Assets directory.");
        var cachePath = pathService.GetFullPath(string.IsNullOrWhiteSpace(options.CacheDirectory)
            ? pathService.Combine(workspace.LibraryPath, "AssetBundleBuildCache")
            : options.CacheDirectory);
        if (IsSameOrChildPath(cachePath, assetsRoot, pathService) ||
            IsSameOrChildPath(cachePath, outputPath, pathService) ||
            IsSameOrChildPath(outputPath, cachePath, pathService))
            throw new InvalidDataException("AssetBundle build cache must be outside Assets and the release output tree.");
        var selectedPackages = options.SelectedPackageNames?.ToArray();
        if (selectedPackages is not null)
        {
            if (selectedPackages.Length == 0)
                throw new ArgumentException("Select at least one assetbundle package.", nameof(options));
            var knownPackages = expandedDefinitions.Select(static definition => definition.PackageName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in selectedPackages)
                if (!knownPackages.Contains(name))
                    throw new InvalidDataException($"Unknown assetbundle package '{name}'.");
        }
        var capturedOptions = new AssetBundleBuildOptions
        {
            EncryptionKey = options.EncryptionKey,
            PackageName = options.PackageName,
            Version = options.Version,
            TargetPlatform = options.TargetPlatform,
            OutputDirectory = outputPath,
            CompressionMode = options.CompressionMode,
            IncludeHotUpdateAssemblies = options.IncludeHotUpdateAssemblies,
            IncludeManagedSymbols = options.IncludeManagedSymbols,
            IncludePackageRuntimeResources = options.IncludePackageRuntimeResources,
            CacheDirectory = cachePath,
            ForceRebuild = options.ForceRebuild,
            SelectedPackageNames = selectedPackages,
            ReleaseInputs = releaseInputs
        };
        progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Preparing, 1, 1,
            $"Captured {capturedAssets.Count} assets"));
        return (workspace, expandedDefinitions.ToArray(), capturedAssets.Values.ToArray(), capturedOptions);
    }

    private static IReadOnlyList<AssetBundleBuildAsset> CaptureHotUpdateAssets(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> inputs,
        bool includeManagedSymbols,
        FileUtility fileSystem,
        CancellationToken cancellationToken)
    {
        if (inputs.Count == 0) return [];

        using var releaseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var manifest = new ManagedCodeReleaseManifest();
        var assets = new List<AssetBundleBuildAsset>(inputs.Count * 2 + 1);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assembly = fileSystem.ReadAllBytes(input.AssemblyPath);
            var assemblyHash = AssetBundleCatalogSerializer.ComputeSha256(assembly);
            if (assembly.LongLength != input.AssemblySize ||
                !assemblyHash.Equals(input.AssemblySha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Runtime assembly '{input.Name}' changed after the release input snapshot was captured.");
            var symbols = includeManagedSymbols && input.SymbolsPath is not null
                ? fileSystem.ReadAllBytes(input.SymbolsPath)
                : null;
            var assemblyAddress = $"Assets/__BEngine/HotUpdate/{input.Name}.dll";
            var symbolsAddress = symbols is { Length: > 0 }
                ? $"Assets/__BEngine/HotUpdate/{input.Name}.pdb"
                : null;
            releaseHash.AppendData(Encoding.UTF8.GetBytes($"{input.Name}\0{input.BuildId}\0"));
            releaseHash.AppendData(SHA256.HashData(assembly));
            assets.Add(CreateMemoryAsset(assemblyAddress, assembly, "ManagedAssembly"));
            if (symbolsAddress is not null)
                assets.Add(CreateMemoryAsset(symbolsAddress, symbols!, "ManagedSymbols"));
            manifest.Modules.Add(new ManagedCodeModuleManifest
            {
                Name = input.Name,
                BuildId = input.BuildId,
                AssemblyAddress = assemblyAddress,
                SymbolsAddress = symbolsAddress,
                Dependencies = input.Dependencies.ToList()
            });
        }
        manifest.ReleaseId = "code-" +
                             Convert.ToHexString(releaseHash.GetHashAndReset()).ToLowerInvariant()[..24];
        assets.Add(CreateMemoryAsset(
            ManagedCodeReleaseManifest.DefaultAddress,
            // Generated Player metadata is immutable; avoid parsing editable YAML at startup.
            ManagedCodeReleaseManifestSerializer.SerializeRuntime(manifest, cancellationToken),
            "ManagedCodeReleaseManifest"));
        return assets;
    }

    private static IReadOnlyList<AssetBundleBuildAsset> CapturePackageResourceAssets(
        IReadOnlyList<RuntimePackageResourceInput> inputs,
        CancellationToken cancellationToken)
    {
        var assets = new List<AssetBundleBuildAsset>(inputs.Count);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var guidBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input.Address));
            var guid = new Guid(guidBytes.AsSpan(0, 16));
            assets.Add(new AssetBundleBuildAsset
            {
                Guid = guid,
                OwnerGuid = guid,
                LocalIdentifier = 0,
                Address = input.Address,
                Entry = AssetBundleValidation.CreateOpaquePayloadEntry(input.Sha256),
                ArtifactPath = input.FilePath,
                AssetType = "PackageResource",
                Importer = "BEnginePackageResource",
                ImporterSettings = new Dictionary<string, string>(),
                ArtifactHash = input.Sha256,
                Size = input.Size
            });
        }
        return assets;
    }

    private static AssetBundleBuildAsset CreateMemoryAsset(
        string address,
        byte[] content,
        string assetType)
    {
        var hash = AssetBundleCatalogSerializer.ComputeSha256(content);
        var guidBytes = SHA256.HashData(Encoding.UTF8.GetBytes(address));
        return new AssetBundleBuildAsset
        {
            Guid = new Guid(guidBytes.AsSpan(0, 16)),
            OwnerGuid = new Guid(guidBytes.AsSpan(0, 16)),
            LocalIdentifier = 0,
            Address = address,
            Entry = AssetBundleValidation.CreateOpaquePayloadEntry(hash),
            Content = (byte[])content.Clone(),
            AssetType = assetType,
            Importer = "BEngineHotUpdate",
            ImporterSettings = new Dictionary<string, string>(),
            ArtifactHash = hash,
            Size = content.LongLength
        };
    }

    private static AssetBundleBuildAsset CaptureAsset(
        ProjectAssetRecord record,
        string artifactsRoot,
        FileUtility fileSystem,
        PathUtility pathService,
        BEngine.Build.BuildTargetPlatform? targetPlatform,
        CancellationToken cancellationToken)
    {
        var artifactPath = pathService.GetFullPath(record.ArtifactPath);
        if (!IsSameOrChildPath(artifactPath, artifactsRoot, pathService) || artifactPath.Equals(artifactsRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Asset artifact escapes the AssetDatabase artifact directory: {record.AssetPath}");
        if (!fileSystem.FileExists(artifactPath))
            throw new FileNotFoundException($"Asset artifact does not exist: {record.AssetPath}", artifactPath);
        if (record.Guid == Guid.Empty)
            throw new InvalidDataException($"Asset '{record.AssetPath}' has an empty GUID.");
        ValidateSha256(record.ArtifactHash, $"artifact hash of '{record.AssetPath}'");
        if (record.ArtifactSize < 0)
            throw new InvalidDataException($"Artifact size of '{record.AssetPath}' cannot be negative.");
        if (string.IsNullOrWhiteSpace(record.AssetType))
            throw new InvalidDataException($"Asset '{record.AssetPath}' has no asset type.");
        if (!fileSystem.FileExists(record.MetaPath))
            throw new FileNotFoundException($"Asset metadata does not exist: {record.AssetPath}", record.MetaPath);
        var meta = YamlUtility.Deserialize<ProjectAssetMetaDocument>(fileSystem.ReadAllText(record.MetaPath));
        if (!Guid.TryParse(meta.Guid, out var metaGuid) || metaGuid != record.Guid)
            throw new InvalidDataException($"Asset metadata GUID changed after the AssetDatabase snapshot: {record.AssetPath}");
        var ownerGuid = record.ParentGuid ?? record.Guid;
        var localIdentifier = record.ParentGuid.HasValue ? record.LocalIdentifier : 0;
        if (record.ParentGuid.HasValue &&
            (!Guid.TryParse(meta.ParentGuid, out var metaOwnerGuid) || metaOwnerGuid != ownerGuid ||
             localIdentifier <= 0 || meta.LocalIdentifier != localIdentifier))
            throw new InvalidDataException(
                $"Sub-asset identity changed after the AssetDatabase snapshot: {record.AssetPath}");
        if (!record.ParentGuid.HasValue && record.LocalIdentifier != 0)
            throw new InvalidDataException(
                $"Main asset '{record.AssetPath}' cannot have a local identifier.");
        VerifyArtifactSnapshot(record, artifactPath, fileSystem);
        var projectAddress = NormalizeAssetAddress(record.AssetPath);
        var address = localIdentifier > 0
            ? AssetBundleValidation.CreateSubAssetAddress(ownerGuid, localIdentifier)
            : projectAddress;
        byte[]? content = null;
        IReadOnlyDictionary<string, string> settings = new Dictionary<string, string>(meta.Settings ?? [], StringComparer.Ordinal);
        if (targetPlatform is { } platform && !record.ParentGuid.HasValue &&
            PlatformAssetBuildPipeline.TryBuildArtifact(record.SourcePath, record.SourceHash, artifactPath,
                record.AssetType, meta.Importer ?? string.Empty, settings, platform,
                fileSystem, pathService, cancellationToken, out var encoded, out var effective))
        { content = encoded; settings = effective; }
        if (!record.ParentGuid.HasValue && PlatformAssetBuildPipeline.TryCompileRuntimeArtifact(
                record.SourcePath, record.AssetType, artifactPath, record.ArtifactHash, content, settings,
                fileSystem, cancellationToken, out var compiled, out var compiledSettings))
        { content = compiled; settings = compiledSettings; }
        var hash = content is null ? record.ArtifactHash.ToLowerInvariant() : Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var entry = AssetBundleValidation.CreateOpaquePayloadEntry(hash);
        return new AssetBundleBuildAsset
        {
            Guid = record.Guid,
            OwnerGuid = ownerGuid,
            LocalIdentifier = localIdentifier,
            Address = address,
            Entry = entry,
            ArtifactPath = artifactPath,
            Content = content,
            AssetType = record.AssetType,
            Importer = meta.Importer?.Trim() ?? string.Empty,
            ImporterSettings = settings,
            ArtifactHash = hash,
            Size = content?.LongLength ?? record.ArtifactSize
        };
    }

    private static string AssetIdentityKey(ProjectAssetRecord record)
    {
        var ownerGuid = record.ParentGuid ?? record.Guid;
        var localIdentifier = record.ParentGuid.HasValue ? record.LocalIdentifier : 0;
        return localIdentifier > 0
            ? AssetBundleValidation.CreateSubAssetAddress(ownerGuid, localIdentifier)
            : $"guid:{ownerGuid:N}";
    }

    private static void VerifyArtifactSnapshot(
        ProjectAssetRecord record,
        string artifactPath,
        FileUtility fileSystem)
    {
        if (fileSystem.GetFileLength(artifactPath) != record.ArtifactSize)
            throw new InvalidDataException(
                $"Artifact '{record.AssetPath}' size differs from the AssetDatabase snapshot. " +
                "Refresh assets and rebuild.");
        using var stream = fileSystem.OpenRead(artifactPath);
        var hash = AssetBundleCatalogSerializer.ComputeSha256(stream);
        if (!hash.Equals(record.ArtifactHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Artifact '{record.AssetPath}' hash differs from the AssetDatabase snapshot. " +
                "Refresh assets and rebuild.");
    }

    private static AssetBundleBuildResult BuildCapturedInput(
        ProjectWorkspace workspace,
        IReadOnlyList<AssetBundleBuildDefinition> definitions,
        IReadOnlyList<AssetBundleBuildAsset> assets,
        AssetBundleBuildOptions options,
        FileUtility fileSystem,
        PathUtility pathService,
        IProgress<AssetBundleBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packageDirectory = pathService.Combine(options.OutputDirectory, options.PackageName);
        var versionDirectory = pathService.Combine(packageDirectory, options.Version);
        fileSystem.CreateDirectory(packageDirectory);
        var stagingRoot = pathService.Combine(packageDirectory, ".staging");
        fileSystem.CreateDirectory(stagingRoot);
        var stagingDirectory = pathService.Combine(stagingRoot, $"{options.Version}.{Guid.NewGuid():N}");
        var stagingBundles = pathService.Combine(stagingDirectory, "bundles");

        try
        {
            fileSystem.CreateDirectory(stagingBundles);
            var assetsByAddress = assets.ToDictionary(asset => asset.Address, StringComparer.OrdinalIgnoreCase);
            var catalog = new AssetBundleCatalog
            {
                PackageName = options.PackageName,
                Version = options.Version
            };
            var cacheRoot = pathService.Combine(options.CacheDirectory!, options.PackageName);
            var selectedPackages = options.SelectedPackageNames?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var baselinePath = GetBuildBaselinePath(cacheRoot, options, pathService);
            var packageCounts = new Dictionary<string, (int Built, int Reused)>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < definitions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = definitions[index];
                progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.BuildingBundles, index,
                    definitions.Count, definition.Name));
                var bundleAssets = definition.AssetPaths.Select(address => assetsByAddress[address])
                    .OrderBy(asset => asset.Address, StringComparer.Ordinal).ToArray();
                var selected = selectedPackages is null || selectedPackages.Contains(definition.PackageName);
                var fingerprint = ComputeBundleFingerprint(definition, bundleAssets, options);
                var descriptor = selected && options.ForceRebuild ? null : TryReuseCachedBundle(
                    cacheRoot, fingerprint, stagingBundles, definition, fileSystem, pathService, cancellationToken);
                var reused = descriptor is not null;
                if (descriptor is null)
                {
                    if (!selected)
                        throw new InvalidDataException(
                            $"Unselected assetbundle package '{definition.PackageName}' has changed or has no valid build cache. " +
                            "Include it in this build, or build all packages first.");
                    descriptor = WriteBundle(stagingBundles, index, definition, bundleAssets,
                        ToCompressionLevel(options.CompressionMode), fileSystem, pathService, cancellationToken,
                        options.EncryptionKey);
                    CacheBuiltBundle(cacheRoot, fingerprint, stagingBundles, descriptor,
                        fileSystem, pathService, cancellationToken);
                }
                descriptor.SourceVersion = options.Version;
                packageCounts.TryGetValue(definition.PackageName, out var counts);
                packageCounts[definition.PackageName] = reused
                    ? (counts.Built, counts.Reused + 1) : (counts.Built + 1, counts.Reused);
                progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.BuildingBundles, index + 1,
                    definitions.Count, $"{definition.PackageName}/{definition.Name}: {(reused ? "reused" : "built")}"));
                catalog.Bundles.Add(descriptor);
                catalog.Assets.AddRange(bundleAssets.Select(asset => new AssetBundleAsset
                {
                    Address = asset.Address,
                    Guid = asset.Guid,
                    OwnerGuid = asset.OwnerGuid,
                    LocalIdentifier = asset.LocalIdentifier,
                    Bundle = definition.Name,
                    Entry = asset.Entry,
                    AssetType = asset.AssetType,
                    Importer = asset.Importer,
                    ImporterSettings = new Dictionary<string, string>(asset.ImporterSettings,
                        StringComparer.Ordinal),
                    Sha256 = asset.ArtifactHash,
                    Size = asset.Size
                }));
            }
            foreach (var package in definitions.GroupBy(static definition => definition.PackageName,
                         StringComparer.OrdinalIgnoreCase).OrderBy(static group => group.Key, StringComparer.Ordinal))
            {
                var bundleNames = package.Select(static definition => definition.Name).Order(StringComparer.Ordinal).ToList();
                catalog.Packages.Add(new AssetBundlePackageDescriptor
                {
                    Name = package.Key,
                    Version = AssetBundlePackageDescriptor.ComputeVersion(catalog, bundleNames),
                    Bundles = bundleNames
                });
            }
            ValidateUnselectedPackages(baselinePath, catalog, selectedPackages, fileSystem);
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.BuildingBundles,
                definitions.Count, definitions.Count));

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.WritingCatalog, 0, 1,
                AssetBundleCatalogSerializer.RuntimeCatalogFileName));
            var catalogBytes = AssetBundleCatalogSerializer.SerializeRuntimeCatalog(catalog);
            var catalogHash = AssetBundleCatalogSerializer.ComputeSha256(catalogBytes);
            fileSystem.WriteAllBytes(pathService.Combine(stagingDirectory,
                AssetBundleCatalogSerializer.RuntimeCatalogFileName), catalogBytes);
            var version = new AssetBundleVersion
            {
                PackageName = options.PackageName,
                Version = options.Version,
                CatalogFile = AssetBundleCatalogSerializer.RuntimeCatalogFileName,
                CatalogSha256 = catalogHash,
                CatalogSize = catalogBytes.LongLength
            };
            var versionBytes = AssetBundleCatalogSerializer.SerializeRuntimeVersion(version);
            var latestPointerBytes = AssetBundleCatalogSerializer.SerializeLatestPointer(
                new AssetBundleLatestPointer
                {
                    PackageName = version.PackageName,
                    Version = version.Version
                });
            fileSystem.WriteAllBytes(pathService.Combine(stagingDirectory, "version.yaml"), versionBytes);
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.WritingCatalog, 1, 1,
                "version.yaml"));

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Publishing, 0, 1,
                options.Version));
            bool reusedExistingVersion;
            using (AcquirePublishLock(packageDirectory, fileSystem, pathService, cancellationToken))
            {
                reusedExistingVersion = PublishVersion(stagingDirectory, versionDirectory, catalog, catalogBytes,
                    versionBytes, fileSystem, pathService, cancellationToken);
                SaveBuildBaseline(baselinePath, catalogBytes, fileSystem, pathService, cancellationToken);
                PublishLatestPointer(packageDirectory, latestPointerBytes, fileSystem, pathService);
            }
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Publishing, 1, 1,
                options.Version));
            progress?.Report(new AssetBundleBuildProgress(AssetBundleBuildPhase.Completed, 1, 1,
                versionDirectory));
            return new AssetBundleBuildResult(catalog, version, packageDirectory, versionDirectory,
                reusedExistingVersion)
            {
                BuiltBundleCount = packageCounts.Values.Sum(static counts => counts.Built),
                ReusedBundleCount = packageCounts.Values.Sum(static counts => counts.Reused),
                Packages = packageCounts.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => new AssetBundlePackageBuildResult(pair.Key, pair.Value.Built, pair.Value.Reused))
                    .ToArray()
            };
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory, fileSystem);
            TryDeleteEmptyDirectory(stagingRoot, fileSystem);
        }
    }

    private static AssetBundleDescriptor WriteBundle(
        string bundleDirectory,
        int index,
        AssetBundleBuildDefinition definition,
        IReadOnlyList<AssetBundleBuildAsset> assets,
        CompressionLevel compressionLevel,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken,
        BEngine.Content.ContentEncryptionKey? encryptionKey = null)
    {
        var temporaryPath = pathService.Combine(bundleDirectory, $"{index:D6}.tmp");
        using (var output = fileSystem.OpenFile(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
                   FileShare.None, 4096, FileOptions.None))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
        {
            var writtenEntries = new Dictionary<string, AssetBundleBuildAsset>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (writtenEntries.TryGetValue(asset.Entry, out var existing))
                {
                    if (!asset.Entry.Equals(existing.Entry, StringComparison.Ordinal) ||
                        !asset.ArtifactHash.Equals(existing.ArtifactHash, StringComparison.OrdinalIgnoreCase) ||
                        asset.Size != existing.Size)
                        throw new InvalidDataException(
                            $"Asset bundle entry '{asset.Entry}' maps to conflicting payloads.");
                    using Stream duplicateSource = OpenAssetPayload(asset, fileSystem);
                    CopyAndVerifyAsset(duplicateSource, Stream.Null, asset, cancellationToken);
                    continue;
                }

                writtenEntries.Add(asset.Entry, asset);
                var entry = archive.CreateEntry(asset.Entry, compressionLevel);
                entry.LastWriteTime = StableArchiveTimestamp;
                entry.ExternalAttributes = 0;
                using Stream source = OpenAssetPayload(asset, fileSystem);
                using var destination = entry.Open();
                CopyAndVerifyAsset(source, destination, asset, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (encryptionKey is not null)
        {
            var encryptedPath = temporaryPath + ".encrypted";
            using (var source = fileSystem.OpenRead(temporaryPath))
            using (var output = fileSystem.OpenFile(encryptedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       64 * 1024, FileOptions.SequentialScan))
                BEngine.Content.ContentEncryption.Write(source, output, encryptionKey,
                    checkCancellation: () => cancellationToken.ThrowIfCancellationRequested());
            fileSystem.MoveFile(encryptedPath, temporaryPath, overwrite: true);
        }
        string hash;
        using (var bundle = fileSystem.OpenRead(temporaryPath))
            hash = AssetBundleCatalogSerializer.ComputeSha256(bundle);
        var fileName = $"{hash}.bassetbundle";
        var finalPath = pathService.Combine(bundleDirectory, fileName);
        if (fileSystem.FileExists(finalPath))
        {
            using var existing = fileSystem.OpenRead(finalPath);
            if (!string.Equals(AssetBundleCatalogSerializer.ComputeSha256(existing), hash,
                    StringComparison.Ordinal))
                throw new InvalidDataException($"Content-addressed bundle collision for '{fileName}'.");
            fileSystem.DeleteFile(temporaryPath);
        }
        else
        {
            fileSystem.MoveFile(temporaryPath, finalPath);
        }
        return new AssetBundleDescriptor
        {
            Name = definition.Name,
            FileName = fileName,
            Sha256 = hash,
            Size = fileSystem.GetFileLength(finalPath),
            Dependencies = definition.Dependencies.OrderBy(value => value, StringComparer.Ordinal).ToList()
        };
    }

    private static Stream OpenAssetPayload(AssetBundleBuildAsset asset, FileUtility fileSystem) =>
        asset.Content is { } content
            ? new MemoryStream(content, writable: false)
            : fileSystem.OpenFile(asset.ArtifactPath ??
                                  throw new InvalidDataException($"Asset '{asset.Address}' has no payload."),
                FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);

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
        if (total != asset.Size || !string.Equals(actualHash, asset.ArtifactHash, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Artifact '{asset.Address}' changed after the AssetDatabase snapshot. Refresh assets and rebuild.");
    }

    private static bool PublishVersion(
        string stagingDirectory,
        string versionDirectory,
        AssetBundleCatalog catalog,
        ReadOnlySpan<byte> catalogBytes,
        ReadOnlySpan<byte> versionBytes,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        if (fileSystem.DirectoryExists(versionDirectory))
        {
            VerifyPublishedVersion(versionDirectory, catalog, catalogBytes, versionBytes, fileSystem, pathService,
                cancellationToken);
            return true;
        }
        try
        {
            fileSystem.MoveDirectory(stagingDirectory, versionDirectory);
            return false;
        }
        catch (IOException) when (fileSystem.DirectoryExists(versionDirectory))
        {
            VerifyPublishedVersion(versionDirectory, catalog, catalogBytes, versionBytes, fileSystem, pathService,
                cancellationToken);
            return true;
        }
    }

    private static void VerifyPublishedVersion(
        string versionDirectory,
        AssetBundleCatalog catalog,
        ReadOnlySpan<byte> catalogBytes,
        ReadOnlySpan<byte> versionBytes,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        VerifyExactFile(pathService.Combine(versionDirectory, AssetBundleCatalogSerializer.RuntimeCatalogFileName), catalogBytes, fileSystem,
            "The published catalog differs from this immutable version.");
        VerifyExactFile(pathService.Combine(versionDirectory, "version.yaml"), versionBytes, fileSystem,
            "The published version metadata differs from this immutable version.");
        var verifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in catalog.Bundles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!verifiedFiles.Add(descriptor.FileName)) continue;
            var bundlePath = pathService.Combine(versionDirectory, "bundles", descriptor.FileName);
            if (!fileSystem.FileExists(bundlePath) || fileSystem.GetFileLength(bundlePath) != descriptor.Size)
                throw new InvalidDataException($"Published bundle is missing or truncated: {descriptor.FileName}");
            using var stream = fileSystem.OpenRead(bundlePath);
            var hash = AssetBundleCatalogSerializer.ComputeSha256(stream);
            if (!string.Equals(hash, descriptor.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"Published bundle hash is invalid: {descriptor.FileName}");
        }
    }

    private static void VerifyExactFile(
        string path,
        ReadOnlySpan<byte> expected,
        FileUtility fileSystem,
        string error)
    {
        if (!fileSystem.FileExists(path) || !fileSystem.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
            throw new InvalidDataException(error);
    }

    private static void PublishLatestPointer(
        string packageDirectory,
        ReadOnlySpan<byte> versionBytes,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var latestPath = pathService.Combine(packageDirectory, "latest.yaml");
        var temporaryPath = pathService.Combine(packageDirectory, $".latest.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = fileSystem.OpenFile(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(versionBytes);
                fileSystem.Flush(stream, flushToDisk: true);
            }
            if (fileSystem.FileExists(latestPath))
            {
                try { fileSystem.ReplaceFile(temporaryPath, latestPath, null, ignoreMetadataErrors: true); }
                catch (PlatformNotSupportedException)
                {
                    fileSystem.MoveFile(temporaryPath, latestPath, overwrite: true);
                }
            }
            else
            {
                fileSystem.MoveFile(temporaryPath, latestPath);
            }
        }
        finally
        {
            if (fileSystem.FileExists(temporaryPath)) fileSystem.DeleteFile(temporaryPath);
        }
    }

    private static Stream AcquirePublishLock(
        string packageDirectory,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        var lockPath = pathService.Combine(packageDirectory, ".publish.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return fileSystem.OpenFile(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
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
        if (path.EndsWith("/package.yaml", StringComparison.OrdinalIgnoreCase)) return false;
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
        => AssetBundleVersionLabel.ValidatePortable(version, "Asset bundle version");

    private static void ValidateCompressionMode(AssetBundleCompressionMode compressionMode)
    {
        if (compressionMode is not (AssetBundleCompressionMode.None or AssetBundleCompressionMode.Fast or
            AssetBundleCompressionMode.Optimal))
            throw new ArgumentOutOfRangeException(nameof(compressionMode), compressionMode,
                "Unsupported asset bundle compression mode.");
    }

    private static CompressionLevel ToCompressionLevel(AssetBundleCompressionMode compressionMode) =>
        compressionMode switch
        {
            AssetBundleCompressionMode.None => CompressionLevel.NoCompression,
            AssetBundleCompressionMode.Fast => CompressionLevel.Fastest,
            AssetBundleCompressionMode.Optimal => CompressionLevel.Optimal,
            _ => throw new ArgumentOutOfRangeException(nameof(compressionMode), compressionMode,
                "Unsupported asset bundle compression mode.")
        };

    private static void ValidateSha256(string value, string fieldName)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F')))
            throw new InvalidDataException($"{fieldName} must contain exactly 64 hexadecimal characters.");
    }

    private static bool IsSameOrChildPath(string path, string root, PathUtility pathService)
    {
        var fullPath = pathService.GetFullPath(path).TrimEnd(pathService.DirectorySeparatorChar,
            pathService.AltDirectorySeparatorChar);
        var fullRoot = pathService.GetFullPath(root).TrimEnd(pathService.DirectorySeparatorChar,
            pathService.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + pathService.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string path, FileUtility fileSystem)
    {
        if (!fileSystem.DirectoryExists(path)) return;
        try { fileSystem.DeleteDirectory(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmptyDirectory(string path, FileUtility fileSystem)
    {
        if (!fileSystem.DirectoryExists(path)) return;
        try { fileSystem.DeleteDirectory(path, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

}
