using BEngine.Serialization;
using BEngine.Serialization.Documents;

namespace BEngine.ProjectSystem;

public sealed record ProjectAssemblyDefinition(string Path, AssemblyDefinitionDocument Document);

public sealed class ProjectAssemblyDatabase
{
    private readonly ProjectWorkspace _workspace;

    public ProjectAssemblyDatabase(ProjectWorkspace workspace) =>
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

    public IReadOnlyList<ProjectAssemblyDefinition> Discover()
    {
        return Directory.EnumerateFiles(_workspace.AssetsPath, "*.asmdef.yaml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProjectAssemblyDefinition(path, Load(path)))
            .ToArray();
    }

    public ProjectAssemblyDefinition? FindForSource(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        return Discover()
            .Where(definition => fullPath.StartsWith(
                Path.GetDirectoryName(definition.Path) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(definition => definition.Path.Length)
            .FirstOrDefault();
    }

    private static AssemblyDefinitionDocument Load(string path)
    {
        var document = YamlUtility.Load<AssemblyDefinitionDocument>(path);
        if (document.Format != "BEngine.AssemblyDefinition" || document.Version != 1 ||
            string.IsNullOrWhiteSpace(document.Name))
        {
            throw new InvalidDataException($"Invalid assembly definition: {path}");
        }
        return document;
    }
}
