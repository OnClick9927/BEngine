using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

[Flags]
public enum ImportAssetOptions
{
    Default = 0,
    ForceUpdate = 1,
    ForceSynchronousImport = 8,
    ImportRecursive = 256,
    DontDownloadFromCacheServer = 8192,
    ForceUncompressedImport = 16384
}

public class DefaultAsset : BObject
{
    public string assetPath { get; internal set; } = string.Empty;
    public string guid { get; internal set; } = string.Empty;
    public string assetType { get; internal set; } = string.Empty;
}

public class TextAsset : DefaultAsset
{
    public string text { get; internal set; } = string.Empty;
    public byte[] bytes => System.Text.Encoding.UTF8.GetBytes(text);
}

public sealed class MonoScript : TextAsset
{
    internal Type? scriptClass { get; set; }
    public Type? GetClass() => scriptClass;

    public static MonoScript? FromMonoBehaviour(MonoBehaviour behaviour) => FromType(behaviour?.GetType());
    public static MonoScript? FromScriptableObject(ScriptableObject scriptableObject) =>
        FromType(scriptableObject?.GetType());

    private static MonoScript? FromType(Type? type)
    {
        if (type is null) return null;
        return AssetDatabase.FindAssets($"{type.Name} t:Script")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<MonoScript>)
            .FirstOrDefault(script => script?.GetClass() == type);
    }
}

public static class AssetDatabase
{
    public static string[] FindAssets(string filter) => FindAssets(filter, null);

    public static string[] FindAssets(string filter, string[]? searchInFolders)
    {
        var host = EditorBridge.Host;
        if (host is null) return [];
        var (search, type) = ParseFilter(filter);
        return host.FindAssets(search)
            .Where(record => type is null || record.AssetType.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Where(record => searchInFolders is null || searchInFolders.Length == 0 ||
                             searchInFolders.Any(folder => IsInsideFolder(record.AssetPath, folder)))
            .Select(record => record.Guid.ToString("N"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string GUIDToAssetPath(string guid) =>
        Guid.TryParse(guid, out var id) && EditorBridge.Host?.GetAsset(id) is { } record
            ? record.AssetPath
            : string.Empty;

    public static string AssetPathToGUID(string path) =>
        EditorBridge.Host?.GetAsset(path)?.Guid.ToString("N") ?? string.Empty;

    public static string[] GetAllAssetPaths() => EditorBridge.Host?.FindAssets(string.Empty)
        .Select(record => record.AssetPath)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];

    public static bool Contains(BObject asset) => !string.IsNullOrWhiteSpace(GetAssetPath(asset));

    public static string GetAssetPath(BObject? asset) => asset is DefaultAsset reference
        ? reference.assetPath
        : string.Empty;

    public static BObject? LoadMainAssetAtPath(string assetPath)
    {
        var record = EditorBridge.Host?.GetAsset(assetPath);
        if (record is null) return null;
        var isText = record.Value.AssetType is "Script" or "YamlAsset" or "AssemblyDefinition" or "Shader";
        DefaultAsset asset = record.Value.AssetType == "Script" && File.Exists(record.Value.SourcePath)
            ? new MonoScript
            {
                text = File.ReadAllText(record.Value.SourcePath),
                scriptClass = FindScriptClass(Path.GetFileNameWithoutExtension(record.Value.SourcePath))
            }
            : isText && File.Exists(record.Value.SourcePath)
                ? new TextAsset { text = File.ReadAllText(record.Value.SourcePath) }
                : new DefaultAsset();
        asset.name = Path.GetFileName(record.Value.SourcePath);
        asset.assetPath = record.Value.AssetPath;
        asset.guid = record.Value.Guid.ToString("N");
        asset.assetType = record.Value.AssetType;
        return asset;
    }

    public static T? LoadAssetAtPath<T>(string assetPath) where T : BObject =>
        LoadMainAssetAtPath(assetPath) as T;

    public static BObject? LoadAssetAtPath(string assetPath, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var asset = LoadMainAssetAtPath(assetPath);
        return asset is not null && type.IsInstanceOfType(asset) ? asset : null;
    }

    public static Type? GetMainAssetTypeAtPath(string assetPath) => LoadMainAssetAtPath(assetPath)?.GetType();

    public static void CreateAsset(BObject asset, string path)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var fullPath = ResolveAssetPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new IOException($"Asset already exists: {path}");
        }
        YamlUtility.Save(asset, fullPath);
        EditorBridge.Host?.ImportAsset(path);
    }

    public static void ImportAsset(string path, ImportAssetOptions options = ImportAssetOptions.Default) =>
        EditorBridge.Host?.ImportAsset(path);

    public static void Refresh(ImportAssetOptions options = ImportAssetOptions.Default) =>
        EditorBridge.Host?.RefreshAssets();

    public static void SaveAssets() { }
    public static void StartAssetEditing() { }
    public static void StopAssetEditing() => Refresh();

    public static bool IsValidFolder(string path)
    {
        try
        {
            return Directory.Exists(ResolveAssetPath(path));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public static string CreateFolder(string parentFolder, string newFolderName) =>
        EditorBridge.Host?.CreateAssetFolder(parentFolder, newFolderName) ?? string.Empty;

    public static bool DeleteAsset(string path) => EditorBridge.Host?.DeleteAsset(path) ?? false;
    public static string MoveAsset(string oldPath, string newPath) =>
        EditorBridge.Host?.MoveAsset(oldPath, newPath) ?? "No editor project is open.";

    public static string RenameAsset(string pathName, string newName)
    {
        var extension = Path.GetExtension(pathName);
        var destination = Path.Combine(Path.GetDirectoryName(pathName) ?? "Assets",
            newName + extension).Replace('\\', '/');
        return MoveAsset(pathName, destination);
    }

    public static string GenerateUniqueAssetPath(string path)
    {
        if (EditorBridge.Host?.GetAsset(path) is null && !File.Exists(ResolveAssetPath(path))) return path;
        var directory = Path.GetDirectoryName(path) ?? "Assets";
        var extension = Path.GetExtension(path);
        var name = Path.GetFileNameWithoutExtension(path);
        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} {index}{extension}").Replace('\\', '/');
            if (EditorBridge.Host?.GetAsset(candidate) is null && !File.Exists(ResolveAssetPath(candidate)))
            {
                return candidate;
            }
        }
    }

    public static string[] GetSubFolders(string path) => Directory.Exists(ResolveAssetPath(path))
        ? Directory.EnumerateDirectories(ResolveAssetPath(path))
            .Select(ToProjectPath)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        : [];

    public static bool OpenAsset(BObject target, int lineNumber = -1, int columnNumber = -1)
    {
        var path = GetAssetPath(target);
        if (string.IsNullOrWhiteSpace(path)) return false;
        Process.Start(new ProcessStartInfo(ResolveAssetPath(path)) { UseShellExecute = true });
        return true;
    }

    private static (string Search, string? Type) ParseFilter(string? filter)
    {
        var terms = (filter ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var type = terms.FirstOrDefault(term => term.StartsWith("t:", StringComparison.OrdinalIgnoreCase));
        var search = string.Join(' ', terms.Where(term => !term.StartsWith("t:", StringComparison.OrdinalIgnoreCase)));
        var requestedType = type is { Length: > 2 } ? type[2..] : null;
        requestedType = requestedType?.ToLowerInvariant() switch
        {
            "monoscript" => "Script",
            "sceneasset" => "Scene",
            "texture2d" => "Texture",
            "assemblydefinitionasset" => "AssemblyDefinition",
            _ => requestedType
        };
        return (search, requestedType);
    }

    private static bool IsInsideFolder(string assetPath, string folder)
    {
        var normalized = folder.Replace('\\', '/').TrimEnd('/');
        return assetPath.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
               assetPath.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveAssetPath(string path)
    {
        var host = EditorBridge.Host ?? throw new InvalidOperationException("No editor project is open.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
              normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(Path.Combine(host.ProjectRootPath, normalized))
                : Path.GetFullPath(Path.Combine(host.AssetsRootPath, normalized));
        var assetsRoot = Path.GetFullPath(host.AssetsRootPath);
        if (!fullPath.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Asset path must stay inside {assetsRoot}.");
        }
        return fullPath;
    }

    private static string ToProjectPath(string fullPath)
    {
        var host = EditorBridge.Host ?? throw new InvalidOperationException("No editor project is open.");
        return Path.GetRelativePath(host.ProjectRootPath, fullPath).Replace('\\', '/');
    }

    private static Type? FindScriptClass(string typeName) => TypeCache.GetAllTypes()
        .FirstOrDefault(type => type.Name.Equals(typeName, StringComparison.Ordinal) &&
                                (typeof(MonoBehaviour).IsAssignableFrom(type) ||
                                 typeof(ScriptableObject).IsAssignableFrom(type)));
}
