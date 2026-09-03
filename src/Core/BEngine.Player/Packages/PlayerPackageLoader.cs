using System.Reflection;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.HotUpdate;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Player;

internal static class PlayerPackageLoader
{
    public static PlayerHotUpdateSession? Load(
        ProjectWorkspace workspace,
        IAssetBundleManager? assetBundles = null,
        PlayerManagedCodeStage stage = PlayerManagedCodeStage.Legacy,
        PlayerBuiltInResourceProvider? builtInResources = null)
    {
        var phasePrefix = stage == PlayerManagedCodeStage.Aot ? "03_AOT_ASSEMBLY" : "05_HOTUPDATE";
        PlayerStartupDiagnostics.Phase($"{phasePrefix}_PREPARE_STARTED");
        var packages = workspace.RuntimeMetadata is { } metadata
            ? metadata.EnabledPackages.Select(package => new PlayerPackageReference
            {
                Id = package.Id,
                Enabled = true
            }).ToArray()
            : LoadLegacyManifest(workspace).Packages.ToArray();
        var enabled = packages
            .Where(package => package.Enabled)
            .Select(package => package.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
            RuntimePackageState.SetEnabled(package.Id, package.Enabled);

        var definitions = DiscoverDefinitions(workspace)
            .Where(item => enabled.Contains(item.Document.Id) && item.Document.Runtime is not null)
            .ToDictionary(item => item.Document.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions.Values)
            Resources.RegisterResourceRoot(Path.GetDirectoryName(Path.GetFullPath(definition.Path))!);

        var release = PlayerHotUpdateReleaseLoader.LoadAsync(
                workspace, assetBundles, stage, builtInResources)
            .ConfigureAwait(false).GetAwaiter().GetResult();
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
        var target = BuildTargetManifestSerializer.LoadCurrent();
        var runtimeFactory = new PlayerManagedCodeRuntimeFactory(DiscoverRuntimeProviders());
        var gate = new HotUpdateActivationGate(runtimeFactory, CreateHostCapabilities(target));
        var prepared = gate.PrepareAsync(
                release,
                new ManagedCodeRuntimeRequest(target.ManagedCodeRuntime),
                CancellationToken.None)
            .AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        PlayerStartupDiagnostics.Phase($"{phasePrefix}_PREPARED",
            $"release={release.ReleaseId};runtime={prepared.RuntimeKind};assemblies={prepared.Assemblies.Count}");
        return new PlayerHotUpdateSession(prepared, stage);
    }

    private static PlayerPackageManifest LoadLegacyManifest(ProjectWorkspace workspace) =>
        File.Exists(workspace.PackageManifestPath)
            ? YamlUtility.Load<PlayerPackageManifest>(workspace.PackageManifestPath)
            : new PlayerPackageManifest();

    private static IEnumerable<IManagedCodeRuntimeProvider> DiscoverRuntimeProviders()
    {
        foreach (var type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(GetLoadableTypes)
                     .Where(type => type is { IsClass: true, IsAbstract: false } &&
                                    (type.IsPublic || type.IsNestedPublic) &&
                                    typeof(IManagedCodeRuntimeProvider).IsAssignableFrom(type) &&
                                    type != typeof(CoreClrManagedCodeRuntimeProvider) &&
                                    type != typeof(AotInterpreterManagedCodeRuntimeProvider))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (Activator.CreateInstance(type) is not IManagedCodeRuntimeProvider provider)
                throw new InvalidOperationException(
                    $"Managed-code runtime provider '{type.FullName}' requires a public parameterless constructor.");
            yield return provider;
        }
    }

    private static IEnumerable<string> CreateHostCapabilities(BuildTargetManifest target)
    {
        yield return $"platform:{target.Platform}";
        yield return $"architecture:{target.Architecture}";
        yield return $"runtime:{target.ManagedCodeRuntime}";
        foreach (var backend in target.GraphicsBackends) yield return $"graphics:{backend}";
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static IReadOnlyList<(string Path, PlayerPackageDefinition Document)> DiscoverDefinitions(
        ProjectWorkspace workspace)
    {
        var projectDefinitions = DiscoverDefinitionsUnder(workspace.PackagesPath)
            .Select(static path => (Path: path, Document: YamlUtility.Load<PlayerPackageDefinition>(path)))
            .ToArray();
        var installed = Path.Combine(AppContext.BaseDirectory, "Packages");
        var projectIds = projectDefinitions
            .Select(static item => item.Document.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedDefinitions = DiscoverDefinitionsUnder(installed)
            .Select(static path => (Path: path, Document: YamlUtility.Load<PlayerPackageDefinition>(path)))
            .Where(item => !projectIds.Contains(item.Document.Id));
        return [.. installedDefinitions, .. projectDefinitions];
    }

    private static string[] DiscoverDefinitionsUnder(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "package.yaml", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        : [];

}
