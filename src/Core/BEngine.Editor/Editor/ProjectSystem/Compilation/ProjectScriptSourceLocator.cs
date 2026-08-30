using System.Text.RegularExpressions;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public static class ProjectScriptSourceLocator
{
    public static string? Find(ProjectWorkspace workspace, string typeIdentity)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);

        var typeName = SimpleTypeName(typeIdentity);
        var files = SearchRoots(workspace)
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return files.FirstOrDefault(path =>
                   Path.GetFileNameWithoutExtension(path).Equals(typeName, StringComparison.OrdinalIgnoreCase)) ??
               files.FirstOrDefault(path => DeclaresType(path, typeName));
    }

    private static IEnumerable<string> SearchRoots(ProjectWorkspace workspace)
    {
        yield return workspace.AssetsPath;
        yield return workspace.PackagesPath;
        yield return Path.Combine(workspace.LibraryPath, "Packages");

        foreach (var repositoryRoot in RepositoryRoots(workspace.RootPath, AppContext.BaseDirectory))
            yield return Path.Combine(repositoryRoot, "src", "Core", "BEngine");
    }

    private static IEnumerable<string> RepositoryRoots(params string[] startingPaths)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var startingPath in startingPaths)
        {
            for (var directory = new DirectoryInfo(Path.GetFullPath(startingPath)); directory is not null;
                 directory = directory.Parent)
            {
                if (!visited.Add(directory.FullName)) continue;
                var projectFile = Path.Combine(directory.FullName, "src", "Core", "BEngine", "BEngine.csproj");
                if (File.Exists(projectFile)) yield return directory.FullName;
            }
        }
    }

    public static string SimpleTypeName(string typeIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        var typeName = typeIdentity.Split(',', 2)[0].Trim();
        var separator = Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('+'));
        var simpleName = separator >= 0 ? typeName[(separator + 1)..] : typeName;
        var genericMarker = simpleName.IndexOf('`');
        return genericMarker >= 0 ? simpleName[..genericMarker] : simpleName;
    }

    private static bool DeclaresType(string path, string typeName)
    {
        try
        {
            return Regex.IsMatch(File.ReadAllText(path),
                $@"\bclass\s+{Regex.Escape(typeName)}\b", RegexOptions.CultureInvariant);
        }
        catch (IOException)
        {
            return false;
        }
    }

}
