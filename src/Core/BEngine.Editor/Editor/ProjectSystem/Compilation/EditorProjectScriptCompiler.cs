using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ProjectSystem.Editor;

public static class EditorProjectScriptCompiler
{
    internal static ScriptBuildConfiguration BuildConfiguration { get; } =
        ProjectScriptCompiler.CreateBuildConfigurationDefaults(editor: true);

    public static Assembly? CompileAndLoad(
        ProjectWorkspace workspace,
        Assembly? gameScripts,
        string coreEditorAssemblyPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(coreEditorAssemblyPath);
        var preferredAssemblies = ProjectScriptCompiler.PreferredAssemblyPaths(coreEditorAssemblyPath);
        using var packages = new BPackageManager(workspace, null, loadAssemblies: false);
        var references = PackageReferences(workspace, packages, preferredAssemblies);
        var runtimeConfiguration = ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: false);
        var editorConfiguration = ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: true);
        var externalAssemblies = references.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = new ProjectAssemblyDatabase(workspace).BuildGraph(
            runtimeConfiguration.Platform,
            runtimeConfiguration.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            editorConfiguration.Platform,
            editorConfiguration.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            externalAssemblies,
            includeEditorAssemblies: true);

        var runtimePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var runtimeAssembly in graph.RuntimeAssemblies)
        {
            var path = ResolveCurrentAssemblyPath(workspace, runtimeAssembly.Name);
            if (path is null)
                throw new FileNotFoundException(
                    $"Runtime project assembly '{runtimeAssembly.Name}' must be compiled before editor assemblies.");
            runtimePaths[runtimeAssembly.Name] = path;
        }
        if (gameScripts is not null && ReadableLocation(gameScripts) is { } gameScriptsPath)
            runtimePaths.TryAdd(gameScripts.GetName().Name ?? "GameScripts", gameScriptsPath);

        return ProjectScriptCompiler.CompileAndLoadGraph(
            workspace,
            graph.EditorAssemblies,
            references,
            runtimePaths,
            editorConfiguration,
            publishRuntimeManifest: false).EntryAssembly;
    }

    internal static Dictionary<string, string> PackageReferences(
        ProjectWorkspace workspace,
        BPackageManager packages,
        IReadOnlyDictionary<string, string>? preferredAssemblyPaths = null)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEngine"] = typeof(BObject).Assembly.Location,
            ["Microsoft.Extensions.DependencyInjection.Abstractions"] =
                typeof(IServiceCollection).Assembly.Location,
            ["BEngine.Editor"] = preferredAssemblyPaths is not null &&
                                 preferredAssemblyPaths.TryGetValue("BEngine.Editor", out var editorPath)
                ? editorPath
                : typeof(BEngine.Editor.EditorWindow).Assembly.Location
        };
        foreach (var package in packages.definitions)
        {
            if (!packages.IsEnabled(package.Document.Id)) continue;
            ProjectScriptCompiler.AddPackageAssemblyReference(
                workspace, package.Document.Id, "runtime", package.Document.Runtime, references,
                preferredAssemblyPaths);
            ProjectScriptCompiler.AddPackageAssemblyReference(
                workspace, package.Document.Id, "editor", package.Document.Editor, references,
                preferredAssemblyPaths);
        }
        return references;
    }

    private static string? ResolveCurrentAssemblyPath(ProjectWorkspace workspace, string assemblyName)
    {
        if (BEngine.Editor.EditorInstanceContext.current is { } instance &&
            ScriptAssemblyStore.ResolveCurrentPath(workspace, assemblyName, instance.scriptAssembliesPath) is
                { } instancePath)
            return instancePath;
        return ScriptAssemblyStore.ResolveCurrentPath(workspace, assemblyName);
    }

    private static string? ReadableLocation(Assembly assembly)
    {
        if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location)) return null;
        var path = Path.GetFullPath(assembly.Location);
        return File.Exists(path) ? path : null;
    }
}
