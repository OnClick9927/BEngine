using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem.Editor;

// Project-owned packages compile as project assemblies, while retaining the external
// dependencies declared by the same package.yaml used when exporting them.
internal static class ProjectPackageSources
{
    internal static PackageAssemblyDocument? FindAssembly(string definitionPath, string name)
    {
        var paths = EditorHostServices.Paths;
        var directory = paths.GetDirectoryName(definitionPath);
        if (directory is null || paths.GetDirectoryName(directory) is not { } parent) return null;
        var manifest = paths.Combine(parent, "package.yaml");
        if (!EditorHostServices.FileSystem.FileExists(manifest)) return null;
        var document = PackageDefinitionLoader.Load(manifest);
        return new[] { document.Runtime, document.Editor }.FirstOrDefault(assembly => assembly?.Assembly == name);
    }

    internal static void AddHostReferences(ProjectWorkspace workspace, IDictionary<string, string> references, bool includeEditor)
    {
        var database = new ProjectAssemblyDatabase(workspace);
        var runtime = ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: false);
        var editor = includeEditor ? ProjectScriptCompiler.CreateBuildConfiguration(workspace, editor: true) : runtime;
        foreach (var definition in database.Discover())
        {
            if (!database.MatchesBuildConfiguration(definition, runtime, editor, includeEditor)) continue;
            if (FindAssembly(definition.Path, definition.Document.Name) is not { } assembly) continue;
            foreach (var name in assembly.HostReferences)
                if (!references.ContainsKey(name))
                    references[name] = RuntimePackageLoader.ResolveAssemblyPath(workspace, name)
                        ?? throw new FileNotFoundException($"Host reference '{name}' is unavailable for project package '{assembly.Assembly}'.");
        }
    }
}
