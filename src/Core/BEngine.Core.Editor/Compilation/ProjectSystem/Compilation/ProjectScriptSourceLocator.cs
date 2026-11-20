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
        var files = new List<string>();
        foreach (var path in SearchRoots(workspace)
            .Where(EditorHostServices.FileSystem.DirectoryExists)
            .SelectMany(directory => EditorHostServices.FileSystem.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Filename matches always won over content matches. Once found, no
            // remaining package or engine source tree needs to be enumerated.
            if (EditorHostServices.Paths.GetFileNameWithoutExtension(path).Equals(typeName, StringComparison.OrdinalIgnoreCase))
                return path;
            files.Add(path);
        }
        var pattern = $@"\bclass\s+{Regex.Escape(typeName)}\b";
        return Matches1308();
        [global::BEngine.SkipLoopGuard]
        string? Matches1308()
        {
            var matchSource1308 = files;
            ArgumentNullException.ThrowIfNull(matchSource1308, "source");
            if (matchSource1308.GetType() != typeof(global::System.Collections.Generic.List<string>))
            {
                foreach (var path in (global::System.Collections.Generic.IEnumerable<string>)matchSource1308)
                {
                    if (DeclaresType(path, pattern))
                        return path;
                }
        
                return default!;
            }
        
            global::System.ReadOnlySpan<string> matchValues1308 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource1308);
            foreach (var path in matchValues1308)
            {
                if (DeclaresType(path, pattern))
                    return path;
            }
        
            return default!;
        }
    }

    private static IEnumerable<string> SearchRoots(ProjectWorkspace workspace)
    {
        yield return workspace.AssetsPath;
        yield return workspace.PackageCachePath;

        foreach (var sourceRoot in EngineSourceRoots(workspace.RootPath, EditorHostServices.Platform.BaseDirectory))
        {
            yield return EditorHostServices.Paths.Combine(sourceRoot, "Core");
            yield return EditorHostServices.Paths.Combine(sourceRoot, "Packages");
        }
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
                            sourceRoot, "Core", "BEngine.Core", "BEngine.Core.csproj")))
                        yield return sourceRoot;
            }
        }
    }

    public static string SimpleTypeName(string typeIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        var typeName = typeIdentity.AsSpan().Before(',').Trim().ToString();
        var separator = Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('+'));
        var simpleName = separator >= 0 ? typeName[(separator + 1)..] : typeName;
        var genericMarker = simpleName.IndexOf('`');
        return genericMarker >= 0 ? simpleName[..genericMarker] : simpleName;
    }

    private static bool DeclaresType(string path, string pattern)
    {
        try
        {
            return Regex.IsMatch(EditorHostServices.FileSystem.ReadAllText(path), pattern, RegexOptions.CultureInvariant);
        }
        catch (IOException)
        {
            return false;
        }
    }

}
