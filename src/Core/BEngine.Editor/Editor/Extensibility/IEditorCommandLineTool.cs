using BEngine.Editor.Documents;

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
        var workspace = new ProjectWorkspace(EditorHostServices.Paths.Combine(
            EditorDataPaths.tempPath, package.Document.Id), new ProjectData { Name = "Package Tools" });
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
}
