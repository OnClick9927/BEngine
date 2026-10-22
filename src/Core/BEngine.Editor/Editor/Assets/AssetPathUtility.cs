using BEngine.Platform;

namespace BEngine.Editor;

internal static class AssetPathUtility
{
    internal static (string Name, string Extension) SplitNameAndExtension(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var paths = EditorServiceAccess.GetRequiredService<PathUtility>();
        var fileName = paths.GetFileName(path);
        var extension = paths.GetExtension(fileName);
        if (!extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
            return (paths.GetFileNameWithoutExtension(fileName), extension);

        var withoutYaml = paths.GetFileNameWithoutExtension(fileName);
        var documentExtension = paths.GetExtension(withoutYaml);
        return string.IsNullOrWhiteSpace(documentExtension)
            ? (withoutYaml, extension)
            : (paths.GetFileNameWithoutExtension(withoutYaml), documentExtension + extension);
    }

    internal static string EditableName(string path, bool isDirectory) =>
        isDirectory
            ? EditorServiceAccess.GetRequiredService<PathUtility>().GetFileName(path.TrimEnd('/', '\\'))
            : SplitNameAndExtension(path).Name;
}
