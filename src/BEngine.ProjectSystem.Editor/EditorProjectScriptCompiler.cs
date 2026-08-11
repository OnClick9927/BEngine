using System.Reflection;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public static class EditorProjectScriptCompiler
{
    internal static ScriptBuildConfiguration BuildConfiguration { get; } =
        new("net9.0-windows", false);

    public static Assembly? CompileAndLoad(
        ProjectWorkspace workspace,
        Assembly? gameScripts,
        string coreEditorAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coreEditorAssemblyPath);
        var preferredAssemblies = ProjectScriptCompiler.PreferredAssemblyPaths(coreEditorAssemblyPath);
        var references = PackageReferences(new BPackageManager(workspace), preferredAssemblies);
        if (gameScripts is not null)
        {
            var gameScriptsName = gameScripts.GetName().Name ?? "GameScripts";
            if (string.IsNullOrWhiteSpace(gameScripts.Location) || !File.Exists(gameScripts.Location))
            {
                throw new FileNotFoundException(
                    $"Runtime script assembly '{gameScriptsName}' does not have a readable file location.");
            }
            references[gameScriptsName] = gameScripts.Location;
        }

        return ProjectScriptCompiler.CompileAndLoadAssembly(
            workspace,
            workspace.EditorScriptsPath,
            "GameEditorScripts",
            "EditorScriptCompilation.log",
            references,
            BuildConfiguration);
    }

    internal static Dictionary<string, string> PackageReferences(
        BPackageManager packages,
        IReadOnlyDictionary<string, string>? preferredAssemblyPaths = null)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages.definitions)
        {
            if (!packages.IsEnabled(package.Document.Id)) continue;
            ProjectScriptCompiler.AddPackageAssemblyReference(
                package.Document.Id, "runtime", package.Document.Runtime, references,
                preferredAssemblyPaths);
            ProjectScriptCompiler.AddPackageAssemblyReference(
                package.Document.Id, "editor", package.Document.Editor, references,
                preferredAssemblyPaths);
        }
        return references;
    }
}
