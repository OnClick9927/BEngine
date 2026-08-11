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
        var files = new[] { workspace.ScriptsPath, workspace.EditorScriptsPath }
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return files.FirstOrDefault(path =>
                   Path.GetFileNameWithoutExtension(path).Equals(typeName, StringComparison.OrdinalIgnoreCase)) ??
               files.FirstOrDefault(path => DeclaresType(path, typeName));
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
