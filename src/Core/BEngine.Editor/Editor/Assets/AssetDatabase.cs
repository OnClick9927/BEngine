using System.Reflection;
using BEngine.Documents;
using BEngine.Editor.Documents;
using BEngine.Serialization;

namespace BEngine.Editor;

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

    public static string GetAssetPath(BObject? asset)
    {
        if (asset is null) return string.Empty;
        return asset switch
        {
            DefaultAsset reference => reference.assetPath,
            PrefabAsset prefab => prefab.assetPath,
            _ => EditorBridge.Host?.GetAsset(asset.Id)?.AssetPath ?? string.Empty
        };
    }

    public static BObject? LoadMainAssetAtPath(string assetPath)
    {
        var record = EditorBridge.Host?.GetAsset(assetPath);
        if (record is null) return null;
        if (record.Value.AssetType == "Prefab" && File.Exists(record.Value.SourcePath))
        {
            var prefab = Document.LoadBObject<PrefabDocument, PrefabAsset>(record.Value.SourcePath);
            prefab.Id = record.Value.Guid;
            prefab.Document.Id = record.Value.Guid;
            prefab.assetPath = record.Value.AssetPath;
            return prefab;
        }
        if (record.Value.AssetType == "AssemblyDefinition" && File.Exists(record.Value.SourcePath))
        {
            AssemblyDefinitionAsset definitionAsset;
            try
            {
                definitionAsset = Document.LoadBObject<AssemblyDefinitionDocument, AssemblyDefinitionAsset>(
                    record.Value.SourcePath);
            }
            catch (Exception exception)
            {
                definitionAsset = new AssemblyDefinitionAsset { importError = exception.Message };
            }
            definitionAsset.name = Path.GetFileName(record.Value.SourcePath);
            definitionAsset.assetPath = record.Value.AssetPath;
            definitionAsset.sourcePath = record.Value.SourcePath;
            definitionAsset.guid = record.Value.Guid.ToString("N");
            definitionAsset.assetType = record.Value.AssetType;
            return definitionAsset;
        }
        if (record.Value.SourcePath.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(record.Value.SourcePath))
        {
            try
            {
                var document = Document.Load<ManagedAssetDocument>(record.Value.SourcePath);
                var type = ResolveManagedAssetType(document.TypeName);
                if (type is not null && YamlUtility.Deserialize(document.Data, type) is BObject managed)
                {
                    managed.Id = record.Value.Guid;
                    if (string.IsNullOrWhiteSpace(managed.name))
                        managed.name = AssetPathUtility.SplitNameAndExtension(record.Value.AssetPath).Name;
                    return managed;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load managed asset {record.Value.AssetPath}: {exception.Message}");
            }
        }
        var isText = record.Value.AssetType is "Script" or "YamlAsset" or "Shader" or "JSON" or "XML" or
            "Markdown" or "Text" or "UI Document" or "UI Style Sheet" or "HTML Document";
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
        asset.sourcePath = record.Value.SourcePath;
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
    public static BObject[] LoadAllAssetsAtPath(string assetPath) => LoadMainAssetAtPath(assetPath) is { } asset
        ? [asset] : [];

    public static void CreateAsset(BObject asset, string path)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var fullPath = ResolveAssetPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new IOException($"Asset already exists: {path}");
        }
        AssetModificationProcessorDispatcher.OnWillCreateAsset(path);
        if (path.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase))
        {
            new ManagedAssetDocument
            {
                TypeName = asset.GetType().AssemblyQualifiedName ?? asset.GetType().FullName ?? asset.GetType().Name,
                Data = YamlUtility.Serialize(asset)
            }.Save(fullPath);
        }
        else YamlUtility.Save(asset, fullPath);
        EditorBridge.Host?.ImportAsset(path);
    }

    public static void ImportAsset(string path, ImportAssetOptions options = ImportAssetOptions.Default) =>
        EditorBridge.Host?.ImportAsset(path);

    public static void ExportPackage(string assetPathName, string fileName) =>
        ExportPackage([assetPathName], fileName);

    public static void ExportPackage(string[] assetPathNames, string fileName)
    {
        ArgumentNullException.ThrowIfNull(assetPathNames);
        BPackageArchive.ExportPackage(assetPathNames, fileName);
    }

    public static BPackageManifest ExportPackage(
        string[] assetPathNames,
        string fileName,
        BPackageExportOptions options,
        Action<BPackageProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(assetPathNames);
        ArgumentNullException.ThrowIfNull(options);
        return BPackageArchive.ExportPackage(assetPathNames, fileName, options, progress);
    }

    public static void ImportPackage(string packagePath, bool interactive)
    {
        var host = EditorBridge.Host ?? throw new InvalidOperationException("No editor project is open.");
        BPackageArchive.ImportPackage(BEngine.ProjectSystem.ProjectWorkspace.Open(host.ProjectRootPath), packagePath,
            new BPackageImportOptions { ConflictPolicy = BPackageConflictPolicy.Fail });
    }

    public static BPackageImportResult ImportPackage(
        string packagePath,
        BPackageImportOptions options,
        Action<BPackageProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var host = EditorBridge.Host ?? throw new InvalidOperationException("No editor project is open.");
        return BPackageArchive.ImportPackage(BEngine.ProjectSystem.ProjectWorkspace.Open(host.ProjectRootPath),
            packagePath, options, progress);
    }

    public static BPackageManifest ReadPackageManifest(string packagePath) =>
        BPackageArchive.ReadManifest(packagePath);

    public static void Refresh(ImportAssetOptions options = ImportAssetOptions.Default) =>
        EditorBridge.Host?.RefreshAssets();

    public static void SaveAssets() =>
        AssetModificationProcessorDispatcher.OnWillSaveAssets(GetAllAssetPaths());
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

    public static bool DeleteAsset(string path)
    {
        var result = AssetModificationProcessorDispatcher.OnWillDeleteAsset(path, RemoveAssetOptions.None);
        if (result == AssetDeleteResult.FailedDelete) return false;
        if (result == AssetDeleteResult.DidDelete) return true;
        return EditorBridge.Host?.DeleteAsset(path) ?? false;
    }

    public static string MoveAsset(string oldPath, string newPath)
    {
        var result = AssetModificationProcessorDispatcher.OnWillMoveAsset(oldPath, newPath);
        if (result == AssetMoveResult.FailedMove) return "Asset move was rejected by a processor.";
        if (result == AssetMoveResult.DidMove) return string.Empty;
        return EditorBridge.Host?.MoveAsset(oldPath, newPath) ?? "No editor project is open.";
    }

    public static string RenameAsset(string pathName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            newName.Contains('/') || newName.Contains('\\'))
            return "The new asset name is invalid.";
        var extension = IsValidFolder(pathName) ? string.Empty : AssetPathUtility.SplitNameAndExtension(pathName).Extension;
        if (extension.Length > 0 && newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            newName = newName[..^extension.Length];
        var destination = Path.Combine(Path.GetDirectoryName(pathName) ?? "Assets",
            newName + extension).Replace('\\', '/');
        return MoveAsset(pathName, destination);
    }

    public static string GenerateUniqueAssetPath(string path)
    {
        if (EditorBridge.Host?.GetAsset(path) is null && !File.Exists(ResolveAssetPath(path)) &&
            !Directory.Exists(ResolveAssetPath(path))) return path;
        var directory = Path.GetDirectoryName(path) ?? "Assets";
        var (name, extension) = AssetPathUtility.SplitNameAndExtension(path);
        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} {index}{extension}").Replace('\\', '/');
            if (EditorBridge.Host?.GetAsset(candidate) is null && !File.Exists(ResolveAssetPath(candidate)) &&
                !Directory.Exists(ResolveAssetPath(candidate)))
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

    public static bool IsMainAsset(BObject asset) => Contains(asset);
    public static bool IsSubAsset(BObject asset) => false;
    public static bool IsForeignAsset(BObject asset) => false;
    public static bool IsNativeAsset(BObject asset) => Contains(asset);
    public static string[] GetDependencies(string pathName, bool recursive = true) =>
        string.IsNullOrWhiteSpace(pathName) ? [] : [pathName.Replace('\\', '/')];
    public static string[] GetDependencies(string[] pathNames, bool recursive = true) => pathNames
        .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Replace('\\', '/'))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static Hash128 GetAssetDependencyHash(string path) =>
        Hash128.Compute($"{path.Replace('\\', '/')}:{AssetPathToGUID(path)}");
    public static string GetCurrentCacheServerIp() => string.Empty;
    public static bool CanOpenAssetInEditor(int instanceId) =>
        EditorUtility.InstanceIDToObject(instanceId) is { } asset && Contains(asset);

    public static bool OpenAsset(BObject target, int lineNumber = -1, int columnNumber = -1)
    {
        var path = GetAssetPath(target);
        return !string.IsNullOrWhiteSpace(path) && OpenAsset(path, lineNumber, columnNumber);
    }

    public static bool OpenAsset(string filePath, int lineNumber = -1, int columnNumber = -1)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string fullPath;
        try
        {
            var normalized = filePath.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            fullPath = Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.GetFullPath(Path.Combine(EditorBridge.Host?.ProjectRootPath ?? Environment.CurrentDirectory,
                    normalized));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!File.Exists(fullPath)) return false;
        return InvokeOpenAssetCallbacks(fullPath, lineNumber, columnNumber) ||
               ExternalCodeEditor.Open(fullPath, lineNumber, columnNumber);
    }

    private static bool InvokeOpenAssetCallbacks(string filePath, int lineNumber, int columnNumber)
    {
        foreach (var method in TypeCache.GetMethodsWithAttribute<OnOpenAssetAttribute>()
                     .Where(method => method.IsStatic && method.ReturnType == typeof(bool))
                     .OrderBy(method => method.GetCustomAttribute<OnOpenAssetAttribute>()?.callbackOrder ?? 0))
        {
            var parameters = method.GetParameters();
            object?[]? arguments = parameters switch
            {
                [{ ParameterType: var file }, { ParameterType: var line }, { ParameterType: var column }]
                    when file == typeof(string) && line == typeof(int) && column == typeof(int) =>
                    [filePath, lineNumber, columnNumber],
                [{ ParameterType: var file }, { ParameterType: var line }]
                    when file == typeof(string) && line == typeof(int) => [filePath, lineNumber],
                [{ ParameterType: var file }] when file == typeof(string) => [filePath],
                _ => null
            };
            if (arguments is null) continue;
            try
            {
                if (method.Invoke(null, arguments) is true) return true;
            }
            catch (TargetInvocationException exception)
            {
                Debug.LogException(exception.InnerException ?? exception);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
        return false;
    }

    private static Type? ResolveManagedAssetType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return null;
        try
        {
            var resolved = Type.GetType(typeName, throwOnError: false);
            if (resolved is not null && typeof(BObject).IsAssignableFrom(resolved)) return resolved;
        }
        catch { }
        var fullName = typeName.Split(',')[0].Trim();
        return TypeCache.GetAllTypes().FirstOrDefault(type => typeof(BObject).IsAssignableFrom(type) &&
            string.Equals(type.FullName, fullName, StringComparison.Ordinal));
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
            "prefabasset" or "prefab" => "Prefab",
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

    internal static string ResolveAssetPath(string path)
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
