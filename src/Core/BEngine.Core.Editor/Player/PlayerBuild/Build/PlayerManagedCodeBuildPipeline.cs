using BEngine.Build;
using BEngine.Platform;

namespace BEngine.Editor.Build;

/// <summary>
/// Compiles release inputs, converges code/resource reachability, and preserves
/// shared code identity. Content providers own roots, dependencies and formats.
/// </summary>
public static class PlayerManagedCodeBuildPipeline
{
    private const string EntryAssembly = AotProjectLayout.AssemblyName;

    public static RuntimeManagedCodeReleaseInputSet CompileInputs(
        PlayerBuildContext context, ProjectWorkspace workspace, FileUtility files,
        PathUtility paths, CancellationToken cancellationToken)
    {
        var inputs = RuntimeManagedCodeReleaseInputCollector.Collect(workspace,
            EditorInstanceContext.current?.scriptAssembliesPath, workspace.ScriptAssembliesPath, files, paths);
        var available = BPackageCatalog.DiscoverAvailable().packages;
        var bootstrapDirectories = RuntimeBuildResources.GetDirectories().Select(directory =>
            (PackageId: available.First(package => package.Document.Runtime?.Assembly == directory.RuntimeAssembly).Document.Id,
             Path: "/Resources/" + directory.RelativePath.Trim('/') + "/")).ToArray();
        inputs = new(inputs.Assemblies, inputs.PackageResources.Where(resource =>
            !bootstrapDirectories.Any(directory => resource.PackageId == directory.PackageId &&
                resource.FilePath.Replace('\\', '/').Contains(directory.Path, StringComparison.OrdinalIgnoreCase))).ToArray(),
            inputs.PackageResourceRoots);
        inputs = PlayerProjectScriptCompiler.Compile(context, workspace, inputs, files, cancellationToken);
        var textPolicy = PlatformTextBuildPolicy.For(context.Target.Platform, context.Request.WebFontBackend);
        if (inputs.Assemblies.Any(input => !textPolicy.IncludesBinding(input.Name) ||
                input.Dependencies.Any(name => !textPolicy.IncludesBinding(name))))
            inputs = new(inputs.Assemblies.Where(input => textPolicy.IncludesBinding(input.Name))
                .Select(input => input with
                {
                    Dependencies = input.Dependencies.Where(name => textPolicy.IncludesBinding(name)).ToArray()
                }).ToArray(), inputs.PackageResources, inputs.PackageResourceRoots);
        if (!WebCanonicalScriptBuild.IsEnabled(context))
            textPolicy.ValidateRuntimeContracts(inputs.Assemblies.Where(static input =>
                !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name)).Select(static input => input.AssemblyPath));
        context.RuntimeManagedCodeAssemblyPaths = inputs.Assemblies.Select(static input => input.AssemblyPath).ToArray();
        return inputs;
    }

    public static RuntimeManagedCodeReleaseInputSet SelectStartupInputs(
        RuntimeManagedCodeReleaseInputSet source, PathUtility paths, IEnumerable<string>? assetFiles = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var byName = source.Assemblies.ToDictionary(static input => input.Name, StringComparer.OrdinalIgnoreCase);
        var entry = GetEntry(byName);
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Include(entry);
        if (assetFiles is not null)
        {
            var referenced = PlayerManagedCodeTrimmer.GetAssetReferencedAssemblies(
                source.Assemblies.Select(static input => input.AssemblyPath), assetFiles);
            foreach (var input in source.Assemblies)
                if (input.Origin.StartsWith("package:", StringComparison.OrdinalIgnoreCase) && referenced.Contains(input.Name))
                    Include(input);
        }
        var assemblies = source.Assemblies.Where(input => included.Contains(input.Name)).ToArray();
        var packageIds = assemblies.Where(static input => input.Origin.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            .Select(static input => input.Origin["package:".Length..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resources = source.PackageResources.Where(resource => packageIds.Contains(resource.PackageId)).ToArray();
        return new(assemblies, resources, source.PackageResourceRoots.Where(root => resources.Any(resource =>
            IsSameOrChild(resource.FilePath, root, paths))).ToArray());

        void Include(RuntimeManagedCodeReleaseInput input)
        {
            if (!included.Add(input.Name)) return;
            if (input.Origin.StartsWith("project:", StringComparison.OrdinalIgnoreCase) &&
                !input.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"AOT assembly '{EntryAssembly}' cannot depend on hot-update project assembly " +
                    $"'{input.Name}'. Move the dependency into AOT or an enabled runtime package.");
            foreach (var name in input.Dependencies)
            {
                if (!byName.TryGetValue(name, out var dependency))
                    throw new InvalidDataException($"Managed assembly '{input.Name}' depends on missing release input '{name}'.");
                Include(dependency);
            }
        }
    }

    public static RuntimeManagedCodeReleaseInputSet SelectDeferredInputs(RuntimeManagedCodeReleaseInputSet source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _ = GetEntry(source.Assemblies.ToDictionary(static input => input.Name, StringComparer.OrdinalIgnoreCase));
        foreach (var input in source.Assemblies)
            if (!input.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase) &&
                input.Dependencies.Contains(EntryAssembly, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Hot-update assembly '{input.Name}' cannot depend on '{EntryAssembly}', because the " +
                    "AOT domain is replaced before the hot-update game domain is activated.");
        return WithoutHostImages(new(source.Assemblies.Where(static input =>
            !input.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase)).ToArray(),
            source.PackageResources.ToArray(), source.PackageResourceRoots.ToArray()));
    }

    public static async Task<PlayerManagedCodeReachabilityResult> BuildStartupClosureAsync(
        PlayerBuildContext context, ProjectWorkspace workspace, RuntimeManagedCodeReleaseInputSet allInputs,
        IPlayerManagedCodeResourceGraph resources, FileUtility files, PathUtility paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var settings = PlayerStrippingSettings.Load(workspace);
        var report = new PlayerStrippingReport
        {
            Target = context.Target.TargetId, Mode = "Full",
            ContentEncryptionKeyFingerprint = context.ContentEncryptionKey?.Fingerprint
        };
        context.StrippingReport = report;
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EntryAssembly };
        // Enabling a package does not root its module initializers. Code and asset
        // reachability must admit it before a later pass activates its roots.
        var originals = new RuntimeManagedCodeReleaseInputSet(allInputs.Assemblies.Where(static input =>
            !input.Origin.StartsWith("project:", StringComparison.OrdinalIgnoreCase) ||
            input.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase)).ToArray(),
            allInputs.PackageResources, allInputs.PackageResourceRoots);
        cancellationToken.ThrowIfCancellationRequested();
        var assets = resources.CollectAssets([], [], settings, cancellationToken);
        var packageFiles = resources.FilterResources([], originals.PackageResources,
            AssetFiles(assets), settings, new()).Select(static resource => resource.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        RuntimeManagedCodeReleaseInputSet linked;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            linked = await PlayerManagedCodeTrimmer.TrimAsync(context, workspace, originals,
                AssetFiles(assets).Concat(packageFiles), settings, report, modules, cancellationToken).ConfigureAwait(false);
            var previousModuleCount = modules.Count;
            modules.UnionWith(linked.Assemblies.Select(static input => input.Name));
            var packageReport = new PlayerStrippingReport();
            var selectedResources = resources.FilterResources(linked.Assemblies, originals.PackageResources,
                AssetFiles(assets).Concat(packageFiles).ToArray(), settings, packageReport);
            var filtered = new RuntimeManagedCodeReleaseInputSet(linked.Assemblies, selectedResources, originals.PackageResourceRoots);
            var previousPackageCount = packageFiles.Count;
            packageFiles.UnionWith(filtered.PackageResources.Select(static resource => resource.FilePath));
            var discovered = resources.CollectAssets(linked.Assemblies.Select(static input => input.AssemblyPath).ToArray(),
                packageFiles, settings, cancellationToken);
            var admitted = assets.ToHashSet(StringComparer.OrdinalIgnoreCase);
            admitted.UnionWith(discovered);
            if (admitted.Count == assets.Count && packageFiles.Count == previousPackageCount && modules.Count == previousModuleCount)
            {
                linked = filtered;
                report.Resources.AddRange(packageReport.Resources);
                break;
            }
            assets = admitted.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        }
        context.RuntimeManagedCodeAssemblyPaths = allInputs.Assemblies.Where(static input =>
                !input.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase))
            .Concat(linked.Assemblies.Where(static input => !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name)))
            .Select(static input => input.AssemblyPath).ToArray();
        var directory = paths.Combine(workspace.RootPath, "Temp", "BuildReports");
        files.CreateDirectory(directory);
        context.StrippingReportPath = paths.Combine(directory, $"stripping-{context.Target.TargetId}-{Guid.NewGuid():N}.yaml");
        files.WriteAllText(context.StrippingReportPath, YamlUtility.Serialize(report));
        files.WriteAllText(Path.ChangeExtension(context.StrippingReportPath, ".aot-roots.xml"),
            files.ReadAllText(context.ReflectionRootDescriptorPath!));
        return new(linked, assets);

        string[] AssetFiles(IEnumerable<string> selected) => selected.Select(path => workspace.ResolveInside(path)).ToArray();
    }

    public static void CaptureHostAssetRoots(PlayerBuildContext context, ProjectWorkspace workspace,
        RuntimeManagedCodeReleaseInputSet inputs, IEnumerable<string> projectAssets, PathUtility paths)
    {
        context.StrippingWorkDirectory ??= paths.Combine(workspace.RootPath, "Temp", "PlayerStripping", Guid.NewGuid().ToString("N"));
        context.HostAssetRootDescriptorPath = PlayerHostAssetRoots.Write(
            paths.Combine(context.StrippingWorkDirectory, "host-asset-roots.xml"),
            inputs.Assemblies.Select(static input => input.AssemblyPath),
            projectAssets.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => workspace.ResolveInside(path))
                .Concat(inputs.PackageResources.Select(static resource => resource.FilePath)), PlayerStrippingSettings.Load(workspace));
    }

    public static RuntimeManagedCodeReleaseInputSet RemoveHostDuplicates(
        PlayerBuildContext context, RuntimeManagedCodeReleaseInputSet inputs)
    {
        var selected = WithoutHostImages(inputs);
        if (context.StrippingReport is { } report)
            foreach (var entry in report.Assemblies)
                if (entry.Included && DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(entry.Name))
                {
                    entry.Included = false;
                    entry.PublishedBytes = 0;
                    entry.Reason = "Provided by the static Host; duplicate AOT payload removed";
                }
        return selected;
    }

    public static RuntimeManagedCodeReleaseInputSet PreserveNativeImages(PlayerBuildContext context,
        RuntimeManagedCodeReleaseInputSet inputs, RuntimeManagedCodeReleaseInputSet originals)
    {
        if (context.Target.ManagedCodeRuntime != ManagedCodeRuntimeKind.AotInterpreter || WebCanonicalScriptBuild.IsEnabled(context))
            return inputs;
        inputs = NativeRuntimeImages.RestoreCanonicalImages(inputs, originals);
        context.RuntimeManagedCodeAssemblyPaths = originals.Assemblies.Concat(inputs.Assemblies)
            .GroupBy(static input => input.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last().AssemblyPath).ToArray();
        return inputs;
    }

    public static async Task<PlayerManagedCodeImageResult> FinalizeImagesAsync(
        PlayerBuildContext context, ProjectWorkspace workspace, RuntimeManagedCodeReleaseInputSet inputs,
        RuntimeManagedCodeReleaseInputSet originals, bool startup, IEnumerable<string> allProjectAssets,
        IEnumerable<string> selectedAssets, FileUtility files, PathUtility paths, CancellationToken cancellationToken)
    {
        PlayerBuildValidation.ValidateAssets(context, selectedAssets.Select(path => workspace.ResolveInside(path))
            .Concat(inputs.PackageResources.Select(static resource => resource.FilePath)), files, paths, cancellationToken);
        var canonical = WebCanonicalScriptBuild.IsEnabled(context);
        if (canonical)
        {
            originals = await WebCanonicalScriptBuild.CreateAsync(context, workspace, originals,
                allProjectAssets.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
                    .Select(path => workspace.ResolveInside(path)), cancellationToken).ConfigureAwait(false);
            if (!startup)
            {
                var deferred = SelectDeferredInputs(originals);
                inputs = new(deferred.Assemblies, inputs.PackageResources, inputs.PackageResourceRoots);
            }
            context.RuntimeManagedCodeAssemblyPaths = originals.Assemblies.Select(static input => input.AssemblyPath).ToArray();
        }
        if (startup && context.Target.Platform == BuildTargetPlatform.Web &&
            context.Request.WebScriptCompilation == WebScriptCompilationMode.AheadOfTime)
        {
            inputs = WebCanonicalScriptInputs.Select(inputs, originals, canonical);
            context.RuntimeManagedCodeAssemblyPaths = originals.Assemblies.Where(static input =>
                    !input.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase)).Concat(inputs.Assemblies)
                .GroupBy(static input => input.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.Last().AssemblyPath).ToArray();
            if (context.StrippingReport is { } report)
            {
                foreach (var input in inputs.Assemblies.Where(static input => !input.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase)))
                {
                    var entry = report.Assemblies.FirstOrDefault(entry => entry.Name.Equals(input.Name, StringComparison.OrdinalIgnoreCase));
                    if (entry is null)
                    {
                        entry = new() { Name = input.Name, OriginalBytes = input.AssemblySize };
                        report.Assemblies.Add(entry);
                    }
                    entry.Included = true;
                    entry.PublishedBytes = input.AssemblySize;
                    entry.StoredBytes = null;
                    entry.Reason = canonical ? "Canonical linked image shared with precompiled Web scripts" : "Canonical original image shared with precompiled Web scripts";
                }
                if (!string.IsNullOrWhiteSpace(context.StrippingReportPath))
                    files.WriteAllText(context.StrippingReportPath, YamlUtility.Serialize(report));
            }
            var startupNames = inputs.Assemblies.Select(static input => input.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var deferredImages = originals.Assemblies.Where(static input =>
                    !input.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase) &&
                    !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name))
                .OrderBy(static input => input.Name, StringComparer.Ordinal).ToArray();
            context.WebPrecompiledManagedCodeReferencePaths = deferredImages.Select(static input => input.AssemblyPath).ToArray();
            context.WebPrecompiledSharedManagedCodeAssemblyPaths = deferredImages.Where(input => startupNames.Contains(input.Name))
                .Select(static input => input.AssemblyPath).ToArray();
            context.WebPrecompiledManagedCodeAssemblyPaths = deferredImages.Where(input => !startupNames.Contains(input.Name) ||
                    input.Origin.StartsWith("package:", StringComparison.OrdinalIgnoreCase) && NativeRuntimeImages.RequiresNative(input.AssemblyPath))
                .Select(static input => input.AssemblyPath).ToArray();
        }
        if (!startup && !string.IsNullOrWhiteSpace(context.Request.HostContractPath))
            PlayerHostContract.Validate(context.Request.HostContractPath, context, inputs.Assemblies.Select(static input => input.AssemblyPath));
        return new(inputs, originals);
    }

    private static RuntimeManagedCodeReleaseInputSet WithoutHostImages(RuntimeManagedCodeReleaseInputSet inputs)
    {
        var assemblies = inputs.Assemblies.Where(static input => !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name)).ToArray();
        var names = assemblies.Select(static input => input.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(assemblies.Select(input => input with { Dependencies = input.Dependencies.Where(names.Contains).ToArray() }).ToArray(),
            inputs.PackageResources, inputs.PackageResourceRoots);
    }

    private static RuntimeManagedCodeReleaseInput GetEntry(IReadOnlyDictionary<string, RuntimeManagedCodeReleaseInput> inputs)
    {
        if (!inputs.TryGetValue(EntryAssembly, out var entry) || !entry.Origin.Equals($"project:{EntryAssembly}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The project must compile '{EntryAssembly}' from " +
                $"'{AotProjectLayout.AssetRoot}/{EntryAssembly}.asmdef.yaml' before building content.");
        return entry;
    }

    private static bool IsSameOrChild(string file, string root, PathUtility paths)
    {
        var candidate = paths.TrimEndingDirectorySeparator(paths.GetFullPath(file));
        var parent = paths.TrimEndingDirectorySeparator(paths.GetFullPath(root));
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(parent + paths.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
