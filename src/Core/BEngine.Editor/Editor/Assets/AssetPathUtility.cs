namespace BEngine.Editor;

internal static class AssetPathUtility
{
    internal static (string Name, string Extension) SplitNameAndExtension(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
            return (Path.GetFileNameWithoutExtension(fileName), extension);

        var withoutYaml = Path.GetFileNameWithoutExtension(fileName);
        var documentExtension = Path.GetExtension(withoutYaml);
        return string.IsNullOrWhiteSpace(documentExtension)
            ? (withoutYaml, extension)
            : (Path.GetFileNameWithoutExtension(withoutYaml), documentExtension + extension);
    }

    internal static string EditableName(string path, bool isDirectory) =>
        isDirectory ? Path.GetFileName(path.TrimEnd('/', '\\')) : SplitNameAndExtension(path).Name;
}
