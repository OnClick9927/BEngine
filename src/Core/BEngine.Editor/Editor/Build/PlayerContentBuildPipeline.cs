using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Content;
using BEngine.Editor.Documents;
using BEngine.HotUpdate;
using BEngine.ProjectSystem;
using BEngine.Rendering.Rhi;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;
using ProjectAssetMetaDocument = BEngine.ProjectSystem.Editor.AssetMetaDocument;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;

namespace BEngine.Editor;

internal static class PlayerContentBuildPipeline
{
    internal const string AotAssemblyName = "AOT";
    internal const string AotAssetRoot = "Assets/Aot";
    internal const string AotScenePath = AotAssetRoot + "/AOT.scene.yaml";

    internal static Task<string> BuildAsync(
        PlayerBuildContext context,
        CancellationToken cancellationToken) =>
        BuildPartitionAsync(context, PlayerContentPartition.BuiltInAot, cancellationToken);

    internal static Task<string> BuildHotUpdateAsync(
        PlayerBuildContext context,
        CancellationToken cancellationToken) =>
        BuildPartitionAsync(context, PlayerContentPartition.HotUpdate, cancellationToken);

    private static async Task<string> BuildPartitionAsync(
        PlayerBuildContext context,
        PlayerContentPartition partition,
        CancellationToken cancellationToken)
    {
        if (!context.Request.EnableHotUpdate)
            throw new InvalidDataException(
                "AOT-partitioned Player builds require managed-code hot update support.");
        var workspace = ProjectWorkspace.Open(context.Request.ProjectPath);
        var database = new ProjectAssetDatabase(workspace);
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
        var assetPaths = partition == PlayerContentPartition.BuiltInAot
            ? PlayerAssetDependencyCollector.Collect(
                workspace, database, scenes, includeImplicitRuntimeRoots: false, cancellationToken)
            : PlayerAssetDependencyCollector.CollectAll(
                database, static path => !IsAotAsset(path), cancellationToken);
        if (partition == PlayerContentPartition.BuiltInAot)
            ValidateAotAssetBoundary(assetPaths);
        else if (assetPaths.Count == 0)
            throw new InvalidDataException(
                $"The hot-update partition has no runtime assets outside '{AotAssetRoot}'.");
        var includedPathSet = assetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedAssets = database.assets.Where(asset =>
                !asset.IsDirectory && includedPathSet.Contains(NormalizeAssetPath(asset.AssetPath)))
            .ToArray();
        var allReleaseInputs = RuntimeManagedCodeReleaseInputCollector.Collect(
            workspace, EditorInstanceContext.current?.scriptAssembliesPath);
        context.RuntimeManagedCodeAssemblyPaths = allReleaseInputs.Assemblies
            .Select(static assembly => assembly.AssemblyPath)
            .ToArray();
        var releaseInputs = partition == PlayerContentPartition.BuiltInAot
            ? SelectBuiltInReleaseInputs(allReleaseInputs)
            : SelectHotUpdateReleaseInputs(allReleaseInputs);
        var graphicsBackends = ResolveGraphicsBackends(context);
        releaseInputs = FilterPackageResourcesForBackends(releaseInputs, graphicsBackends);
        var versionScenes = partition == PlayerContentPartition.BuiltInAot
            ? scenes
            : assetPaths.Where(static path =>
                    path.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        var version = partition == PlayerContentPartition.BuiltInAot
            ? ComputeContentVersion(includedAssets, releaseInputs, versionScenes, context.Request, partition)
            : PlayerBuildSettingsStore.NormalizeHotResourceVersion(context.Request.HotResourceVersion);

        var layout = PlayerBuildLayout.Create(workspace, context.StagingDirectory);
        layout.CreateDataDirectories();
        var sourceSettingsPath = Path.Combine(
            workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        var settings = File.Exists(sourceSettingsPath)
            ? YamlUtility.Load<AssetBundleSettingsDocument>(sourceSettingsPath)
            : new AssetBundleSettingsDocument();
        settings.Enabled = true;
        settings.PackageName = NormalizePackageName(
            string.IsNullOrWhiteSpace(settings.PackageName) ? workspace.Project.Name : settings.PackageName);
        settings.BuiltInDirectory = partition == PlayerContentPartition.BuiltInAot
            ? string.Empty
            : "AssetBundles";
        settings.CheckForUpdatesOnStartup =
            context.Request.ContentUpdatePolicy != PlayerContentUpdatePolicy.Disabled;
        settings.ApplyUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
        settings.FailStartupWhenUpdateFails =
            context.Request.ContentUpdatePolicy == PlayerContentUpdatePolicy.RequireLatest;
        WriteRuntimeMetadata(workspace, layout.ResourceDirectory, scenes, allReleaseInputs, settings);
        if (partition == PlayerContentPartition.BuiltInAot)
        {
            context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
                $"Packing {assetPaths.Count} built-in AOT resources", 0.25f));
            WriteBuiltInResourceArchive(
                layout.AotResourceArchivePath,
                includedAssets,
                releaseInputs,
                context.Request.IncludeDebugSymbols,
                cancellationToken);
            context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
                $"Packed {assetPaths.Count} built-in AOT resources", 0.62f));
            return version;
        }

        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
            $"Packing {assetPaths.Count} {PartitionDisplayName(partition)} assets for {version}", 0.25f));
        var bundleOutput = Path.Combine(layout.ResourceDirectory, settings.BuiltInDirectory);
        var bundleProgress = new DirectProgress<AssetBundleBuildProgress>(value =>
            context.Progress?.Report(MapBundleProgress(value)));
        _ = await AssetBundleBuilder.BuildAsync(
            workspace,
            database,
            [new AssetBundleBuildDefinition
            {
                Name = partition == PlayerContentPartition.BuiltInAot ? "aot" : "hotupdate-content",
                AssetPaths = assetPaths
            }],
            new AssetBundleBuildOptions
            {
                PackageName = settings.PackageName,
                Version = version,
                OutputDirectory = bundleOutput,
                CompressionMode = context.Request.CompressAssetBundles
                    ? AssetBundleCompressionMode.Optimal
                    : AssetBundleCompressionMode.None,
                IncludeHotUpdateAssemblies = true,
                IncludeManagedSymbols = context.Request.IncludeDebugSymbols,
                IncludePackageRuntimeResources = releaseInputs.PackageResources.Count != 0,
                ReleaseInputs = releaseInputs
            },
            bundleProgress,
            cancellationToken).ConfigureAwait(false);
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
            $"Packed {assetPaths.Count} {PartitionDisplayName(partition)} assets for {version}", 0.62f));
        return version;
    }

    private static void WriteBuiltInResourceArchive(
        string outputPath,
        IEnumerable<ProjectAssetRecord> assets,
        RuntimeManagedCodeReleaseInputSet releaseInputs,
        bool includeManagedSymbols,
        CancellationToken cancellationToken)
    {
        var entries = new List<BuiltInResourceArchiveWriteEntry>();
        foreach (var asset in assets.OrderBy(static asset => asset.AssetPath, StringComparer.Ordinal)
                     .ThenBy(static asset => asset.LocalIdentifier))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(CaptureBuiltInProjectAsset(asset));
        }
        CaptureBuiltInPackageResources(releaseInputs.PackageResources, entries, cancellationToken);
        CaptureBuiltInManagedCode(
            releaseInputs.Assemblies, entries, includeManagedSymbols, cancellationToken);
        BuiltInResourceArchive.Write(outputPath, entries, cancellationToken);
    }

    private static BuiltInResourceArchiveWriteEntry CaptureBuiltInProjectAsset(ProjectAssetRecord asset)
    {
        var artifactPath = Path.GetFullPath(asset.ArtifactPath);
        if (!File.Exists(artifactPath))
            throw new FileNotFoundException(
                $"AOT asset artifact does not exist: '{asset.AssetPath}'.", artifactPath);
        if (!File.Exists(asset.MetaPath))
            throw new FileNotFoundException(
                $"AOT asset metadata does not exist: '{asset.AssetPath}'.", asset.MetaPath);
        if (asset.Guid == Guid.Empty)
            throw new InvalidDataException($"AOT asset '{asset.AssetPath}' has an empty GUID.");

        var metadata = YamlUtility.Load<ProjectAssetMetaDocument>(asset.MetaPath);
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
        return BuiltInResourceArchiveWriteEntry.FromFile(
            address,
            artifactPath,
            asset.AssetType,
            asset.ArtifactHash,
            asset.ArtifactSize,
            asset.Guid,
            ownerGuid,
            localIdentifier,
            metadata.Importer ?? string.Empty,
            metadata.Settings);
    }

    private static void CaptureBuiltInPackageResources(
        IEnumerable<RuntimePackageResourceInput> resources,
        ICollection<BuiltInResourceArchiveWriteEntry> entries,
        CancellationToken cancellationToken)
    {
        foreach (var resource in resources.OrderBy(static resource => resource.Address, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var guid = CreateStableGuid(resource.Address);
            entries.Add(BuiltInResourceArchiveWriteEntry.FromFile(
                resource.Address,
                resource.FilePath,
                "PackageResource",
                resource.Sha256,
                resource.Size,
                guid,
                guid,
                importer: "BEnginePackageResource"));
        }
    }

    private static void CaptureBuiltInManagedCode(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> assemblies,
        ICollection<BuiltInResourceArchiveWriteEntry> entries,
        bool includeManagedSymbols,
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
            entries.Add(BuiltInResourceArchiveWriteEntry.FromFile(
                assemblyAddress,
                assembly.AssemblyPath,
                "ManagedAssembly",
                assembly.AssemblySha256,
                assembly.AssemblySize,
                assemblyGuid,
                assemblyGuid,
                importer: "BEngineHotUpdate"));

            string? symbolsAddress = null;
            if (includeManagedSymbols && assembly.SymbolsPath is { } symbolsPath)
            {
                symbolsAddress = $"Assets/__BEngine/HotUpdate/{assembly.Name}.pdb";
                var symbolsSnapshot = ComputeFileSnapshot(symbolsPath);
                var symbolsGuid = CreateStableGuid(symbolsAddress);
                entries.Add(BuiltInResourceArchiveWriteEntry.FromFile(
                    symbolsAddress,
                    symbolsPath,
                    "ManagedSymbols",
                    symbolsSnapshot.Sha256,
                    symbolsSnapshot.Size,
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
                             Convert.ToHexString(releaseHash.GetHashAndReset()).ToLowerInvariant()[..24];
        entries.Add(BuiltInResourceArchiveWriteEntry.FromMemory(
            ManagedCodeReleaseManifest.DefaultAddress,
            ManagedCodeReleaseManifestSerializer.Serialize(manifest),
            "ManagedCodeReleaseManifest",
            importer: "BEngineHotUpdate"));
    }

    private static (string Sha256, long Size) ComputeFileSnapshot(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists) throw new FileNotFoundException("Managed symbols were not found.", file.FullName);
        using var stream = file.OpenRead();
        return (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), file.Length);
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
        var scenes = requested.Where(static scene => !string.IsNullOrWhiteSpace(scene))
            .Select(static scene => NormalizeAssetPath(scene))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (scenes.Length != 1 ||
            !scenes[0].Equals(AotScenePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Scenes In Build must contain only the AOT scene '{AotScenePath}'.");
        return scenes;
    }

    internal static RuntimeManagedCodeReleaseInputSet SelectBuiltInReleaseInputs(
        RuntimeManagedCodeReleaseInputSet source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var byName = source.Assemblies.ToDictionary(
            static assembly => assembly.Name, StringComparer.OrdinalIgnoreCase);
        var aot = GetAotAssembly(byName);
        var includedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Include(aot);

        var assemblies = source.Assemblies.Where(assembly => includedNames.Contains(assembly.Name)).ToArray();
        var packageIds = assemblies
            .Where(static assembly =>
                assembly.Origin.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            .Select(static assembly => assembly.Origin["package:".Length..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resources = source.PackageResources
            .Where(resource => packageIds.Contains(resource.PackageId))
            .ToArray();
        var resourceRoots = source.PackageResourceRoots
            .Where(root => resources.Any(resource => IsSameOrChildPath(resource.FilePath, root)))
            .ToArray();
        return new RuntimeManagedCodeReleaseInputSet(assemblies, resources, resourceRoots);

        void Include(RuntimeManagedCodeReleaseInput assembly)
        {
            if (!includedNames.Add(assembly.Name)) return;
            if (assembly.Origin.StartsWith("project:", StringComparison.OrdinalIgnoreCase) &&
                !assembly.Name.Equals(AotAssemblyName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"AOT assembly '{AotAssemblyName}' cannot depend on hot-update project assembly " +
                    $"'{assembly.Name}'. Move the dependency into AOT or an enabled runtime package.");
            foreach (var dependencyName in assembly.Dependencies)
            {
                if (!byName.TryGetValue(dependencyName, out var dependency))
                    throw new InvalidDataException(
                        $"Managed assembly '{assembly.Name}' depends on missing release input " +
                        $"'{dependencyName}'.");
                Include(dependency);
            }
        }
    }

    internal static RuntimeManagedCodeReleaseInputSet SelectHotUpdateReleaseInputs(
        RuntimeManagedCodeReleaseInputSet source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var byName = source.Assemblies.ToDictionary(
            static assembly => assembly.Name, StringComparer.OrdinalIgnoreCase);
        _ = GetAotAssembly(byName);
        foreach (var assembly in source.Assemblies.Where(static assembly =>
                     !assembly.Name.Equals(AotAssemblyName, StringComparison.OrdinalIgnoreCase)))
            if (assembly.Dependencies.Contains(AotAssemblyName, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Hot-update assembly '{assembly.Name}' cannot depend on '{AotAssemblyName}', because the " +
                    "AOT domain is replaced before the hot-update game domain is activated.");

        return new RuntimeManagedCodeReleaseInputSet(
            source.Assemblies.Where(static assembly =>
                    !assembly.Name.Equals(AotAssemblyName, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            source.PackageResources.ToArray(),
            source.PackageResourceRoots.ToArray());
    }

    internal static RuntimeManagedCodeReleaseInputSet FilterPackageResourcesForBackends(
        RuntimeManagedCodeReleaseInputSet source,
        IReadOnlySet<GraphicsBackend> graphicsBackends)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(graphicsBackends);
        if (graphicsBackends.Count == 0 || graphicsBackends.Contains(GraphicsBackend.Auto))
            throw new InvalidDataException(
                "Player Shader filtering requires one or more concrete graphics backends.");

        var resources = source.PackageResources
            .Where(resource => PlayerPackagedResourceArchiveBuilder.IsShaderResourceSupported(
                resource.FilePath, graphicsBackends))
            .ToArray();
        var resourceRoots = source.PackageResourceRoots
            .Where(root => resources.Any(resource => IsSameOrChildPath(resource.FilePath, root)))
            .ToArray();
        return new RuntimeManagedCodeReleaseInputSet(
            source.Assemblies.ToArray(), resources, resourceRoots);
    }

    private static RuntimeManagedCodeReleaseInput GetAotAssembly(
        IReadOnlyDictionary<string, RuntimeManagedCodeReleaseInput> assemblies)
    {
        if (!assemblies.TryGetValue(AotAssemblyName, out var aot) ||
            !aot.Origin.Equals($"project:{AotAssemblyName}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"The project must compile '{AotAssemblyName}' from " +
                $"'{AotAssetRoot}/{AotAssemblyName}.asmdef.yaml' before building content.");
        return aot;
    }

    private static void ValidateAotAssetBoundary(IEnumerable<string> assetPaths)
    {
        var external = assetPaths.Where(static path => !IsAotAsset(path)).ToArray();
        if (external.Length == 0) return;
        throw new InvalidDataException(
            $"The AOT scene may only reference project assets below '{AotAssetRoot}'. Move these assets into " +
            $"the AOT folder: {string.Join(", ", external)}.");
    }

    private static bool IsAotAsset(string path)
    {
        var normalized = NormalizeAssetPath(path).TrimEnd('/');
        return normalized.Equals(AotAssetRoot, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(AotAssetRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
        AssetBundleSettingsDocument assetBundleSettings)
    {
        Directory.CreateDirectory(runtimeRoot);
        var source = workspace.Project;
        var runtimeProject = new ProjectData
        {
            EngineVersion = source.EngineVersion,
            Name = source.Name,
            StartupScene = scenes[0],
            AssetsDirectory = "Assets",
            ScriptsDirectory = "Assets/Scripts",
            EditorScriptsDirectory = "Assets/Editor",
            FixedDeltaTime = source.FixedDeltaTime,
            Window = new WindowData
            {
                Title = source.Window.Title,
                Width = source.Window.Width,
                Height = source.Window.Height,
                VSync = source.Window.VSync
            }
        };

        var projectSettings = File.Exists(workspace.ProjectSettingsFilePath)
            ? YamlUtility.Load<ProjectSettingsData>(workspace.ProjectSettingsFilePath)
            : new ProjectSettingsData();

        var packageIds = releaseInputs.Assemblies
            .Where(static assembly => assembly.Origin.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            .Select(static assembly => assembly.Origin["package:".Length..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceManifest = File.Exists(workspace.PackageManifestPath)
            ? YamlUtility.Load<PackageManifestDocument>(workspace.PackageManifestPath)
            : new PackageManifestDocument();
        var runtimeMetadata = new RuntimeMetadataDocument
        {
            Project = runtimeProject,
            ProjectSettings = projectSettings,
            AssetBundles = assetBundleSettings,
            EnabledPackages = sourceManifest.Packages
                .Where(package => package.Enabled && packageIds.Contains(package.Id))
                .Select(package => new RuntimePackageReferenceData
                {
                    Id = package.Id,
                    Version = package.Version
                }).ToList()
        };
        RuntimeMetadataSerializer.Save(runtimeMetadata,
            Path.Combine(runtimeRoot, RuntimeMetadataSerializer.FileName));
    }

    private static IReadOnlySet<GraphicsBackend> ResolveGraphicsBackends(PlayerBuildContext context)
    {
        var configured = PlatformPlayerBuildCapabilities.TryGetRenderer(
                context.Target.TargetId, out var renderer)
            ? renderer.Backends
            : context.Target.GraphicsBackends;
        var backends = configured
            .Where(static backend => backend != GraphicsBackend.Auto)
            .ToHashSet();
        if (backends.Count == 0)
            throw new InvalidDataException(
                $"Build target '{context.Target.TargetId}' has no concrete graphics backend.");
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
        PlayerContentPartition partition)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var contentFormat = partition == PlayerContentPartition.BuiltInAot
            ? $"built-in-resources-v{BuiltInResourceArchive.CurrentVersion}"
            : $"catalog-schema-{AssetBundleCatalog.CurrentSchemaVersion}";
        hash.AppendData(Encoding.UTF8.GetBytes(
            $"content-format\0{contentFormat}\0" +
            $"partition\0{partition}\0" +
            $"compression\0{(partition == PlayerContentPartition.HotUpdate && request.CompressAssetBundles ? AssetBundleCompressionMode.Optimal : AssetBundleCompressionMode.None)}\0" +
            $"hotupdate\0{request.EnableHotUpdate}\0symbols\0{request.IncludeDebugSymbols}\0"));
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
                    using var symbols = File.OpenRead(symbolsPath);
                    hash.AppendData(SHA256.HashData(symbols));
                }
            }
        foreach (var resource in releaseInputs.PackageResources)
            hash.AppendData(Encoding.UTF8.GetBytes(
                $"{resource.Address}\0{resource.Sha256}\0{resource.Size}\0"));
        var value = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return $"content-{value[..24]}";
    }

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
