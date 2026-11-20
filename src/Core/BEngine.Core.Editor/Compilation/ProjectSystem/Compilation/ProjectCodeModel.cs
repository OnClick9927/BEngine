using System.Collections.ObjectModel;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

/// <summary>A read-only view of the same source groups, references and symbols used by editor compilation.</summary>
public sealed record ProjectCodeAssembly(
    string Name, string RootNamespace, bool EditorOnly, bool AllowUnsafeCode,
    string TargetFramework, IReadOnlyList<string> SourcePaths,
    IReadOnlyList<string> ProjectReferences, IReadOnlyDictionary<string, string> AssemblyReferences,
    IReadOnlyList<string> DefineSymbols)
{
    public IReadOnlyList<PackageExternalReferenceDocument> PackageReferences { get; init; } = [];
}

/// <summary>Captures code authoring data without compiling, loading or changing package state.</summary>
public static class ProjectCodeModel
{
    public static IReadOnlyList<ProjectCodeAssembly> Capture(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var packages = BPackageCatalog.DiscoverCached(workspace);
        var manifest = EditorHostServices.FileSystem.FileExists(workspace.PackageManifestPath)
            ? YamlUtility.Load<PackageManifestDocument>(workspace.PackageManifestPath)
            : new PackageManifestDocument();
        var enabled = manifest.Packages.Where(package => package.Enabled && package.Type != "project")
            .Select(package => packages.GetRequired(package.Id)).ToArray();
        var runtime = ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: false);
        var editor = ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: true);
        var runtimeReferences = RuntimePackageLoader.ResolveEnabledReferences(workspace, enabled).AssemblyReferences;
        var editorReferences = new Dictionary<string, string>(runtimeReferences, StringComparer.OrdinalIgnoreCase)
        { ["BEngine.Core.Editor"] = typeof(BEngine.Editor.EditorServices).Assembly.Location };
        CoreModuleReferences.AddEditor(editorReferences);
        ProjectPackageSources.AddHostReferences(workspace, editorReferences, includeEditor: true);
        foreach (var package in enabled)
            ProjectScriptCompiler.AddPackageAssemblyReference(workspace, package.Document.Id, "editor",
                package.Document.Editor, editorReferences, preferredAssemblyPaths: null);
        var graph = new ProjectAssemblyDatabase(workspace).BuildGraph(
            runtime.Platform, runtime.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            editor.Platform, editor.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            editorReferences.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), includeEditorAssemblies: true,
            externalEditorAssemblies: editorReferences.Keys.Except(runtimeReferences.Keys, StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
        var names = graph.OrderedAssemblies.Select(node => node.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Array.AsReadOnly(graph.OrderedAssemblies.Select(node =>
        {
            var configuration = node.EditorOnly ? editor : runtime;
            IReadOnlyDictionary<string, string> references = node.EditorOnly ? editorReferences : runtimeReferences;
            return new ProjectCodeAssembly(node.Name, node.RootNamespace, node.EditorOnly, node.AllowUnsafeCode,
                configuration.TargetFramework, Array.AsReadOnly(node.SourcePaths.ToArray()),
                Array.AsReadOnly(node.References.Where(names.Contains).ToArray()),
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(references, StringComparer.OrdinalIgnoreCase)),
                Array.AsReadOnly(configuration.DefineSymbols.ToArray()))
            {
                PackageReferences = Array.AsReadOnly((ProjectPackageSources.FindAssembly(node.DefinitionPath, node.Name)?.PackageReferences ?? []).ToArray())
            };
        }).ToArray());
    }
}
