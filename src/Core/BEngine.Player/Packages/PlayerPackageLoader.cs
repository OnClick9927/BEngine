using System.Reflection;
using System.Runtime.Loader;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.Player;

internal static class PlayerPackageLoader
{
    public static void Load(ProjectWorkspace workspace)
    {
        var manifest = File.Exists(workspace.PackageManifestPath)
            ? YamlUtility.Load<PlayerPackageManifest>(workspace.PackageManifestPath)
            : new PlayerPackageManifest();
        var enabled = manifest.Packages
            .Where(package => package.Enabled)
            .Select(package => package.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
            RuntimePackageState.SetEnabled(package.Id, package.Enabled);

        var definitions = DiscoverDefinitions(workspace)
            .Where(item => enabled.Contains(item.Document.Id) && item.Document.Runtime is not null)
            .ToDictionary(item => item.Document.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions.Values)
            Resources.RegisterResourceRoot(Path.GetDirectoryName(Path.GetFullPath(definition.Path))!);
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in definitions.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            LoadRuntime(workspace, packageId, definitions, loaded,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        LoadProjectAssemblies(workspace);
    }

    private static void LoadRuntime(
        ProjectWorkspace workspace,
        string packageId,
        IReadOnlyDictionary<string, (string Path, PlayerPackageDefinition Document)> definitions,
        ISet<string> loaded,
        ISet<string> active)
    {
        if (loaded.Contains(packageId) || !definitions.TryGetValue(packageId, out var package)) return;
        if (!active.Add(packageId))
            throw new InvalidDataException($"Runtime package dependency cycle at '{packageId}'.");
        foreach (var dependency in package.Document.Runtime!.Dependencies
                     .Where(dependency => dependency.Target == "runtime"))
            LoadRuntime(workspace, dependency.PackageId, definitions, loaded, active);
        active.Remove(packageId);

        var assemblyName = package.Document.Runtime.Assembly;
        if (AppDomain.CurrentDomain.GetAssemblies().All(assembly =>
                !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)))
        {
            var path = ResolveAssemblyPath(workspace, assemblyName, package.Path) ?? throw new FileNotFoundException(
                $"Runtime assembly '{assemblyName}' for package '{packageId}' was not found.");
            PreloadCompanions(Path.GetDirectoryName(path)!, assemblyName);
            AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
        loaded.Add(packageId);
    }

    private static void LoadProjectAssemblies(ProjectWorkspace workspace)
    {
        var manifest = ScriptAssemblyStore.LoadProjectManifest(workspace);
        if (manifest is null)
        {
            var gameScripts = ScriptAssemblyStore.ResolveCurrentPath(workspace, "GameScripts");
            if (gameScripts is not null && !IsLoaded("GameScripts"))
                AssemblyLoadContext.Default.LoadFromAssemblyPath(gameScripts);
            return;
        }

        var manifestNames = manifest.Assemblies.Select(assembly => assembly.Assembly)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in manifest.Assemblies)
        {
            foreach (var reference in assembly.References.Where(manifestNames.Contains))
                if (!loaded.Contains(reference) && !IsLoaded(reference))
                    throw new InvalidDataException(
                        $"Project assembly manifest loads '{assembly.Assembly}' before dependency '{reference}'.");
            if (!IsLoaded(assembly.Assembly))
            {
                var path = ScriptAssemblyStore.ResolveProjectAssemblyPath(workspace, assembly);
                PreloadCompanions(Path.GetDirectoryName(path)!, assembly.Assembly);
                AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            loaded.Add(assembly.Assembly);
        }
    }

    private static bool IsLoaded(string assemblyName) => AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
        assembly.GetName().Name?.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) == true);

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

    private static string? ResolveAssemblyPath(
        ProjectWorkspace workspace,
        string assemblyName,
        string definitionPath)
    {
        if (ScriptAssemblyStore.ResolveCurrentPath(workspace, assemblyName) is { } compiledAssembly)
            return compiledAssembly;

        var packageRoot = Path.GetDirectoryName(definitionPath)!;
        var exportedAssembly = Path.Combine(packageRoot, $"{assemblyName}.dll");
        return File.Exists(exportedAssembly) ? Path.GetFullPath(exportedAssembly) : null;
    }

    private static void PreloadCompanions(string directory, string packageAssemblyName)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            AssemblyName assemblyName;
            try { assemblyName = AssemblyName.GetAssemblyName(path); }
            catch (BadImageFormatException) { continue; }
            var name = assemblyName.Name;
            if (string.IsNullOrWhiteSpace(name) ||
                name.Equals(packageAssemblyName, StringComparison.OrdinalIgnoreCase) ||
                AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)) continue;
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
    }
}
