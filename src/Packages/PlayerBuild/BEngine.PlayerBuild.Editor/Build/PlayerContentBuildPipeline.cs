using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.Editor.Documents;
using BEngine.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering.Rhi;
using BEngine.UIElements;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;
using ProjectAssetMetaDocument = BEngine.ProjectSystem.Editor.AssetMetaDocument;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;
using AotProjectLayout = BEngine.ProjectSystem.Editor.AotProjectLayout;

namespace BEngine.Editor;

internal static class PlayerContentBuildPipeline
{
    internal const string AotAssemblyName = "AOT";
    internal const string AotAssetRoot = AotProjectLayout.AssetRoot;
    internal const string AotScenePath = AotProjectLayout.SceneAssetPath;
    private static readonly HashSet<string> WebNativeSharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEngine.UIElements",
        "BEngine.Audio"
    };

    internal static Task<string> BuildAsync(
        PlayerBuildContext context,
        CancellationToken cancellationToken) =>
        BuildAsync(
            context,
            EditorServiceAccess.GetRequiredService<IAssetBundleBuildPipeline>(),
            EditorServiceAccess.GetRequiredService<IProjectAssetDatabaseFactory>(),
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>(),
            EditorServiceAccess.GetRequiredService<PlatformPlayerBuildCapabilities>(),
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
            EditorServiceAccess.GetRequiredService<IAssetBundleBuildPipeline>(),
            EditorServiceAccess.GetRequiredService<IProjectAssetDatabaseFactory>(),
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>(),
            EditorServiceAccess.GetRequiredService<PlatformPlayerBuildCapabilities>(),
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
        var builtInRoots = scenes.Concat(projectPartition.ResourcesRoots)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var assetPaths = partition == PlayerContentPartition.BuiltInAot
            ? projectPartition.BuiltInAssets
            : projectPartition.HotUpdateAssets;
        var includedPathSet = assetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedAssets = database.assets.Where(asset =>
                !asset.IsDirectory && includedPathSet.Contains(NormalizeAssetPath(asset.AssetPath)))
            .ToArray();
        var allReleaseInputs = RuntimeManagedCodeReleaseInputCollector.Collect(
            workspace,
            EditorInstanceContext.current?.scriptAssembliesPath,
            fileSystem,
            pathService);
        allReleaseInputs = PlayerProjectScriptCompiler.Compile(context, workspace, allReleaseInputs, fileSystem, cancellationToken);
        allReleaseInputs = WebFontBackendSettings.FilterRuntimeInputs(allReleaseInputs,
            context.Target.Platform == BuildTargetPlatform.Web, context.Request.WebFontBackend,
            deferReferenceValidation: WebCanonicalScriptBuild.IsEnabled(context));
        context.RuntimeManagedCodeAssemblyPaths = allReleaseInputs.Assemblies
            .Select(static assembly => assembly.AssemblyPath)
            .ToArray();
        var releaseInputs = partition == PlayerContentPartition.BuiltInAot
            ? SelectBuiltInReleaseInputs(allReleaseInputs, pathService,
                context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full
                    ? null : assetPaths.Select(path => workspace.ResolveInside(path)))
            : SelectHotUpdateReleaseInputs(allReleaseInputs);
        if (partition == PlayerContentPartition.BuiltInAot &&
            context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full)
        {
            var stripping = PlayerStrippingSettings.Load(workspace);
            var report = new PlayerStrippingReport
            {
                Target = context.Target.TargetId, Mode = "Full",
                ContentEncryptionKeyFingerprint = context.ContentEncryptionKey?.Fingerprint
            };
            context.StrippingReport = report;
            var moduleAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AotAssemblyName };
            // Include candidates for asset-only/reflection-only package references. The
            // linker decides reachability, so enabling a package does not itself root it.
            releaseInputs = new RuntimeManagedCodeReleaseInputSet(allReleaseInputs.Assemblies
                .Where(static a => !a.Origin.StartsWith("project:", StringComparison.OrdinalIgnoreCase) ||
                                  a.Name.Equals(AotAssemblyName, StringComparison.OrdinalIgnoreCase)).ToArray(),
                allReleaseInputs.PackageResources, allReleaseInputs.PackageResourceRoots);
            var candidates = AssetBundleBuilder.CollectAllAssets(database, fileSystem, pathService,
                path => IsAotAsset(path) || resourceClosure.Contains(path), cancellationToken);
            var originalInputs = releaseInputs;
            var roots = PlayerResourceStripper.GetProjectRoots(builtInRoots, candidates, [], stripping, database.assets);
            assetPaths = AssetBundleBuilder.CollectDependencies(workspace, database, roots,
                fileSystem, pathService, includeImplicitRuntimeRoots: false, cancellationToken);
            PlayerProjectAssetPartition.ValidateAotBoundary(assetPaths, resourceClosure);
            var packageFiles = PlayerResourceStripper.FilterPackageResources(
                    new RuntimeManagedCodeReleaseInputSet([], originalInputs.PackageResources, originalInputs.PackageResourceRoots),
                    assetPaths.Select(path => workspace.ResolveInside(path)), stripping, new PlayerStrippingReport())
                .PackageResources.Select(static resource => resource.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Start at scene/explicit roots, then grow the graph with literals from
            // reachable code. Starting with every literal lets dead code and dead
            // scene types accidentally root each other.
            while (true)
            {
                releaseInputs = await PlayerManagedCodeTrimmer.TrimAsync(context, workspace, originalInputs,
                    assetPaths.Select(path => workspace.ResolveInside(path)).Concat(packageFiles), stripping, report, moduleAssemblies, cancellationToken)
                    .ConfigureAwait(false);
                // A reference from dead code is not a reason to run a package's
                // initializers. Activate module roots only after code/assets reach it.
                var previousModuleCount = moduleAssemblies.Count;
                moduleAssemblies.UnionWith(releaseInputs.Assemblies.Select(static assembly => assembly.Name));
                var packageReport = new PlayerStrippingReport();
                var filtered = PlayerResourceStripper.FilterPackageResources(
                    new RuntimeManagedCodeReleaseInputSet(releaseInputs.Assemblies, originalInputs.PackageResources, originalInputs.PackageResourceRoots),
                    assetPaths.Select(path => workspace.ResolveInside(path)).Concat(packageFiles), stripping, packageReport);
                var previousPackageCount = packageFiles.Count;
                packageFiles.UnionWith(filtered.PackageResources.Select(static resource => resource.FilePath));
                roots = PlayerResourceStripper.GetProjectRoots(builtInRoots, candidates,
                    releaseInputs.Assemblies.Select(static a => a.AssemblyPath), stripping, database.assets, packageFiles);
                var discovered = AssetBundleBuilder.CollectDependencies(workspace, database, roots,
                    fileSystem, pathService, includeImplicitRuntimeRoots: false, cancellationToken);
                PlayerProjectAssetPartition.ValidateAotBoundary(discovered, resourceClosure);
                var before = assetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
                before.UnionWith(discovered);
                if (before.Count == assetPaths.Count && packageFiles.Count == previousPackageCount &&
                    moduleAssemblies.Count == previousModuleCount)
                {
                    releaseInputs = filtered;
                    report.Resources.AddRange(packageReport.Resources);
                    break;
                }
                assetPaths = before.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
            }
            includedPathSet = assetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            includedAssets = database.assets.Where(asset => !asset.IsDirectory &&
                includedPathSet.Contains(NormalizeAssetPath(asset.AssetPath))).ToArray();
            PlayerResourceStripper.RecordProjectAssets(database.assets.Where(a => candidates.Contains(a.AssetPath, StringComparer.OrdinalIgnoreCase)),
                includedPathSet, report);
            context.RuntimeManagedCodeAssemblyPaths = allReleaseInputs.Assemblies
                .Where(static a => !a.Name.Equals(AotAssemblyName, StringComparison.OrdinalIgnoreCase))
                // This pass only links the startup module's reachable contracts.
                // The static Host must keep its original image until the final
                // application link, which also sees the platform entry point.
                .Concat(releaseInputs.Assemblies.Where(static a =>
                    !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(a.Name)))
                .Select(static a => a.AssemblyPath).ToArray();
            var reportDirectory = pathService.Combine(workspace.RootPath, "Temp", "BuildReports");
            fileSystem.CreateDirectory(reportDirectory);
            context.StrippingReportPath = pathService.Combine(reportDirectory,
                $"stripping-{context.Target.TargetId}-{Guid.NewGuid():N}.yaml");
            fileSystem.WriteAllText(context.StrippingReportPath, YamlUtility.Serialize(report));
            fileSystem.WriteAllText(Path.ChangeExtension(context.StrippingReportPath, ".aot-roots.xml"),
                fileSystem.ReadAllText(context.ReflectionRootDescriptorPath!));
        }
        var graphicsBackends = ResolveGraphicsBackends(context, buildCapabilities);
        releaseInputs = FilterPackageResourcesForBackends(
            releaseInputs, graphicsBackends, pathService);
        if (partition == PlayerContentPartition.BuiltInAot && context.StrippingReport is { } strippingReport)
        {
            // The default load context already supplies these contracts. Shipping
            // another copy in the AOT archive wastes payload and never changes loading.
            var archiveAssemblies = releaseInputs.Assemblies
                .Where(static assembly => !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(assembly.Name)).ToArray();
            var archiveNames = archiveAssemblies.Select(static assembly => assembly.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            releaseInputs = new RuntimeManagedCodeReleaseInputSet(archiveAssemblies.Select(assembly => assembly with
                { Dependencies = assembly.Dependencies.Where(archiveNames.Contains).ToArray() }).ToArray(),
                releaseInputs.PackageResources, releaseInputs.PackageResourceRoots);
            foreach (var entry in strippingReport.Assemblies.Where(static entry =>
                         entry.Included && DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(entry.Name)))
            {
                entry.Included = false;
                entry.PublishedBytes = 0;
                entry.Reason = "Provided by the static Host; duplicate AOT payload removed";
            }
            var keptResources = releaseInputs.PackageResources.Select(static r => r.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in strippingReport.Resources.Where(static r => r.Name.StartsWith("Assets/Packages/", StringComparison.OrdinalIgnoreCase)))
                if (entry.Included && !keptResources.Contains(entry.Name))
                {
                    entry.Included = false;
                    entry.PublishedBytes = 0;
                    entry.Reason = "Not used by selected graphics backends";
                }
            fileSystem.WriteAllText(context.StrippingReportPath!, YamlUtility.Serialize(strippingReport));
        }
        releaseInputs = PlayerShaderBuildPipeline.AddArtifacts(context, releaseInputs, includedAssets, cancellationToken);
        // Validate the final admitted assets for every explicit FreeType Web build,
        // including interpreter/non-Full builds and standalone content updates.
        WebCanonicalScriptBuild.ValidateFontBackend(context,
            assetPaths.Select(path => workspace.ResolveInside(path))
                .Concat(releaseInputs.PackageResources.Select(static resource => resource.FilePath)),
            fileSystem, pathService, cancellationToken);
        var canonicalWebScripts = WebCanonicalScriptBuild.IsEnabled(context);
        if (canonicalWebScripts)
        {
            allReleaseInputs = await WebCanonicalScriptBuild.CreateAsync(context, workspace, allReleaseInputs,
                projectPartition.BuiltInAssets.Concat(projectPartition.HotUpdateAssets)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
                    .Select(path => workspace.ResolveInside(path)), cancellationToken).ConfigureAwait(false);
            if (partition == PlayerContentPartition.HotUpdate)
            {
                // AB and native code must consume the same final images. Keep the
                // partition's already filtered resources and generated shaders.
                var canonicalHotUpdate = SelectHotUpdateReleaseInputs(allReleaseInputs);
                releaseInputs = new RuntimeManagedCodeReleaseInputSet(canonicalHotUpdate.Assemblies,
                    releaseInputs.PackageResources, releaseInputs.PackageResourceRoots);
            }
            context.RuntimeManagedCodeAssemblyPaths = allReleaseInputs.Assemblies
                .Select(static input => input.AssemblyPath).ToArray();
        }
        if (partition == PlayerContentPartition.BuiltInAot && context.Target.Platform == BuildTargetPlatform.Web &&
            context.Request.WebScriptCompilation == WebScriptCompilationMode.AheadOfTime)
        {
            // Both runtime stages bind the same final package metadata. Full builds
            // use their joint linked images; other modes retain original images.
            releaseInputs = WebCanonicalScriptInputs.Select(releaseInputs, allReleaseInputs, canonicalWebScripts);
            context.RuntimeManagedCodeAssemblyPaths = allReleaseInputs.Assemblies
                .Where(static input => !input.Name.Equals(AotAssemblyName, StringComparison.OrdinalIgnoreCase))
                .Concat(releaseInputs.Assemblies)
                .GroupBy(static input => input.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.Last().AssemblyPath).ToArray();
            if (context.StrippingReport is { } canonicalReport)
            {
                foreach (var input in releaseInputs.Assemblies.Where(static input =>
                             !input.Name.Equals(AotAssemblyName, StringComparison.OrdinalIgnoreCase)))
                {
                    var entry = canonicalReport.Assemblies.FirstOrDefault(entry =>
                        entry.Name.Equals(input.Name, StringComparison.OrdinalIgnoreCase));
                    if (entry is null)
                    {
                        entry = new PlayerStrippingReportEntry { Name = input.Name, OriginalBytes = input.AssemblySize };
                        canonicalReport.Assemblies.Add(entry);
                    }
                    entry.Included = true;
                    entry.PublishedBytes = input.AssemblySize;
                    entry.StoredBytes = null;
                    entry.Reason = canonicalWebScripts
                        ? "Canonical linked image shared with precompiled Web scripts"
                        : "Canonical original image shared with precompiled Web scripts";
                }
                if (!string.IsNullOrWhiteSpace(context.StrippingReportPath))
                    fileSystem.WriteAllText(context.StrippingReportPath, YamlUtility.Serialize(canonicalReport));
            }
            var builtInNames = releaseInputs.Assemblies.Select(static input => input.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hotUpdateImages = allReleaseInputs.Assemblies
                .Where(input => !input.Name.Equals(AotAssemblyName, StringComparison.OrdinalIgnoreCase) &&
                    !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name))
                .OrderBy(static input => input.Name, StringComparer.Ordinal)
                .ToArray();
            context.WebPrecompiledManagedCodeReferencePaths = hotUpdateImages
                .Select(static input => input.AssemblyPath).ToArray();
            context.WebPrecompiledSharedManagedCodeAssemblyPaths = hotUpdateImages
                .Where(input => builtInNames.Contains(input.Name))
                .Select(static input => input.AssemblyPath).ToArray();
            context.WebPrecompiledManagedCodeAssemblyPaths = hotUpdateImages
                .Where(input => !builtInNames.Contains(input.Name) ||
                    input.Origin.StartsWith("package:", StringComparison.OrdinalIgnoreCase) &&
                    WebNativeSharedAssemblyNames.Contains(input.Name))
                .Select(static input => input.AssemblyPath).ToArray();
        }
        if (partition == PlayerContentPartition.HotUpdate && !string.IsNullOrWhiteSpace(context.Request.HostContractPath))
            PlayerHostContract.Validate(context.Request.HostContractPath, context, releaseInputs.Assemblies.Select(static a => a.AssemblyPath));
        var versionScenes = partition == PlayerContentPartition.BuiltInAot
            ? scenes
            : assetPaths.Where(static path =>
                    path.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        var version = partition == PlayerContentPartition.BuiltInAot
            ? ComputeContentVersion(
                includedAssets, releaseInputs, versionScenes, context.Request, partition, fileSystem)
            : PlayerBuildSettingsStore.NormalizeHotResourceVersion(context.Request.HotResourceVersion);

        var layout = PlayerBuildLayout.Create(workspace, context.StagingDirectory, fileSystem, pathService);
        layout.CreateDataDirectories();
        var sourceSettingsPath = pathService.Combine(
            workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        var settings = fileSystem.FileExists(sourceSettingsPath)
            ? LoadYaml<AssetBundleSettingsDocument>(sourceSettingsPath, fileSystem, pathService)
            : new AssetBundleSettingsDocument();
        settings.Enabled = true;
        settings.PackageName = NormalizePackageName(
            string.IsNullOrWhiteSpace(settings.PackageName) ? workspace.Project.Name : settings.PackageName);
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
                context.ContentEncryptionKey,
                context.Request.CompressAssetBundles);
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
                EncryptionKey = context.ContentEncryptionKey,
                PackageName = settings.PackageName,
                Version = version,
                TargetPlatform = context.Target.Platform,
                OutputDirectory = bundleOutput,
                CacheDirectory = pathService.Combine(workspace.LibraryPath, "AssetBundleBuildCache"),
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
                compress: PlayerResourceArchiveCompression.IsEnabled(targetPlatform, compress));
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
            byte[]? content = null;
            IReadOnlyDictionary<string, string> settings = metadata.Settings ?? [];
            if (PlatformAssetBuildPipeline.TryBuildArtifact(asset.SourcePath, asset.SourceHash, artifactPath,
                    asset.AssetType, metadata.Importer ?? string.Empty, settings,
                    targetPlatform, fileSystem, pathService, cancellationToken, out var encoded, out var effective))
            { content = encoded; settings = effective; }
            if (asset.AssetType == "Scene" && PlatformAssetBuildPipeline.TryCompileRuntimeArtifact(asset.SourcePath, asset.AssetType,
                    artifactPath, asset.ArtifactHash, content, settings, fileSystem, cancellationToken,
                    out var compiled, out var compiledSettings))
            { content = compiled; settings = compiledSettings; }
            if (content is not null)
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
                             Convert.ToHexString(releaseHash.GetHashAndReset()).ToLowerInvariant()[..24];
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
        return (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
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
        var actualHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (content.LongLength != expectedSize ||
            !actualHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Built-in resource '{address}' changed while the Player was being built.");
        // Keep authoring assets and their snapshot validation unchanged. Runtime
        // fonts use raw atlas/font bytes, avoiding JSON and Base64 during boot.
        if (pathService.GetExtension(address).Equals(".sdfont", StringComparison.OrdinalIgnoreCase))
        {
            content = SdfFontAsset.CompileRuntimeBytes(content);
            var settings = importerSettings is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(importerSettings, StringComparer.Ordinal);
            settings["builtFontFormat"] = "binary-v1";
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
        RuntimeManagedCodeReleaseInputSet source) =>
        SelectBuiltInReleaseInputs(
            source, EditorServiceAccess.GetRequiredService<PathUtility>());

    private static RuntimeManagedCodeReleaseInputSet SelectBuiltInReleaseInputs(
        RuntimeManagedCodeReleaseInputSet source,
        PathUtility pathService,
        IEnumerable<string>? assetFiles = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var byName = source.Assemblies.ToDictionary(
            static assembly => assembly.Name, StringComparer.OrdinalIgnoreCase);
        var aot = GetAotAssembly(byName);
        var includedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Include(aot);
        if (assetFiles is not null)
        {
            // Resource-only package types have no compile-time edge from AOT.
            // Hot project code remains in the game domain and is not activated here.
            var packageAssemblies = source.Assemblies.Where(static assembly =>
                assembly.Origin.StartsWith("package:", StringComparison.OrdinalIgnoreCase)).ToArray();
            var referenced = PlayerManagedCodeTrimmer.GetAssetReferencedAssemblies(
                source.Assemblies.Select(static assembly => assembly.AssemblyPath), assetFiles);
            foreach (var assembly in packageAssemblies.Where(assembly => referenced.Contains(assembly.Name)))
                Include(assembly);
        }

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
            .Where(root => resources.Any(resource =>
                IsSameOrChildPath(resource.FilePath, root, pathService)))
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
        IReadOnlySet<GraphicsBackend> graphicsBackends) =>
        FilterPackageResourcesForBackends(
            source,
            graphicsBackends,
            EditorServiceAccess.GetRequiredService<PathUtility>());

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

        var projectSettings = fileSystem.FileExists(workspace.ProjectSettingsFilePath)
            ? LoadYaml<ProjectSettingsData>(
                workspace.ProjectSettingsFilePath, fileSystem, pathService)
            : new ProjectSettingsData();

        var packageIds = releaseInputs.Assemblies
            .Where(static assembly => assembly.Origin.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            .Select(static assembly => assembly.Origin["package:".Length..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceManifest = fileSystem.FileExists(workspace.PackageManifestPath)
            ? LoadYaml<PackageManifestDocument>(
                workspace.PackageManifestPath, fileSystem, pathService)
            : new PackageManifestDocument();
        var runtimeMetadata = new RuntimeMetadataDocument
        {
            Project = runtimeProject,
            ProjectSettings = projectSettings,
            AssetBundles = assetBundleSettings,
            EnabledPackages = sourceManifest.Packages
                .Where(package => package.Enabled && package.Type != "project" && packageIds.Contains(package.Id))
                .Select(package => new RuntimePackageReferenceData
                {
                    Id = package.Id,
                    Version = package.Version
                }).ToList()
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
            report.GraphicsBackends = context.GraphicsBackends.Select(static b => b.ToString()).ToList();
            report.RuntimeShaderCompilerIncluded = context.GraphicsBackends.Contains(GraphicsBackend.Vulkan) && context.Request.RuntimeShaderCompilation;
        }
        if (!context.Request.RuntimeShaderCompilation && !context.Request.CompileShaders && context.GraphicsBackends.Contains(GraphicsBackend.Vulkan))
            throw new InvalidDataException("Vulkan without a runtime compiler requires Compile Shaders.");
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
            $"compression\0{(request.CompressAssetBundles ? AssetBundleCompressionMode.Optimal : AssetBundleCompressionMode.None)}\0" +
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
