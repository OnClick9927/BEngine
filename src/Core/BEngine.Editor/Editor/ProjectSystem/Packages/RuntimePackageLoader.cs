using System.Reflection;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;
using BEngine.Serialization;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

public static class RuntimePackageLoader
{
    public static RuntimePackageSet LoadEnabled(ProjectWorkspace workspace)
        => LoadEnabled(workspace, BPackageCatalog.DiscoverCached(workspace).packages, true);

    internal static RuntimePackageSet LoadEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> packageDefinitions,
        bool applyRuntimeState = true) => LoadEnabled(
        workspace,
        packageDefinitions,
        applyRuntimeState,
        new PackageSourceCompilerService(BEngine.Platform.ProcessUtility.Current));

    internal static RuntimePackageSet LoadEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> packageDefinitions,
        bool applyRuntimeState,
        IPackageSourceCompiler packageSourceCompiler)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(packageDefinitions);
        ArgumentNullException.ThrowIfNull(packageSourceCompiler);
        var definitions = packageDefinitions
            .GroupBy(item => item.Document.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var manifest = EditorHostServices.FileSystem.FileExists(workspace.PackageManifestPath)
            ? YamlUtility.Load<PackageManifestDocument>(workspace.PackageManifestPath)
            : new PackageManifestDocument();
        var manifestById = manifest.Packages.ToDictionary(
            package => package.Id, StringComparer.OrdinalIgnoreCase);
        var enabledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEngine"] = typeof(BObject).Assembly.Location,
            ["Microsoft.Extensions.DependencyInjection.Abstractions"] =
                typeof(IServiceCollection).Assembly.Location
        };

        if (applyRuntimeState)
            foreach (var package in manifest.Packages)
                RuntimePackageState.SetEnabled(package.Id, package.Enabled);

        var enabledDefinitions = manifest.Packages.Where(package => package.Enabled && package.Type != "project")
            .Select(package => definitions.TryGetValue(package.Id, out var definition)
                ? definition
                : throw new FileNotFoundException(
                    $"Enabled package '{package.Id}' is missing from " +
                    $"'{ProjectPackageCache.GetCacheRoot(workspace)}'."))
            .ToArray();
        var compilation = packageSourceCompiler.CompileEnabled(workspace, enabledDefinitions);

        foreach (var definition in enabledDefinitions)
        {
            enabledIds.Add(definition.Document.Id);
            if (definition.Document.Runtime is not { } runtime) continue;
            if (!compilation.TryGetAssemblyPath(runtime.Assembly, out var assemblyPath) ||
                !EditorHostServices.FileSystem.FileExists(assemblyPath))
                throw new FileNotFoundException(
                    $"Enabled package '{definition.Document.Id}' requires runtime assembly " +
                    $"'{runtime.Assembly}', but its source compilation produced no assembly.");
            references[runtime.Assembly] = assemblyPath;
        }

        return new RuntimePackageSet(enabledIds, references);
    }

    internal static RuntimePackageSet ResolveEnabledReferences(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> packageDefinitions)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(packageDefinitions);
        var definitions = packageDefinitions
            .GroupBy(item => item.Document.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var manifest = EditorHostServices.FileSystem.FileExists(workspace.PackageManifestPath)
            ? YamlUtility.Load<PackageManifestDocument>(workspace.PackageManifestPath)
            : new PackageManifestDocument();
        var enabledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEngine"] = typeof(BObject).Assembly.Location,
            ["Microsoft.Extensions.DependencyInjection.Abstractions"] =
                typeof(IServiceCollection).Assembly.Location
        };

        foreach (var package in manifest.Packages.Where(package => package.Enabled && package.Type != "project"))
        {
            if (!definitions.TryGetValue(package.Id, out var definition))
                throw new FileNotFoundException(
                    $"Enabled package '{package.Id}' is missing from " +
                    $"'{ProjectPackageCache.GetCacheRoot(workspace)}'.");
            enabledIds.Add(package.Id);
            if (definition.Document.Runtime is not { } runtime) continue;
            var assemblyPath = ResolveAssemblyPath(workspace, runtime.Assembly);
            if (assemblyPath is null)
                throw new FileNotFoundException(
                    $"Enabled package '{package.Id}' runtime assembly '{runtime.Assembly}' is not available. " +
                    "Package compilation must complete before project scripts are queued.");
            references[runtime.Assembly] = assemblyPath;
        }

        return new RuntimePackageSet(enabledIds, references);
    }

    internal static string? ResolveAssemblyPath(ProjectWorkspace workspace, string assemblyName)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var loaded = EditorHostServices.Platform.GetLoadedAssemblies().FirstOrDefault(candidate =>
            candidate.GetName().Name?.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) == true);
        if (ReadableLocation(loaded) is { } loadedPath) return loadedPath;

        if (BEngine.Editor.EditorInstanceContext.current is { } instance &&
            ScriptAssemblyStore.ResolveCurrentPath(workspace, assemblyName, instance.scriptAssembliesPath) is
                { } instancePath)
            return instancePath;
        if (ScriptAssemblyStore.ResolveCurrentPath(workspace, assemblyName) is { } projectPath)
            return projectPath;

        var applicationPath = EditorHostServices.Paths.Combine(EditorHostServices.Platform.BaseDirectory, $"{assemblyName}.dll");
        return EditorHostServices.FileSystem.FileExists(applicationPath) ? EditorHostServices.Paths.GetFullPath(applicationPath) : null;
    }

    private static string? ReadableLocation(Assembly? assembly)
    {
        if (assembly is null || assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location)) return null;
        var path = EditorHostServices.Paths.GetFullPath(assembly.Location);
        return EditorHostServices.FileSystem.FileExists(path) ? path : null;
    }

}
