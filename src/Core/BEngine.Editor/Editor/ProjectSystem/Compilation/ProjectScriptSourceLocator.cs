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
            .Where(EditorHostServices.FileSystem.DirectoryExists)
            .SelectMany(directory => EditorHostServices.FileSystem.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return files.FirstOrDefault(path =>
                   EditorHostServices.Paths.GetFileNameWithoutExtension(path).Equals(typeName, StringComparison.OrdinalIgnoreCase)) ??
               files.FirstOrDefault(path => DeclaresType(path, typeName));
    }

    private static IEnumerable<string> SearchRoots(ProjectWorkspace workspace)
    {
        yield return workspace.AssetsPath;
        yield return workspace.PackageCachePath;

        foreach (var sourceRoot in EngineSourceRoots(workspace.RootPath, EditorHostServices.Platform.BaseDirectory))
            yield return EditorHostServices.Paths.Combine(sourceRoot, "Core", "BEngine");
    }

    private static IEnumerable<string> EngineSourceRoots(params string[] startingPaths)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var startingPath in startingPaths)
        {
            for (var directory = EditorHostServices.Paths.GetFullPath(startingPath);
                 directory is not null;
                 directory = EditorHostServices.Paths.GetDirectoryName(directory))
            {
                if (!visited.Add(directory)) continue;
                foreach (var sourceRoot in new[]
                         {
                             EditorHostServices.Paths.Combine(directory, "Engine", "src"),
                             EditorHostServices.Paths.Combine(directory, "src")
                         })
                    if (EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(
                            sourceRoot, "Core", "BEngine", "BEngine.csproj")))
                        yield return sourceRoot;
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
            return Regex.IsMatch(EditorHostServices.FileSystem.ReadAllText(path),
                $@"\bclass\s+{Regex.Escape(typeName)}\b", RegexOptions.CultureInvariant);
        }
        catch (IOException)
        {
            return false;
        }
    }

}
