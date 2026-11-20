using EditorCapabilities = BEngine.Editor.EditorServices;
using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.Editor.Documents;
using BEngine.Player.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering.Rhi;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.IProjectAssetDatabase;
using ProjectAssetMetaDocument = BEngine.ProjectSystem.Editor.AssetMetaDocument;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;
using AotProjectLayout = BEngine.Editor.Build.ProjectSystem.AotProjectLayout;

namespace BEngine.Editor.Build;

internal static class PlayerContentBuildPipeline
{
    internal const string AotAssemblyName = "AOT";
    internal const string AotAssetRoot = AotProjectLayout.AssetRoot;
    internal const string AotScenePath = AotProjectLayout.SceneAssetPath;

    internal static Task<string> BuildAsync(
        PlayerBuildContext context,
        CancellationToken cancellationToken) =>
        BuildAsync(
            context,
            EditorCapabilities.GetRequiredService<IAssetBundleBuildPipeline>(),
            EditorCapabilities.GetRequiredService<IProjectAssetDatabaseFactory>(),
            EditorCapabilities.GetRequiredService<FileUtility>(),
            EditorCapabilities.GetRequiredService<PathUtility>(),
            EditorCapabilities.GetRequiredService<PlatformPlayerBuildCapabilities>(),
            cancellationToken);

    internal static Task<string> BuildAsync(
        PlayerBuildContext context,
        IAssetBundleBuildPipeline assetBundleBuildPipeline,
        IProjectAssetDatabaseFactory assetDatabaseFactory,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformPlayerBuildCapabilities buildCapabilities,
        CancellationToken cancellationToken) =>
        BuildPartitionAsync(
            context,
            PlayerContentPartition.BuiltInAot,
            assetBundleBuildPipeline,
            assetDatabaseFactory,
            fileSystem,
            pathService,
            buildCapabilities,
            cancellationToken);

    internal static Task<string> BuildHotUpdateAsync(
        PlayerBuildContext context,
        CancellationToken cancellationToken) =>
        BuildHotUpdateAsync(
            context,
            EditorCapabilities.GetRequiredService<IAssetBundleBuildPipeline>(),
            EditorCapabilities.GetRequiredService<IProjectAssetDatabaseFactory>(),
            EditorCapabilities.GetRequiredService<FileUtility>(),
            EditorCapabilities.GetRequiredService<PathUtility>(),
            EditorCapabilities.GetRequiredService<PlatformPlayerBuildCapabilities>(),
            cancellationToken);

    internal static Task<string> BuildHotUpdateAsync(
        PlayerBuildContext context,
        IAssetBundleBuildPipeline assetBundleBuildPipeline,
        IProjectAssetDatabaseFactory assetDatabaseFactory,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformPlayerBuildCapabilities buildCapabilities,
        CancellationToken cancellationToken) =>
        BuildPartitionAsync(
            context,
            PlayerContentPartition.HotUpdate,
            assetBundleBuildPipeline,
            assetDatabaseFactory,
            fileSystem,
            pathService,
            buildCapabilities,
            cancellationToken);

    private static async Task<string> BuildPartitionAsync(
        PlayerBuildContext context,
        PlayerContentPartition partition,
        IAssetBundleBuildPipeline assetBundleBuildPipeline,
        IProjectAssetDatabaseFactory assetDatabaseFactory,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformPlayerBuildCapabilities buildCapabilities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assetBundleBuildPipeline);
        ArgumentNullException.ThrowIfNull(assetDatabaseFactory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(buildCapabilities);
        context.ContentEncryptionKey ??= context.Request.EncryptContent
            ? ContentEncryptionKey.FromPassphrase(context.Request.EncryptionKey) : null;
        if (!context.Request.EnableHotUpdate)
            throw new InvalidDataException(
                "AOT-partitioned Player builds require managed-code hot update support.");
        var workspace = ProjectWorkspace.Open(context.Request.ProjectPath, fileSystem, pathService);
        var database = assetDatabaseFactory.Create(workspace);
        var refresh = database.PrepareRefresh(scan => context.Progress?.Report(new PlayerBuildProgress(
            PlayerBuildPhase.BuildingContent,
            string.IsNullOrWhiteSpace(scan.AssetPath)
                ? "Refreshing assets"
                : $"Importing {scan.AssetPath}",
            0.04f + scan.Ratio * 0.1f)), cancellationToken);
        database.ApplyRefresh(refresh);
        cancellationToken.ThrowIfCancellationRequested();

        var scenes = ResolveBuiltInScenes(context.Request);
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
            partition == PlayerContentPartition.BuiltInAot
                ? "Resolving AOT scene dependencies"
                : "Collecting hot-update content",
            0.16f));
        var projectPartition = PlayerProjectAssetPartition.Collect(
            workspace, database, scenes, fileSystem, pathService, cancellationToken);
        var resourceClosure = projectPartition.ResourcesClosure.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtInRoots = ((((global::System.Collections.Generic.IEnumerable<string>)(scenes.Concat(projectPartition.ResourcesRoots))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems5847, distinctComparer5847) => distinctItems5847.ToHashSet<string>(distinctComparer5847).ToArray()});
        var assetPaths = partition == PlayerContentPartition.BuiltInAot
            ? projectPartition.BuiltInAssets
            : projectPartition.HotUpdateAssets;
        var includedPathSet = assetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedAssets = database.assets.Where(asset =>
                !asset.IsDirectory && includedPathSet.Contains(NormalizeAssetPath(asset.AssetPath)))
            .ToArray();
        var allReleaseInputs = PlayerManagedCodeBuildPipeline.CompileInputs(
            context, workspace, fileSystem, pathService, cancellationToken);
        var releaseInputs = partition == PlayerContentPartition.BuiltInAot
            ? SelectBuiltInReleaseInputs(allReleaseInputs, pathService,
                context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full
                    ? null : assetPaths.Select(path => workspace.ResolveInside(path)))
            : SelectHotUpdateReleaseInputs(allReleaseInputs);
        if (partition == PlayerContentPartition.BuiltInAot &&
            context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full)
        {
            var candidates = AssetBundleBuilder.CollectAllAssets(database, fileSystem, pathService,
                path => IsAotAsset(path) || resourceClosure.Contains(path), cancellationToken);
            var graph = new AssetBundleManagedCodeResourceGraph(workspace, database, builtInRoots,
                candidates, resourceClosure, fileSystem, pathService);
            var reachable = await PlayerManagedCodeBuildPipeline.BuildStartupClosureAsync(
                context, workspace, allReleaseInputs, graph, fileSystem, pathService, cancellationToken).ConfigureAwait(false);
            releaseInputs = reachable.Inputs;
            assetPaths = reachable.AssetPaths;
            var report = context.StrippingReport!;
            includedPathSet = assetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            includedAssets = database.assets.Where(asset => !asset.IsDirectory &&
                includedPathSet.Contains(NormalizeAssetPath(asset.AssetPath))).ToArray();
            PlayerResourceStripper.RecordProjectAssets(database.assets.Where(a => candidates.Contains(a.AssetPath, StringComparer.OrdinalIgnoreCase)),
                includedPathSet, report);
        }
        if (partition == PlayerContentPartition.BuiltInAot)
            PlayerManagedCodeBuildPipeline.CaptureHostAssetRoots(context, workspace, allReleaseInputs,
                projectPartition.BuiltInAssets.Concat(projectPartition.HotUpdateAssets), pathService);
        var graphicsBackends = ResolveGraphicsBackends(context, buildCapabilities);
        releaseInputs = FilterPackageResourcesForBackends(
            releaseInputs, graphicsBackends, pathService);
        if (partition == PlayerContentPartition.BuiltInAot && context.StrippingReport is { } strippingReport)
        {
            releaseInputs = PlayerManagedCodeBuildPipeline.RemoveHostDuplicates(context, releaseInputs);
            var keptResources = releaseInputs.PackageResources.Select(static r => r.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
            {
                var __filteredSource17972 = strippingReport.Resources;
                ArgumentNullException.ThrowIfNull(__filteredSource17972, "source");
                foreach (var entry in __filteredSource17972)
                {
                    if (!(entry.Name.StartsWith("Assets/Packages/", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (entry.Included && !keptResources.Contains(entry.Name))
                    {
                        entry.Included = false;
                        entry.PublishedBytes = 0;
                        entry.Reason = "Not used by selected graphics backends";
                    }
                }
            }
            fileSystem.WriteAllText(context.StrippingReportPath!, YamlUtility.Serialize(strippingReport));
        }
        releaseInputs = PlayerManagedCodeBuildPipeline.PreserveNativeImages(context, releaseInputs, allReleaseInputs);
        releaseInputs = PlayerShaderBuildPipeline.AddArtifacts(context, releaseInputs, includedAssets, cancellationToken);
        var images = await PlayerManagedCodeBuildPipeline.FinalizeImagesAsync(
            context, workspace, releaseInputs, allReleaseInputs, partition == PlayerContentPartition.BuiltInAot,
            projectPartition.BuiltInAssets.Concat(projectPartition.HotUpdateAssets), assetPaths,
            fileSystem, pathService, cancellationToken).ConfigureAwait(false);
        releaseInputs = images.Inputs;
        allReleaseInputs = images.AllInputs;
        var versionScenes = partition == PlayerContentPartition.BuiltInAot
            ? scenes
            : assetPaths.Where(static path =>
                    path.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        var version = partition == PlayerContentPartition.BuiltInAot
            ? ComputeContentVersion(
                includedAssets, releaseInputs, versionScenes, context.Request, partition, fileSystem)
            : BEngine.Player.ContentVersionLabel.NormalizeHotResourceVersion(context.Request.HotResourceVersion);

        var layout = PlayerBuildLayout.Create(workspace, context.StagingDirectory, fileSystem, pathService);
        layout.CreateDataDirectories();
        var sourceSettingsPath = pathService.Combine(
            workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        var settings = fileSystem.FileExists(sourceSettingsPath)
            ? LoadYaml<AssetBundleSettingsDocument>(sourceSettingsPath, fileSystem, pathService)
            : new AssetBundleSettingsDocument();
        settings.Enabled = true;
        settings.PackageName = GetPackageName(settings, workspace);
        settings.BuiltInDirectory = settings.LoadMode == AssetBundleLoadMode.StreamingAssets
            ? (string.IsNullOrWhiteSpace(settings.BuiltInDirectory)
                ? "StreamingAssets/AssetBundles" : settings.BuiltInDirectory)
            : partition == PlayerContentPartition.BuiltInAot ? string.Empty : "AssetBundles";
        settings.CheckForUpdatesOnStartup =
            settings.LoadMode != AssetBundleLoadMode.StreamingAssets &&
            context.Request.ContentUpdatePolicy != PlayerContentUpdatePolicy.Disabled;
        settings.ApplyUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
        settings.FailStartupWhenUpdateFails =
            context.Request.ContentUpdatePolicy == PlayerContentUpdatePolicy.RequireLatest;
        WriteRuntimeMetadata(
            workspace, layout.ResourceDirectory, scenes, allReleaseInputs, settings,
            fileSystem, pathService);
        if (partition == PlayerContentPartition.BuiltInAot)
        {
            context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
                $"Packing {assetPaths.Count} built-in AOT resources", 0.25f));
            WriteBuiltInResourceArchive(
                layout.AotResourceArchivePath,
                includedAssets,
                releaseInputs,
                context.Request.IncludeDebugSymbols,
                context.Target.Platform,
                fileSystem,
                pathService,
                cancellationToken,
                context.StrippingReport,
                ((BEngine.Content.ContentEncryptionKey?)context.ContentEncryptionKey),
                AssetBundlePlayerBuildOptions.GetCompression(context.Request.ContentOptions));
            if (context.StrippingReport is { } finalReport)
                fileSystem.WriteAllText(context.StrippingReportPath!, YamlUtility.Serialize(finalReport));
            context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
                $"Packed {assetPaths.Count} built-in AOT resources", 0.62f));
            DeleteShaderStaging(context);
            return version;
        }

        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
            $"Packing {assetPaths.Count} {PartitionDisplayName(partition)} assets for {version}", 0.25f));
        // Content publication has a stable artifact location regardless of the
        // eventual Player's read-only or persistent-cache delivery mode.
        var bundleOutput = pathService.Combine(layout.ResourceDirectory, "AssetBundles");
        var bundleProgress = new DirectProgress<AssetBundleBuildProgress>(value =>
            context.Progress?.Report(MapBundleProgress(value)));
        var bundleResult = await assetBundleBuildPipeline.BuildAsync(
            workspace,
            database,
            AssetBundleBuildMap.FromImporters(workspace, database, assetPaths,
                "hotupdate-content", fileSystem, pathService),
            new AssetBundleBuildOptions
            {
                EncryptionKey = ((BEngine.Content.ContentEncryptionKey?)context.ContentEncryptionKey),
                PackageName = settings.PackageName,
                Version = version,
                TargetPlatform = context.Target.Platform,
                OutputDirectory = bundleOutput,
                CacheDirectory = pathService.Combine(workspace.LibraryPath, "AssetBundleBuildCache"),
                CompressionMode = AssetBundlePlayerBuildOptions.GetCompression(context.Request.ContentOptions)
                    ? AssetBundleCompressionMode.Optimal
                    : AssetBundleCompressionMode.None,
                AdditionalBundles = PlayerAssetBundleContentBuilder.Create(
                    releaseInputs, context.Request.IncludeDebugSymbols, fileSystem, cancellationToken)
            },
            bundleProgress,
            cancellationToken).ConfigureAwait(false);
        Debug.Log($"AssetBundle packages for '{settings.PackageName}' {version}: " +
                  $"built {bundleResult.BuiltBundleCount}, reused {bundleResult.ReusedBundleCount} bundles. " +
                  string.Join("; ", bundleResult.Packages.Select(static package =>
                      $"{package.Name}: built {package.BuiltBundleCount}, reused {package.ReusedBundleCount}")));
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
            $"Packed {assetPaths.Count} {PartitionDisplayName(partition)} assets for {version}", 0.62f));
        DeleteShaderStaging(context);
        return version;
    }

    private static void DeleteShaderStaging(PlayerBuildContext context)
    {
        var directory = Path.Combine(context.StagingDirectory, ".shader-artifacts");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private static void WriteBuiltInResourceArchive(
        string outputPath,
        IEnumerable<ProjectAssetRecord> assets,
        RuntimeManagedCodeReleaseInputSet releaseInputs,
        bool includeManagedSymbols,
        BEngine.Build.BuildTargetPlatform targetPlatform,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken,
        PlayerStrippingReport? strippingReport = null,
        ContentEncryptionKey? encryptionKey = null,
        bool compress = true)
    {
        var entries = new List<BuiltInResourceArchiveWriteEntry>();
        var resourceReport = strippingReport?.Resources.ToDictionary(
            static entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets.OrderBy(static asset => asset.AssetPath, StringComparer.Ordinal)
                     .ThenBy(static asset => asset.LocalIdentifier))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = CaptureBuiltInProjectAsset(asset, fileSystem, pathService, targetPlatform, cancellationToken);
            entries.Add(entry);
            if (resourceReport?.TryGetValue(entry.Address, out var publishedResource) == true)
                publishedResource.PublishedBytes = entry.Size;
        }
        CaptureBuiltInPackageResources(
            releaseInputs.PackageResources, entries, fileSystem, pathService, cancellationToken);
        CaptureBuiltInManagedCode(
            releaseInputs.Assemblies,
            entries,
            includeManagedSymbols,
            fileSystem,
            pathService,
            cancellationToken);
        using var engineCancellation = new BCancellationSource();
        using var registration = cancellationToken.Register(
            static state => _ = ((BCancellationSource)state!).Cancel(), engineCancellation);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            BuiltInResourceArchive.Write(
                outputPath, entries, engineCancellation.Token, fileSystem, pathService, encryptionKey,
                compress: PlatformContentBuildPolicy.CompressBuiltInArchive(targetPlatform, compress));
            if (strippingReport is not null)
            {
                using var published = fileSystem.OpenRead(outputPath);
                strippingReport.AotArchiveBytes = published.Length;
                var archive = BuiltInResourceArchive.Open(outputPath, fileSystem, pathService, encryptionKey);
                foreach (var entry in archive.Entries)
                    if (resourceReport?.TryGetValue(entry.Address, out var resource) == true)
                        resource.StoredBytes = entry.StoredSize;
            }
        }
        catch (BOperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static BuiltInResourceArchiveWriteEntry CaptureBuiltInProjectAsset(
        ProjectAssetRecord asset,
        FileUtility fileSystem,
        PathUtility pathService,
        BEngine.Build.BuildTargetPlatform targetPlatform,
        CancellationToken cancellationToken)
    {
        var artifactPath = pathService.GetFullPath(asset.ArtifactPath);
        if (!fileSystem.FileExists(artifactPath))
            throw new FileNotFoundException(
                $"AOT asset artifact does not exist: '{asset.AssetPath}'.", artifactPath);
        if (!fileSystem.FileExists(asset.MetaPath))
            throw new FileNotFoundException(
                $"AOT asset metadata does not exist: '{asset.AssetPath}'.", asset.MetaPath);
        if (asset.Guid == Guid.Empty)
            throw new InvalidDataException($"AOT asset '{asset.AssetPath}' has an empty GUID.");

        var metadata = LoadYaml<ProjectAssetMetaDocument>(asset.MetaPath, fileSystem, pathService);
        if (!Guid.TryParse(metadata.Guid, out var metadataGuid) || metadataGuid != asset.Guid)
            throw new InvalidDataException(
                $"AOT asset metadata GUID changed after refresh: '{asset.AssetPath}'.");
        var ownerGuid = asset.ParentGuid ?? asset.Guid;
        var localIdentifier = asset.ParentGuid.HasValue ? asset.LocalIdentifier : 0;
        if (asset.ParentGuid.HasValue &&
            (!Guid.TryParse(metadata.ParentGuid, out var metadataOwnerGuid) || metadataOwnerGuid != ownerGuid ||
             localIdentifier <= 0 || metadata.LocalIdentifier != localIdentifier))
            throw new InvalidDataException(
                $"AOT sub-asset identity changed after refresh: '{asset.AssetPath}'.");
        if (!asset.ParentGuid.HasValue && asset.LocalIdentifier != 0)
            throw new InvalidDataException(
                $"AOT main asset '{asset.AssetPath}' cannot have a local identifier.");

        var address = localIdentifier > 0
            ? AssetBundleValidation.CreateSubAssetAddress(ownerGuid, localIdentifier)
            : NormalizeAssetPath(asset.AssetPath);
        if (!asset.ParentGuid.HasValue)
        {
            if (PlatformAssetBuildPipeline.TryPrepareRuntimeArtifact(asset.SourcePath, asset.SourceHash,
                    artifactPath, asset.ArtifactHash, asset.AssetType, metadata.Importer ?? string.Empty,
                    metadata.Settings ?? [], targetPlatform, fileSystem, pathService, cancellationToken,
                    out var content, out var settings))
                return BuiltInResourceArchiveWriteEntry.FromMemory(address, content, asset.AssetType,
                    asset.Guid, ownerGuid, localIdentifier, metadata.Importer ?? string.Empty, settings);
        }
        return CaptureFileEntry(
            address,
            artifactPath,
            asset.AssetType,
            asset.ArtifactHash,
            asset.ArtifactSize,
            fileSystem,
            pathService,
            asset.Guid,
            ownerGuid,
            localIdentifier,
            metadata.Importer ?? string.Empty,
            metadata.Settings);
    }

    private static void CaptureBuiltInPackageResources(
        IEnumerable<RuntimePackageResourceInput> resources,
        ICollection<BuiltInResourceArchiveWriteEntry> entries,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        foreach (var resource in resources.OrderBy(static resource => resource.Address, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var guid = CreateStableGuid(resource.Address);
            entries.Add(CaptureFileEntry(
                resource.Address,
                resource.FilePath,
                "PackageResource",
                resource.Sha256,
                resource.Size,
                fileSystem,
                pathService,
                guid,
                guid,
                importer: "BEnginePackageResource"));
        }
    }

    private static void CaptureBuiltInManagedCode(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> assemblies,
        ICollection<BuiltInResourceArchiveWriteEntry> entries,
        bool includeManagedSymbols,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        if (assemblies.Count == 0)
            throw new InvalidDataException("The built-in AOT resource archive has no managed-code input.");

        using var releaseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var manifest = new ManagedCodeReleaseManifest();
        foreach (var assembly in assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assemblyAddress = $"Assets/__BEngine/HotUpdate/{assembly.Name}.dll";
            var assemblyGuid = CreateStableGuid(assemblyAddress);
            entries.Add(CaptureFileEntry(
                assemblyAddress,
                assembly.AssemblyPath,
                "ManagedAssembly",
                assembly.AssemblySha256,
                assembly.AssemblySize,
                fileSystem,
                pathService,
                assemblyGuid,
                assemblyGuid,
                importer: "BEngineHotUpdate"));

            string? symbolsAddress = null;
            if (includeManagedSymbols && assembly.SymbolsPath is { } symbolsPath)
            {
                symbolsAddress = $"Assets/__BEngine/HotUpdate/{assembly.Name}.pdb";
                var symbolsSnapshot = ComputeFileSnapshot(symbolsPath, fileSystem, pathService);
                var symbolsGuid = CreateStableGuid(symbolsAddress);
                entries.Add(CaptureFileEntry(
                    symbolsAddress,
                    symbolsPath,
                    "ManagedSymbols",
                    symbolsSnapshot.Sha256,
                    symbolsSnapshot.Size,
                    fileSystem,
                    pathService,
                    symbolsGuid,
                    symbolsGuid,
                    importer: "BEngineHotUpdate"));
            }

            manifest.Modules.Add(new ManagedCodeModuleManifest
            {
                Name = assembly.Name,
                BuildId = assembly.BuildId,
                AssemblyAddress = assemblyAddress,
                SymbolsAddress = symbolsAddress,
                Dependencies = assembly.Dependencies.ToList()
            });
            releaseHash.AppendData(Encoding.UTF8.GetBytes($"{assembly.Name}\0{assembly.BuildId}\0"));
            releaseHash.AppendData(Convert.FromHexString(assembly.AssemblySha256));
        }
        manifest.ReleaseId = "code-" +
                             Convert.ToHexStringLower(releaseHash.GetHashAndReset())[..24];
        entries.Add(BuiltInResourceArchiveWriteEntry.FromMemory(
            ManagedCodeReleaseManifest.DefaultAddress,
            ManagedCodeReleaseManifestSerializer.SerializeRuntime(manifest, cancellationToken),
            "ManagedCodeReleaseManifest",
            importer: "BEngineHotUpdate"));
    }

    private static (string Sha256, long Size) ComputeFileSnapshot(
        string path,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var fullPath = pathService.GetFullPath(path);
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException("Managed symbols were not found.", fullPath);
        using var stream = fileSystem.OpenRead(fullPath);
        return (Convert.ToHexStringLower(SHA256.HashData(stream)),
            fileSystem.GetFileLength(fullPath));
    }

    private static BuiltInResourceArchiveWriteEntry CaptureFileEntry(
        string address,
        string path,
        string assetType,
        string expectedSha256,
        long expectedSize,
        FileUtility fileSystem,
        PathUtility pathService,
        Guid guid = default,
        Guid ownerGuid = default,
        long localIdentifier = 0,
        string importer = "BuiltInResource",
        IReadOnlyDictionary<string, string>? importerSettings = null)
    {
        var fullPath = pathService.GetFullPath(path);
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException(
                $"Built-in resource '{address}' was not found.", fullPath);
        var content = fileSystem.ReadAllBytes(fullPath);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(content));
        if (content.LongLength != expectedSize ||
            !actualHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Built-in resource '{address}' changed while the Player was being built.");
        // Package resources have no imported asset record, but can still opt into
        // the same runtime compiler as project assets through their registered type.
        var compilerType = assetType == "PackageResource" ? AssetTypeRegistry.Resolve(address) ?? assetType : assetType;
        if (PlatformAssetBuildPipeline.TryPrepareRuntimeArtifact(fullPath, actualHash, fullPath, actualHash,
                compilerType, importer, importerSettings ?? new Dictionary<string, string>(), null,
                fileSystem, pathService, CancellationToken.None, out var compiled, out var settings))
        {
            content = compiled;
            importerSettings = settings;
        }
        return BuiltInResourceArchiveWriteEntry.FromMemory(
            address,
            content,
            assetType,
            guid,
            ownerGuid,
            localIdentifier,
            importer,
            importerSettings);
    }

    private static Guid CreateStableGuid(string address)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(address));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static IReadOnlyList<string> ResolveBuiltInScenes(PlayerBuildRequest request)
    {
        IReadOnlyList<string> requested = request.Scenes.Count == 0
            ? [AotScenePath]
            : request.Scenes;
        var scenes = ((((global::System.Collections.Generic.IEnumerable<string>)(requested.Where(static scene => !string.IsNullOrWhiteSpace(scene)).Select(static scene => NormalizeAssetPath(scene)))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems46478, distinctComparer46478) => distinctItems46478.ToHashSet<string>(distinctComparer46478).ToArray()});
        if (scenes.Length != 1 ||
            !scenes[0].Equals(AotScenePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Scenes In Build must contain only the AOT scene '{AotScenePath}'.");
        return scenes;
    }

    internal static RuntimeManagedCodeReleaseInputSet SelectBuiltInReleaseInputs(
        RuntimeManagedCodeReleaseInputSet source) =>
        SelectBuiltInReleaseInputs(
            source, EditorCapabilities.GetRequiredService<PathUtility>());

    private static RuntimeManagedCodeReleaseInputSet SelectBuiltInReleaseInputs(
        RuntimeManagedCodeReleaseInputSet source, PathUtility pathService, IEnumerable<string>? assetFiles = null) =>
        PlayerManagedCodeBuildPipeline.SelectStartupInputs(source, pathService, assetFiles);

    internal static RuntimeManagedCodeReleaseInputSet SelectHotUpdateReleaseInputs(
        RuntimeManagedCodeReleaseInputSet source) => PlayerManagedCodeBuildPipeline.SelectDeferredInputs(source);

    internal static RuntimeManagedCodeReleaseInputSet FilterPackageResourcesForBackends(
        RuntimeManagedCodeReleaseInputSet source,
        IReadOnlySet<GraphicsBackend> graphicsBackends) =>
        FilterPackageResourcesForBackends(
            source,
            graphicsBackends,
            EditorCapabilities.GetRequiredService<PathUtility>());

    private static RuntimeManagedCodeReleaseInputSet FilterPackageResourcesForBackends(
        RuntimeManagedCodeReleaseInputSet source,
        IReadOnlySet<GraphicsBackend> graphicsBackends,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(graphicsBackends);
        if (graphicsBackends.Count == 0 || graphicsBackends.Contains(GraphicsBackend.Auto))
            throw new InvalidDataException(
                "Player Shader filtering requires one or more concrete graphics backends.");

        var resources = source.PackageResources
            .Where(resource => PlayerPackagedResourceArchiveBuilder.IsShaderResourceSupported(
                resource.FilePath, graphicsBackends, pathService))
            .ToArray();
        var resourceRoots = source.PackageResourceRoots
            .Where(root => resources.Any(resource =>
                IsSameOrChildPath(resource.FilePath, root, pathService)))
            .ToArray();
        return new RuntimeManagedCodeReleaseInputSet(
            source.Assemblies.ToArray(), resources, resourceRoots);
    }

    private static bool IsAotAsset(string path) => PlayerProjectAssetPartition.IsAotAsset(path);

    private static bool IsSameOrChildPath(string path, string root, PathUtility pathService)
    {
        var candidate = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(path));
        var parent = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(root));
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(
                   parent + pathService.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string PartitionDisplayName(PlayerContentPartition partition) => partition switch
    {
        PlayerContentPartition.BuiltInAot => "built-in AOT",
        PlayerContentPartition.HotUpdate => "hot-update",
        _ => throw new ArgumentOutOfRangeException(nameof(partition), partition, null)
    };

    private static void WriteRuntimeMetadata(
        ProjectWorkspace workspace,
        string runtimeRoot,
        IReadOnlyList<string> scenes,
        RuntimeManagedCodeReleaseInputSet releaseInputs,
        AssetBundleSettingsDocument assetBundleSettings,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        fileSystem.CreateDirectory(runtimeRoot);
        var source = workspace.Project;
        var runtimeProject = new RuntimeProjectData
        {
            Name = source.Name,
            StartupScene = scenes[0],
            AssetsDirectory = "Assets",
            FixedDeltaTime = source.FixedDeltaTime,
            Window = new WindowData
            {
                Title = source.Window.Title,
                Width = source.Window.Width,
                Height = source.Window.Height,
                VSync = source.Window.VSync
            }
        };

        var projectSettings = fileSystem.FileExists(workspace.ProjectSettingsFilePath)
            ? LoadYaml<ProjectSettingsData>(
                workspace.ProjectSettingsFilePath, fileSystem, pathService)
            : new ProjectSettingsData();

        var runtimeMetadata = new RuntimeMetadataDocument
        {
            Project = runtimeProject,
            ProjectSettings = projectSettings.ToRuntime(),
            AssetBundles = assetBundleSettings
        };
        RuntimeMetadataSerializer.Save(
            runtimeMetadata,
            pathService.Combine(runtimeRoot, RuntimeMetadataSerializer.FileName),
            fileSystem,
            pathService);
    }

    private static T LoadYaml<T>(
        string path,
        FileUtility fileSystem,
        PathUtility pathService) =>
        YamlUtility.Deserialize<T>(fileSystem.ReadAllText(pathService.GetFullPath(path)));

    private static IReadOnlySet<GraphicsBackend> ResolveGraphicsBackends(
        PlayerBuildContext context,
        PlatformPlayerBuildCapabilities buildCapabilities)
    {
        context.GraphicsBackends = PlayerGraphicsBuildSettings.Resolve(context.Request, context.Target, buildCapabilities);
        if (context.StrippingReport is { } report)
        {
            report.GraphicsBackends = Gather63478(context.GraphicsBackends);
            [global::BEngine.SkipLoopGuard]
            global::System.Collections.Generic.List<string> Gather63478(global::System.Collections.Generic.IEnumerable<global::BEngine.Rendering.Rhi.GraphicsBackend> items63478)
            {
                ArgumentNullException.ThrowIfNull(items63478, "source");
                string Evaluate63478(global::BEngine.Rendering.Rhi.GraphicsBackend b) => b.ToString();
                global::System.ReadOnlySpan<global::BEngine.Rendering.Rhi.GraphicsBackend> values;
                if (items63478.GetType() == typeof(global::BEngine.Rendering.Rhi.GraphicsBackend[]))
                    values = (global::BEngine.Rendering.Rhi.GraphicsBackend[])items63478;
                else if (items63478.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Rendering.Rhi.GraphicsBackend>))
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Rendering.Rhi.GraphicsBackend>)items63478);
                else
                {
                    return global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(items63478, Evaluate63478));
                }

                var result = new global::System.Collections.Generic.List<string>(values.Length);
                global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(result, values.Length);
                var destination = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result);
                for (var index = 0; index < values.Length; index++)
                    destination[index] = Evaluate63478(values[index]);
                return result;
            }
            report.RuntimeShaderCompilerIncluded = PlatformGraphicsBuildPolicy.IncludesRuntimeCompiler(context.GraphicsBackends, context.Request.RuntimeShaderCompilation);
        }
        PlatformGraphicsBuildPolicy.Validate(context.GraphicsBackends,
            context.Request.CompileShaders, context.Request.RuntimeShaderCompilation);
        var backends = context.GraphicsBackends.ToHashSet();
        return backends;
    }

    private static PlayerBuildProgress MapBundleProgress(AssetBundleBuildProgress progress)
    {
        var (start, length) = progress.Phase switch
        {
            AssetBundleBuildPhase.Preparing => (0.25f, 0.06f),
            AssetBundleBuildPhase.BuildingBundles => (0.31f, 0.19f),
            AssetBundleBuildPhase.WritingCatalog => (0.50f, 0.06f),
            AssetBundleBuildPhase.Publishing => (0.56f, 0.05f),
            AssetBundleBuildPhase.Completed => (0.62f, 0f),
            _ => (0.25f, 0f)
        };
        var item = string.IsNullOrWhiteSpace(progress.ItemName) ? progress.Phase.ToString() : progress.ItemName;
        return new PlayerBuildProgress(PlayerBuildPhase.BuildingContent, item,
            Math.Clamp(start + length * progress.Fraction, 0f, 0.62f));
    }

    private static string ComputeContentVersion(
        IEnumerable<BEngine.ProjectSystem.Editor.AssetRecord> assets,
        RuntimeManagedCodeReleaseInputSet releaseInputs,
        IReadOnlyList<string> scenes,
        PlayerBuildRequest request,
        PlayerContentPartition partition,
        FileUtility fileSystem)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var contentFormat = partition == PlayerContentPartition.BuiltInAot
            ? $"built-in-resources-v{BuiltInResourceArchive.CurrentVersion}/sdf-font-runtime-v1"
            : $"catalog-schema-{AssetBundleCatalog.CurrentSchemaVersion}";
        hash.AppendData(Encoding.UTF8.GetBytes(
            $"content-format\0{contentFormat}\0" +
            $"partition\0{partition}\0" +
            $"compression\0{(AssetBundlePlayerBuildOptions.GetCompression(request.ContentOptions) ? AssetBundleCompressionMode.Optimal : AssetBundleCompressionMode.None)}\0" +
            $"hotupdate\0{request.EnableHotUpdate}\0symbols\0{request.IncludeDebugSymbols}\0" +
            $"shader-offline\0{request.CompileShaders}\0shader-runtime\0{request.RuntimeShaderCompilation}\0" +
            $"graphics\0{string.Join(',', request.GraphicsBackends)}\0" +
            $"hot-startup\0{request.HotUpdateStartupScene}\0"));
        foreach (var scene in scenes)
            hash.AppendData(Encoding.UTF8.GetBytes($"scene\0{scene}\0"));
        foreach (var asset in assets.OrderBy(static asset => asset.AssetPath, StringComparer.Ordinal)
                     .ThenBy(static asset => asset.LocalIdentifier))
            hash.AppendData(Encoding.UTF8.GetBytes(
                $"{asset.AssetPath}\0{asset.LocalIdentifier}\0{asset.ArtifactHash}\0{asset.ArtifactSize}\0"));
        if (request.EnableHotUpdate)
            foreach (var assembly in releaseInputs.Assemblies)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(
                    $"{assembly.Name}\0{assembly.BuildId}\0{assembly.AssemblySha256}\0{assembly.AssemblySize}\0"));
                if (request.IncludeDebugSymbols && assembly.SymbolsPath is { } symbolsPath)
                {
                    using var symbols = fileSystem.OpenRead(symbolsPath);
                    hash.AppendData(SHA256.HashData(symbols));
                }
            }
        foreach (var resource in releaseInputs.PackageResources)
            hash.AppendData(Encoding.UTF8.GetBytes(
                $"{resource.Address}\0{resource.Sha256}\0{resource.Size}\0"));
        var value = Convert.ToHexStringLower(hash.GetHashAndReset());
        return $"content-{value[..24]}";
    }

    internal static string GetProjectPackageName(string projectPath)
    {
        var files = EditorCapabilities.GetRequiredService<FileUtility>();
        var paths = EditorCapabilities.GetRequiredService<PathUtility>();
        var workspace = ProjectWorkspace.Open(projectPath, files, paths);
        var source = paths.Combine(workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        var settings = files.FileExists(source)
            ? LoadYaml<AssetBundleSettingsDocument>(source, files, paths) : new AssetBundleSettingsDocument();
        return GetPackageName(settings, workspace);
    }

    private static string GetPackageName(AssetBundleSettingsDocument settings, ProjectWorkspace workspace) =>
        NormalizePackageName(string.IsNullOrWhiteSpace(settings.PackageName) ? workspace.Project.Name : settings.PackageName);

    private static string NormalizePackageName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '-');
        var normalized = builder.ToString().Trim('-', '.', '_');
        return (string.IsNullOrWhiteSpace(normalized)
            ? "game"
            : normalized[..Math.Min(128, normalized.Length)]).ToLowerInvariant();
    }

    private static string NormalizeAssetPath(string value) => value.Replace('\\', '/').TrimStart('/');

    private sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private enum PlayerContentPartition
    {
        BuiltInAot,
        HotUpdate
    }
}
