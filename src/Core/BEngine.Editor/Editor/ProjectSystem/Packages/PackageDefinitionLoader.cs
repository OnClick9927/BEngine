using BEngine.Serialization;
using BEngine.Editor;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

public static partial class PackageDefinitionLoader
{
    public static PackageDefinitionDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = EditorHostServices.Paths.GetFullPath(path);
        return Normalize(YamlUtility.Load<PackageDefinitionDocument>(fullPath), fullPath);
    }

}
