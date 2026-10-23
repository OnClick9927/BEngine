using BEngine.Editor.Documents;
using BEngine.Tooling;

namespace BEngine.Editor;

public interface IEditorCommandLineTool
{
    string Option { get; }
    int Run(IReadOnlyList<string> arguments);
}

internal static class EditorPackageCommandLine
{
    public static int Run(string[] arguments)
    {
        var catalog = BPackageCatalog.DiscoverAvailable();
        var owners = catalog.packages.Where(definition =>
            definition.Document.CommandLineOptions.Contains(arguments[0], StringComparer.OrdinalIgnoreCase)).ToArray();
        if (owners.Length != 1)
            throw new ArgumentException($"Expected one installed package providing '{arguments[0]}', found {owners.Length}.");
        var package = owners[0];
        var project = ResolveProject(arguments);
        var previousEnvironment = new[] { "TEMP", "TMP", "TMPDIR", ToolEnvironment.ProjectDirectoryVariable }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var temporaryRoot = project is null ? EditorDataPaths.tempPath :
            EditorHostServices.Paths.Combine(project, "Temp");
        var commandRoot = EditorHostServices.Paths.Combine(temporaryRoot, "PackageTools", Guid.NewGuid().ToString("N"));
        try
        {
            if (project is not null) ToolEnvironment.ConfigureProject(project);
            return RunTool(commandRoot, package, catalog, arguments);
        }
        finally
        {
            foreach (var (name, value) in previousEnvironment)
                Environment.SetEnvironmentVariable(name, value);
            try
            {
                if (EditorHostServices.FileSystem.DirectoryExists(commandRoot))
                    EditorHostServices.FileSystem.DeleteDirectory(commandRoot, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.WriteLine(exception); }
        }
    }

    private static int RunTool(string commandRoot, BPackageDefinition package, BPackageCatalog catalog, string[] arguments)
    {
        var workspace = new ProjectWorkspace(commandRoot, new ProjectData { Name = "Package Tools" });
        var manifest = new PackageManifestDocument();
        foreach (var id in catalog.GetDependencyClosure(package.Document.Id).Append(package.Document.Id))
            manifest.Packages.Add(new PackageReferenceDocument
            {
                Id = id, Version = catalog.GetRequired(id).Document.PackageVersion, Enabled = true
            });
        YamlUtility.Save(manifest, workspace.PackageManifestPath);
        using var manager = new BPackageManager(workspace, catalog);
        var tool = TypeCache.GetTypesDerivedFrom<IEditorCommandLineTool>()
            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters)
            .Select(type => (IEditorCommandLineTool)Activator.CreateInstance(type)!)
            .SingleOrDefault(tool => tool.Option.Equals(arguments[0], StringComparison.OrdinalIgnoreCase));
        return tool?.Run(arguments) ?? throw new InvalidOperationException(
            $"Package '{package.Document.Id}' did not register command '{arguments[0]}'.");
    }

    private static string? ResolveProject(string[] arguments)
    {
        var index = Array.FindIndex(arguments, argument => argument.Equals("--project", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Option '--project' requires a project path.");
        var path = EditorHostServices.Paths.GetFullPath(arguments[index + 1]);
        var root = EditorHostServices.FileSystem.FileExists(path)
            ? EditorHostServices.Paths.GetDirectoryName(path)! : path;
        if (!EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(root, ProjectWorkspace.ProjectFileName)))
            throw new FileNotFoundException("The BEngine project was not found.", path);
        return root;
    }
}
