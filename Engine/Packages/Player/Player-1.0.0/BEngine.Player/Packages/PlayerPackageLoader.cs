using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.HotUpdate;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine.Player;

internal static class PlayerPackageLoader
{
    public static async BValueTask<PlayerHotUpdateSession?> LoadAsync(
        ProjectWorkspace workspace,
        IManagedCodeRuntimeFactory runtimeFactory,
        IAssetBundleManager? assetBundles = null,
        PlayerManagedCodeStage stage = PlayerManagedCodeStage.Legacy,
        PlayerBuiltInResourceProvider? builtInResources = null,
        BCancellationToken cancellationToken = default,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null,
        ApplicationPathUtility? applicationPaths = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        fileSystem ??= RuntimeServiceAccess.FileSystem;
        pathService ??= RuntimeServiceAccess.PathService;
        platformService ??= RuntimeServiceAccess.PlatformService;
        applicationPaths ??= RuntimeServiceAccess.ApplicationPaths;
        var phasePrefix = stage == PlayerManagedCodeStage.Aot ? "03_AOT_ASSEMBLY" : "05_HOTUPDATE";
        PlayerStartupDiagnostics.Phase($"{phasePrefix}_PREPARE_STARTED");
        using var definitionsTiming = PlayerStartupPerformanceDiagnostics.Begin("packages.definitions");
        var packages = workspace.GetRuntimeMetadata() is { } metadata
            ? metadata.EnabledPackages.Select(package => new PlayerPackageReference
            {
                Id = package.Id,
                Enabled = true
            }).ToArray()
            : LoadLegacyManifest(workspace, fileSystem).Packages.ToArray();
        var enabled = packages
            .Where(package => package.Enabled)
            .Select(package => package.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
            RuntimePackageState.SetEnabled(package.Id, package.Enabled);

        var definitions = DiscoverDefinitions(workspace, fileSystem, pathService, applicationPaths)
            .Where(item => enabled.Contains(item.Document.Id) && item.Document.Runtime is not null)
            .ToDictionary(item => item.Document.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions.Values)
            Resources.RegisterResourceRoot(pathService.GetDirectoryName(
                pathService.GetFullPath(definition.Path))!);
        definitionsTiming.Dispose();

        using var releaseTiming = PlayerStartupPerformanceDiagnostics.Begin("packages.releaseLoadSynchronousPrefix");
        var releaseOperation = PlayerHotUpdateReleaseLoader.LoadAsync(
            workspace, assetBundles, stage, builtInResources, cancellationToken, fileSystem);
        releaseTiming.Dispose();
        var release = await releaseOperation;
        if (stage == PlayerManagedCodeStage.Aot)
            PlayerStartupEnvironment.Current?.Report(.65f, "managedCode");
        if (release is null)
        {
            PlayerStartupDiagnostics.Phase($"{phasePrefix}_PREPARE_SKIPPED", "reason=no-release");
            return null;
        }
        var moduleNames = release.Modules.Select(module => module.Name).ToArray();
        var containsAot = moduleNames.Contains("AOT", StringComparer.OrdinalIgnoreCase);
        if (stage == PlayerManagedCodeStage.Aot && !containsAot)
            throw new InvalidDataException(
                "The built-in managed-code release must contain the project's AOT assembly.");
        if (stage == PlayerManagedCodeStage.HotUpdate && containsAot)
            throw new InvalidDataException(
                "The remote HotUpdate managed-code release must not contain the AOT assembly.");
        var target = BuildTargetManifestSerializer.LoadCurrent(
            baseDirectory: null, fileSystem, pathService, platformService);
        var gate = new HotUpdateActivationGate(runtimeFactory, CreateHostCapabilities(target));
        using var prepareTiming = PlayerStartupPerformanceDiagnostics.Begin("packages.runtimePrepareSynchronousPrefix");
        var prepareOperation = gate.PrepareAsync(
            release,
            new ManagedCodeRuntimeRequest(target.ManagedCodeRuntime),
            cancellationToken);
        prepareTiming.Dispose();
        var prepared = await prepareOperation;
        if (stage == PlayerManagedCodeStage.Aot)
            PlayerStartupEnvironment.Current?.Report(.75f, "managedCode");
        PlayerStartupDiagnostics.Phase($"{phasePrefix}_PREPARED",
            $"release={release.ReleaseId};runtime={prepared.RuntimeKind};" +
            $"assemblies={string.Join(',', prepared.Assemblies.Select(AssemblyName))};" +
            $"owned={string.Join(',', prepared.OwnedAssemblies.Select(AssemblyName))};" +
            $"modules={string.Join(',', prepared.ModuleIds)}");
        return new PlayerHotUpdateSession(prepared, stage);
    }

    private static string AssemblyName(System.Reflection.Assembly assembly) =>
        assembly.GetName().Name ?? "<unnamed>";

    private static PlayerPackageManifest LoadLegacyManifest(
        ProjectWorkspace workspace,
        FileUtility fileSystem) =>
        fileSystem.FileExists(workspace.PackageManifestPath)
            ? YamlUtility.Deserialize<PlayerPackageManifest>(
                fileSystem.ReadAllText(workspace.PackageManifestPath))
            : new PlayerPackageManifest();

    private static IEnumerable<string> CreateHostCapabilities(BuildTargetManifest target)
    {
        yield return $"platform:{target.Platform}";
        yield return $"architecture:{target.Architecture}";
        yield return $"runtime:{target.ManagedCodeRuntime}";
        foreach (var backend in target.GraphicsBackends) yield return $"graphics:{backend}";
    }

    private static IReadOnlyList<(string Path, PlayerPackageDefinition Document)> DiscoverDefinitions(
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PathUtility pathService,
        ApplicationPathUtility applicationPaths)
    {
        var projectDefinitionRoots = new[] { workspace.PackageCachePath, workspace.PackagesPath }
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var projectDefinitions = projectDefinitionRoots
            .SelectMany(root => DiscoverDefinitionsUnder(root, fileSystem, pathService))
            .Select(path => (Path: path, Document: YamlUtility.Deserialize<PlayerPackageDefinition>(
                fileSystem.ReadAllText(path))))
            .GroupBy(item => item.Document.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var installed = pathService.Combine(applicationPaths.BaseDirectory, "Packages");
        var projectIds = projectDefinitions
            .Select(static item => item.Document.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedDefinitions = DiscoverDefinitionsUnder(installed, fileSystem, pathService)
            .Select(path => (Path: path, Document: YamlUtility.Deserialize<PlayerPackageDefinition>(
                fileSystem.ReadAllText(path))))
            .Where(item => !projectIds.Contains(item.Document.Id));
        return [.. installedDefinitions, .. projectDefinitions];
    }

    private static string[] DiscoverDefinitionsUnder(
        string root,
        FileUtility fileSystem,
        PathUtility pathService) => fileSystem.DirectoryExists(root)
        ? fileSystem.EnumerateFiles(root, "package.yaml", SearchOption.AllDirectories)
            .Select(pathService.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        : [];

}
