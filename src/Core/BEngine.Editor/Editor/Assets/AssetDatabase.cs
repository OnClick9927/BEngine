using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Documents;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem.Editor;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class AssetDatabase
{
    private static readonly ConditionalWeakTable<BObject, SubAssetBinding> SubAssetBindings = new();
    private static readonly Dictionary<Guid, WeakReference<BObject>> EmbeddedSubAssetCache = [];
    private static readonly Lock SubAssetGate = new();

    public static string[] FindAssets(string filter) => FindAssets(filter, null);

    public static string[] FindAssets(string filter, string[]? searchInFolders)
    {
        var host = EditorBridge.Host;
        if (host is null) return [];
        var (search, type) = ParseFilter(filter);
        return host.FindAssets(search)
            .Where(record => !record.IsSubAsset)
            .Where(record => type is null || record.AssetType.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Where(record => searchInFolders is null || searchInFolders.Length == 0 ||
                             searchInFolders.Any(folder => IsInsideFolder(record.AssetPath, folder)))
            .Select(record => record.Guid.ToString("N"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string GUIDToAssetPath(string guid)
    {
        if (!Guid.TryParse(guid, out var id) || EditorBridge.Host?.GetAsset(id) is not { } record)
            return string.Empty;
        return record.ParentGuid is { } parent && EditorBridge.Host.GetAsset(parent) is { } owner
            ? owner.AssetPath
            : record.AssetPath;
    }

    public static string AssetPathToGUID(string path)
    {
        var record = EditorBridge.Host?.GetAsset(path);
        return record?.ParentGuid?.ToString("N") ?? record?.Guid.ToString("N") ?? string.Empty;
    }

    public static string[] GetAllAssetPaths() => EditorBridge.Host?.FindAssets(string.Empty)
        .Where(record => !record.IsSubAsset)
        .Select(record => record.AssetPath)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];

    public static bool Contains(BObject asset) => !string.IsNullOrWhiteSpace(GetAssetPath(asset));

    public static string GetAssetPath(BObject? asset)
    {
        if (asset is null) return string.Empty;
        if (SubAssetBindings.TryGetValue(asset, out var binding)) return binding.ParentAssetPath;
        return asset switch
        {
            BAsset reference when !string.IsNullOrWhiteSpace(reference.assetPath) => reference.assetPath,
            _ => EditorBridge.Host?.GetAsset(asset.Id)?.AssetPath ?? string.Empty
        };
    }

    public static BObject? LoadMainAssetAtPath(string assetPath)
    {
        var record = EditorBridge.Host?.GetAsset(assetPath);
        return LoadAssetRecord(record);
    }

    private static BObject? LoadAssetRecord(EditorAssetRecord? record)
    {
        if (record is null) return null;
        if (record.Value.AssetType == "Prefab" && File.Exists(record.Value.SourcePath))
        {
            var prefab = Document.LoadBObject<PrefabDocument, PrefabAsset>(record.Value.SourcePath);
            prefab.Id = record.Value.Guid;
            prefab.Document.Id = record.Value.Guid;
            prefab.assetPath = record.Value.AssetPath;
            prefab.BindAssetReference(record.Value.AssetPath, record.Value.Guid);
            return BindLoadedSubAsset(prefab, record.Value);
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
            definitionAsset.Id = record.Value.Guid;
            definitionAsset.assetPath = record.Value.AssetPath;
            definitionAsset.sourcePath = record.Value.SourcePath;
            definitionAsset.guid = record.Value.Guid.ToString("N");
            definitionAsset.assetType = record.Value.AssetType;
            return BindLoadedSubAsset(definitionAsset, record.Value);
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
                    if (managed is BAsset managedAsset)
                        managedAsset.BindAssetReference(record.Value.AssetPath, record.Value.Guid);
                    if (string.IsNullOrWhiteSpace(managed.name))
                        managed.name = AssetPathUtility.SplitNameAndExtension(record.Value.AssetPath).Name;
                    return BindLoadedSubAsset(managed, record.Value);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load managed asset {record.Value.AssetPath}: {exception.Message}");
            }
        }
        if (File.Exists(record.Value.SourcePath))
        {
            try
            {
                var context = new AssetLoadContext(record.Value.Guid, record.Value.AssetPath,
                    record.Value.SourcePath, record.Value.AssetType);
                if (AssetTypeRegistry.Load(context) is { } registered)
                {
                    registered.Id = record.Value.Guid;
                    if (registered is BAsset registeredAsset)
                        registeredAsset.BindAssetReference(record.Value.AssetPath, record.Value.Guid);
                    if (registered is FileAsset fileAsset)
                        fileAsset.BindAssetFile(record.Value.AssetPath, record.Value.SourcePath,
                            record.Value.Guid, record.Value.AssetType);
                    if (string.IsNullOrWhiteSpace(registered.name))
                        registered.name = AssetPathUtility.SplitNameAndExtension(record.Value.AssetPath).Name;
                    return BindLoadedSubAsset(registered, record.Value);
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or
                                              YamlDotNet.Core.YamlException)
            {
                Debug.LogWarning($"Could not load typed asset {record.Value.AssetPath}: {exception.Message}");
            }
        }
        var isText = record.Value.AssetType is "Script" or "YamlAsset" or "Shader" or "JSON" or "XML" or
            "Markdown" or "Text" or "UI Document" or "UI Style Sheet" or "HTML Document";
        FileAsset asset = record.Value.AssetType == "Script" && File.Exists(record.Value.SourcePath)
            ? CreateLegacyScript(record.Value.SourcePath)
            : isText && File.Exists(record.Value.SourcePath)
                ? new TextAsset { text = File.ReadAllText(record.Value.SourcePath) }
                : new DefaultAsset();
        asset.name = Path.GetFileName(record.Value.SourcePath);
        asset.BindAssetFile(record.Value.AssetPath, record.Value.SourcePath,
            record.Value.Guid, record.Value.AssetType);
        return BindLoadedSubAsset(asset, record.Value);
    }

    public static T? LoadAssetAtPath<T>(string assetPath) where T : BObject
    {
        var mainAsset = LoadMainAssetAtPath(assetPath);
        if (mainAsset is T typed) return typed;
        if (typeof(T) == typeof(Sprite) && mainAsset is BEngine.Texture texture)
            return CreateImportedSprite(texture) as T;
        return LoadAllAssetsAtPath(assetPath).OfType<T>().FirstOrDefault();
    }

    public static BObject? LoadAssetAtPath(string assetPath, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var asset = LoadMainAssetAtPath(assetPath);
        if (asset is not null && type.IsInstanceOfType(asset)) return asset;
        if (type == typeof(Sprite) && asset is BEngine.Texture texture)
            return CreateImportedSprite(texture);
        return LoadAllAssetsAtPath(assetPath).FirstOrDefault(type.IsInstanceOfType);
    }

    public static Type? GetMainAssetTypeAtPath(string assetPath) => LoadMainAssetAtPath(assetPath)?.GetType();
    public static BObject[] LoadAllAssetsAtPath(string assetPath)
    {
        if (LoadMainAssetAtPath(assetPath) is not { } asset) return [];
        var assets = new List<BObject> { asset };
        if (asset is BEngine.Texture texture && CreateImportedSprite(texture) is { } sprite)
            assets.Add(sprite);
        var ownerRecord = EditorBridge.Host?.GetAsset(assetPath);
        if (ownerRecord is { ParentGuid: null })
        {
            assets.AddRange(EditorBridge.Host!.FindAssets(string.Empty)
                .Where(record => record.ParentGuid == ownerRecord.Value.Guid)
                .Select(record => LoadAssetRecord(record))
                .OfType<BObject>());
            assets.AddRange(LoadEmbeddedSubAssets(ownerRecord.Value));
        }
        return assets.DistinctBy(item => item.GetInstanceID()).ToArray();
    }

    public static BObject[] LoadAllAssetRepresentationsAtPath(string assetPath) =>
        LoadAllAssetsAtPath(assetPath).Skip(1).ToArray();

    private static Sprite? CreateImportedSprite(BEngine.Texture texture)
    {
        if (AssetImporter.GetAtPath(texture.assetPath) is not TextureImporter
            {
                textureType: TextureImporterType.Sprite
            } importer) return null;
        var sprite = Sprite.FromTexture(texture.assetPath,
            new Vector2((Fix64)importer.spritePivotX, (Fix64)importer.spritePivotY), texture.assetPath);
        sprite.Id = texture.Id;
        sprite.BindAssetFile(texture.assetPath, texture.sourcePath, texture.Id, nameof(Sprite));
        var parentGuid = texture.parentAssetGuid ?? texture.Id;
        sprite.BindSubAssetReference(texture.assetPath, parentGuid, 21300000, texture.Id);
        BindSubAsset(sprite, new SubAssetBinding(texture.assetPath, texture.assetPath,
            parentGuid, 21300000, false, true));
        return sprite;
    }

    public static void CreateAsset(BObject asset, string path)
    {
        ArgumentNullException.ThrowIfNull(asset);
        EditorAssetWritePolicy.EnsureCanWrite("Creating project assets");
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
        else
        {
            var save = asset.GetType().GetMethod("Save", BindingFlags.Instance | BindingFlags.Public,
                binder: null, types: [typeof(string)], modifiers: null);
            if (save is not null)
            {
                try { save.Invoke(asset, [fullPath]); }
                catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
            }
            else YamlUtility.Save(asset, fullPath);
        }
        BAsset.Invalidate(fullPath);
        EditorBridge.Host?.ImportAsset(path);
        if (asset is BAsset createdAsset && Guid.TryParse(AssetPathToGUID(path), out var createdId))
            createdAsset.BindAssetReference(path, createdId);
    }

    public static void ImportAsset(string path, ImportAssetOptions options = ImportAssetOptions.Default)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Importing project assets");
        try { BAsset.Invalidate(ResolveAssetPath(path)); }
        catch (InvalidDataException) { BAsset.Invalidate(path); }
        EditorBridge.Host?.ImportAsset(path);
        lock (SubAssetGate) EmbeddedSubAssetCache.Clear();
        if (path.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            TextureAtlasResolver.Clear();
    }

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
        EditorAssetWritePolicy.EnsureCanWrite("Importing packages");
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
        EditorAssetWritePolicy.EnsureCanWrite("Importing packages");
        var host = EditorBridge.Host ?? throw new InvalidOperationException("No editor project is open.");
        return BPackageArchive.ImportPackage(BEngine.ProjectSystem.ProjectWorkspace.Open(host.ProjectRootPath),
            packagePath, options, progress);
    }

    public static BPackageManifest ReadPackageManifest(string packagePath) =>
        BPackageArchive.ReadManifest(packagePath);

    public static void Refresh(ImportAssetOptions options = ImportAssetOptions.Default)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Refreshing project assets");
        BAsset.ClearLoadedAssets();
        TextureAtlasResolver.Clear();
        EditorBridge.Host?.RefreshAssets();
    }

    public static void SaveAssets()
    {
        EditorAssetWritePolicy.EnsureCanWrite("Saving project assets");
        var dirtyAssets = EditorUtility.GetDirtyObjects().OfType<BAsset>()
            .Where(Contains).ToArray();
        var requested = dirtyAssets.Select(GetAssetPath)
            .Concat(GetAllAssetPaths()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var approved = AssetModificationProcessorDispatcher.OnWillSaveAssets(requested)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in dirtyAssets)
        {
            var path = GetAssetPath(asset);
            if (approved.Contains(path)) SaveAsset(asset);
        }
    }

    public static bool SaveAsset(BAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        EditorAssetWritePolicy.EnsureCanWrite("Saving project assets");
        var assetPath = GetAssetPath(asset);
        if (string.IsNullOrWhiteSpace(assetPath)) return false;
        if (TryGetSubAssetBinding(asset, out var subAsset) && subAsset.IsEmbedded)
            return SaveEmbeddedSubAsset(asset, subAsset);
        var fullPath = ResolveAssetPath(assetPath);
        if (!File.Exists(fullPath)) return false;
        if (asset is Sprite && !IsLegacySpritePath(assetPath)) return false;
        if (asset is BEngine.Texture or BEngine.Font or Script or Shader or DefaultAsset) return false;

        AssetModificationProcessorDispatcher.OnWillSaveAssets([assetPath]);
        if (fullPath.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase))
        {
            new ManagedAssetDocument
            {
                TypeName = asset.GetType().AssemblyQualifiedName ?? asset.GetType().FullName ?? asset.GetType().Name,
                Data = YamlUtility.Serialize(asset)
            }.Save(fullPath);
        }
        else
        {
            var save = asset.GetType().GetMethod("Save", BindingFlags.Instance | BindingFlags.Public,
                binder: null, types: [typeof(string)], modifiers: null);
            if (save is not null)
            {
                try { save.Invoke(asset, [fullPath]); }
                catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
            }
            else
            {
                try { Document.SaveBObject(asset, fullPath); }
                catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
                {
                    YamlUtility.Save(asset, fullPath);
                }
            }
        }
        ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        EditorUtility.ClearDirty(asset);
        return true;
    }

    public static bool RevertAsset(BAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var path = GetAssetPath(asset);
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (TryGetSubAssetBinding(asset, out var binding) && binding.IsEmbedded)
        {
            lock (SubAssetGate) EmbeddedSubAssetCache.Remove(asset.Id);
            var embeddedRestored = LoadEmbeddedSubAssets(EditorBridge.Host?.GetAsset(path) ?? default)
                .FirstOrDefault(item => item.Id == asset.Id);
            if (embeddedRestored is null || embeddedRestored.GetType() != asset.GetType()) return false;
            EditorUtility.CopySerialized(embeddedRestored, asset);
            asset.name = embeddedRestored.name;
            EditorUtility.ClearDirty(asset);
            return true;
        }
        if (asset is Sprite && !IsLegacySpritePath(path)) return false;
        BAsset.Invalidate(path);
        if (LoadMainAssetAtPath(path) is not BAsset restored ||
            restored.GetType() != asset.GetType()) return false;
        EditorUtility.CopySerialized(restored, asset);
        asset.name = restored.name;
        EditorUtility.ClearDirty(asset);
        return true;
    }

    private static bool IsLegacySpritePath(string path) =>
        path.EndsWith(".sprite.yaml", StringComparison.OrdinalIgnoreCase);

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

    public static string CreateFolder(string parentFolder, string newFolderName)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Creating project folders");
        return EditorBridge.Host?.CreateAssetFolder(parentFolder, newFolderName) ?? string.Empty;
    }

    public static bool DeleteAsset(string path)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Deleting project assets");
        var result = AssetModificationProcessorDispatcher.OnWillDeleteAsset(path, RemoveAssetOptions.None);
        if (result == AssetDeleteResult.FailedDelete) return false;
        if (result == AssetDeleteResult.DidDelete) return true;
        var host = EditorBridge.Host;
        if (host is null) return false;
        var owner = host.GetAsset(path);
        if (owner is { ParentGuid: null })
            foreach (var child in host.FindAssets(string.Empty).Where(item => item.ParentGuid == owner.Value.Guid)
                         .ToArray())
            {
                if (!host.DeleteAsset(child.AssetPath)) return false;
                BAsset.ClearLoadedAssets();
            }
        var deleted = host.DeleteAsset(path);
        if (deleted) BAsset.ClearLoadedAssets();
        return deleted;
    }

    public static string MoveAsset(string oldPath, string newPath)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Moving project assets");
        var result = AssetModificationProcessorDispatcher.OnWillMoveAsset(oldPath, newPath);
        if (result == AssetMoveResult.FailedMove) return "Asset move was rejected by a processor.";
        if (result == AssetMoveResult.DidMove) return string.Empty;
        var host = EditorBridge.Host;
        if (host is null) return "No editor project is open.";
        var owner = host.GetAsset(oldPath);
        var atlasChild = owner is { ParentGuid: null } &&
                          oldPath.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase)
            ? host.FindAssets(string.Empty).FirstOrDefault(item =>
                item.ParentGuid == owner.Value.Guid &&
                item.LocalIdentifier == 2800000 &&
                Path.GetExtension(item.AssetPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            : default;
        var outputPath = newPath.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase)
            ? newPath[..^".atlas.yaml".Length] + ".png"
            : Path.ChangeExtension(newPath, ".png").Replace('\\', '/');
        var moveAtlasChild = atlasChild.Guid != Guid.Empty &&
                             !SameAssetPath(atlasChild.AssetPath, outputPath);
        string originalAtlasContents = string.Empty;
        if (atlasChild.Guid != Guid.Empty)
        {
            try
            {
                var oldFullPath = ResolveAssetPath(oldPath);
                originalAtlasContents = File.ReadAllText(oldFullPath);
                _ = TextureAtlas.Load(oldFullPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               InvalidDataException or FormatException or
                                               YamlDotNet.Core.YamlException)
            {
                return $"The texture atlas cannot be moved because it is not readable: {exception.Message}";
            }

            if (moveAtlasChild && (host.GetAsset(outputPath) is not null ||
                                   File.Exists(ResolveAssetPath(outputPath)) ||
                                   Directory.Exists(ResolveAssetPath(outputPath))))
                return $"The generated atlas Texture destination already exists: {outputPath}";
        }
        var moveError = host.MoveAsset(oldPath, newPath);
        if (moveError.Length > 0) return moveError;
        BAsset.ClearLoadedAssets();
        if (atlasChild.Guid == Guid.Empty) return string.Empty;
        var childMoveError = moveAtlasChild ? host.MoveAsset(atlasChild.AssetPath, outputPath) : string.Empty;
        if (childMoveError.Length > 0)
        {
            var rollbackError = TryRollbackAssetMove(host, newPath, oldPath);
            BAsset.ClearLoadedAssets();
            return rollbackError.Length == 0
                ? $"The generated atlas Texture could not be moved; the atlas move was rolled back: {childMoveError}"
                : $"The generated atlas Texture could not be moved: {childMoveError} " +
                  $"Rolling the atlas back also failed: {rollbackError}";
        }
        BAsset.ClearLoadedAssets();
        try
        {
            var atlas = TextureAtlas.Load(ResolveAssetPath(newPath));
            atlas.Texture = outputPath;
            atlas.Save(ResolveAssetPath(newPath));
            host.ImportAsset(newPath);
            TextureAtlasResolver.Clear();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or FormatException or InvalidOperationException or
                                          YamlDotNet.Core.YamlException)
        {
            var childRollbackError = moveAtlasChild
                ? TryRollbackAssetMove(host, outputPath, atlasChild.AssetPath)
                : string.Empty;
            var atlasRollbackError = TryRollbackAssetMove(host, newPath, oldPath);
            var restoredPath = atlasRollbackError.Length == 0 ? oldPath : newPath;
            var restoreError = TryRestoreAtlasContents(host, restoredPath, originalAtlasContents);
            BAsset.ClearLoadedAssets();
            TextureAtlasResolver.Clear();
            var rollbackErrors = new[] { childRollbackError, atlasRollbackError, restoreError }
                .Where(error => error.Length > 0).ToArray();
            return rollbackErrors.Length == 0
                ? $"The atlas Texture reference could not be updated; the move was rolled back: {exception.Message}"
                : $"The atlas Texture reference could not be updated: {exception.Message} " +
                  $"Rollback errors: {string.Join("; ", rollbackErrors)}";
        }
        return string.Empty;
    }

    private static bool SameAssetPath(string left, string right) =>
        left.Replace('\\', '/').Trim('/').Equals(right.Replace('\\', '/').Trim('/'),
            StringComparison.OrdinalIgnoreCase);

    private static string TryRollbackAssetMove(IEditorHost host, string movedPath, string originalPath)
    {
        try { return host.MoveAsset(movedPath, originalPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or InvalidOperationException)
        {
            return exception.Message;
        }
    }

    private static string TryRestoreAtlasContents(IEditorHost host, string assetPath, string contents)
    {
        try
        {
            File.WriteAllText(ResolveAssetPath(assetPath), contents);
            host.ImportAsset(assetPath);
            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or InvalidOperationException)
        {
            return exception.Message;
        }
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

    public static bool IsMainAsset(BObject asset) => Contains(asset) && !IsSubAsset(asset);
    public static bool IsSubAsset(BObject asset) => TryGetSubAssetBinding(asset, out _) ||
        asset is BAsset { parentAssetGuid: not null };
    public static bool IsForeignAsset(BObject asset) => false;
    public static bool IsNativeAsset(BObject asset) => Contains(asset);

    public static void AddObjectToAsset(BObject objectToAdd, BObject assetObject)
    {
        ArgumentNullException.ThrowIfNull(assetObject);
        AddObjectToAsset(objectToAdd, GetAssetPath(assetObject));
    }

    public static void AddObjectToAsset(BObject objectToAdd, string path)
    {
        ArgumentNullException.ThrowIfNull(objectToAdd);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EditorAssetWritePolicy.EnsureCanWrite("Adding sub-assets");
        if (Contains(objectToAdd))
            throw new InvalidOperationException("The object is already stored in the AssetDatabase.");
        var owner = EditorBridge.Host?.GetAsset(path) ??
                    throw new InvalidOperationException($"The main asset '{path}' is not imported.");
        if (owner.ParentGuid.HasValue)
            throw new InvalidOperationException("A sub-asset cannot own another sub-asset.");
        var meta = LoadMeta(owner.SourcePath);
        meta.SubAssets ??= [];
        if (meta.SubAssets.Any(item => Guid.TryParse(item.Guid, out var id) && id == objectToAdd.Id))
            throw new InvalidOperationException("The object is already a sub-asset of this asset.");
        var localId = NextLocalIdentifier(meta);
        meta.SubAssets.Add(new SubAssetMetaDocument
        {
            Guid = objectToAdd.Id.ToString("N"),
            LocalIdentifier = localId,
            Name = objectToAdd.name ?? string.Empty,
            TypeName = objectToAdd.GetType().AssemblyQualifiedName ??
                       objectToAdd.GetType().FullName ?? objectToAdd.GetType().Name,
            Data = YamlUtility.Serialize(objectToAdd)
        });
        meta.Save(owner.SourcePath + ".meta");
        BindSubAsset(objectToAdd, new SubAssetBinding(owner.AssetPath, owner.AssetPath,
            owner.Guid, localId, true));
        if (objectToAdd is BAsset asset)
            asset.BindSubAssetReference(owner.AssetPath, owner.Guid, localId, objectToAdd.Id);
        EditorBridge.Host.ImportAsset(owner.AssetPath);
    }

    public static void RemoveObjectFromAsset(BObject objectToRemove)
    {
        ArgumentNullException.ThrowIfNull(objectToRemove);
        EditorAssetWritePolicy.EnsureCanWrite("Removing sub-assets");
        if (!TryGetSubAssetBinding(objectToRemove, out var binding))
            throw new InvalidOperationException("The object is not a sub-asset.");
        if (binding.IsImportedRepresentation)
            throw new InvalidOperationException("Imported representations are owned by their importer and cannot be removed.");
        if (!binding.IsEmbedded)
        {
            var child = EditorBridge.Host?.GetAsset(binding.SourceAssetPath) ??
                        throw new InvalidOperationException("The file-backed sub-asset is no longer imported.");
            var meta = LoadMeta(child.SourcePath);
            meta.ParentGuid = string.Empty;
            meta.LocalIdentifier = 0;
            meta.Save(child.SourcePath + ".meta");
            SubAssetBindings.Remove(objectToRemove);
            if (objectToRemove is BAsset fileAsset)
                fileAsset.BindAssetReference(child.AssetPath, child.Guid);
            EditorBridge.Host!.ImportAsset(child.AssetPath);
            return;
        }
        var owner = EditorBridge.Host?.GetAsset(binding.ParentAssetPath) ??
                    throw new InvalidOperationException("The sub-asset owner is no longer imported.");
        var ownerMeta = LoadMeta(owner.SourcePath);
        var removed = ownerMeta.SubAssets?.RemoveAll(item =>
            Guid.TryParse(item.Guid, out var id) && id == objectToRemove.Id) ?? 0;
        if (removed == 0) throw new InvalidOperationException("The embedded sub-asset metadata is missing.");
        ownerMeta.Save(owner.SourcePath + ".meta");
        SubAssetBindings.Remove(objectToRemove);
        lock (SubAssetGate) EmbeddedSubAssetCache.Remove(objectToRemove.Id);
        if (objectToRemove is BAsset embeddedAsset)
            embeddedAsset.BindAssetReference(string.Empty, objectToRemove.Id);
        EditorBridge.Host!.ImportAsset(owner.AssetPath);
    }

    public static bool TryGetGUIDAndLocalFileIdentifier(
        BObject asset,
        out string guid,
        out long localId)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (TryGetSubAssetBinding(asset, out var binding))
        {
            guid = binding.ParentGuid.ToString("N");
            localId = binding.LocalIdentifier;
            return true;
        }
        var path = GetAssetPath(asset);
        if (path.Length > 0 && EditorBridge.Host?.GetAsset(path) is { } record)
        {
            guid = record.Guid.ToString("N");
            localId = 0;
            return true;
        }
        guid = string.Empty;
        localId = 0;
        return false;
    }
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

    internal static void RegisterFileSubAsset(
        string childFullPath,
        string parentFullPath,
        long localIdentifier)
    {
        childFullPath = Path.GetFullPath(childFullPath);
        parentFullPath = Path.GetFullPath(parentFullPath);
        if (!File.Exists(childFullPath))
            throw new FileNotFoundException("The sub-asset source file does not exist.", childFullPath);
        if (!File.Exists(parentFullPath))
            throw new FileNotFoundException("The main asset source file does not exist.", parentFullPath);
        var parent = LoadOrCreateMeta(parentFullPath);
        var assignedLocalIdentifier = Math.Max(1, localIdentifier);
        if (parent.SubAssets?.Any(item => item.LocalIdentifier == assignedLocalIdentifier) == true)
            throw new InvalidOperationException(
                $"Sub-asset local identifier {assignedLocalIdentifier} is already assigned to an embedded object.");
        if (FindFileSubAssetCollision(parent.Guid, assignedLocalIdentifier, childFullPath) is { } collision)
            throw new InvalidOperationException(
                $"Sub-asset identity {parent.Guid}/{assignedLocalIdentifier} is already assigned to '{collision}'.");
        var child = LoadOrCreateMeta(childFullPath);
        child.ParentGuid = parent.Guid;
        child.LocalIdentifier = assignedLocalIdentifier;
        child.Save(childFullPath + ".meta");
    }

    private static string? FindFileSubAssetCollision(
        string parentGuid,
        long localIdentifier,
        string childFullPath)
    {
        var canonicalChild = Path.GetFullPath(childFullPath);
        var host = EditorBridge.Host;
        var inMemory = host?.FindAssets(string.Empty).FirstOrDefault(record =>
            record.ParentGuid is { } parent && parent.ToString("N").Equals(parentGuid,
                StringComparison.OrdinalIgnoreCase) &&
            record.LocalIdentifier == localIdentifier &&
            !Path.GetFullPath(record.SourcePath).Equals(canonicalChild, StringComparison.OrdinalIgnoreCase));
        if (inMemory is { } collisionRecord && collisionRecord.Guid != Guid.Empty)
            return collisionRecord.AssetPath;

        var root = host?.AssetsRootPath ?? FindAssetScanRoot(childFullPath);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        string[] metadataPaths;
        try { metadataPaths = Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        foreach (var metaPath in metadataPaths)
        {
            var sourcePath = metaPath[..^".meta".Length];
            if (Path.GetFullPath(sourcePath).Equals(canonicalChild, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var meta = Document.Load<AssetMetaDocument>(metaPath);
                if (meta.LocalIdentifier == localIdentifier &&
                    meta.ParentGuid.Equals(parentGuid, StringComparison.OrdinalIgnoreCase))
                    return host is null ? sourcePath : ToProjectPath(sourcePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidDataException or FormatException or
                                              YamlDotNet.Core.YamlException) { }
        }
        return null;
    }

    private static string FindAssetScanRoot(string path)
    {
        for (var directory = Directory.GetParent(Path.GetFullPath(path)); directory is not null;
             directory = directory.Parent)
            if (directory.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase)) return directory.FullName;
        return Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
    }

    private static T BindLoadedSubAsset<T>(T asset, EditorAssetRecord record) where T : BObject
    {
        if (record.ParentGuid is not { } parentGuid) return asset;
        var owner = EditorBridge.Host?.GetAsset(parentGuid);
        var parentPath = owner?.AssetPath ?? record.AssetPath;
        var localId = Math.Max(1, record.LocalIdentifier);
        if (asset is BAsset subAsset)
            subAsset.BindSubAssetReference(parentPath, parentGuid, localId, record.Guid);
        BindSubAsset(asset, new SubAssetBinding(parentPath, record.AssetPath,
            parentGuid, localId, false));
        return asset;
    }

    private static IReadOnlyList<BObject> LoadEmbeddedSubAssets(EditorAssetRecord owner)
    {
        if (owner.Guid == Guid.Empty || owner.ParentGuid.HasValue || !File.Exists(owner.SourcePath + ".meta"))
            return [];
        var meta = LoadMeta(owner.SourcePath);
        if (meta.SubAssets is not { Count: > 0 }) return [];
        var result = new List<BObject>(meta.SubAssets.Count);
        foreach (var document in meta.SubAssets.OrderBy(item => item.LocalIdentifier))
        {
            if (!Guid.TryParse(document.Guid, out var objectId) || document.LocalIdentifier <= 0 ||
                string.IsNullOrWhiteSpace(document.TypeName) || string.IsNullOrWhiteSpace(document.Data))
                continue;
            BObject? loaded = null;
            lock (SubAssetGate)
                if (EmbeddedSubAssetCache.TryGetValue(objectId, out var weak) &&
                    weak.TryGetTarget(out var cached)) loaded = cached;
            if (loaded is null)
            {
                var type = ResolveManagedAssetType(document.TypeName);
                if (type is null) continue;
                try { loaded = YamlUtility.Deserialize(document.Data, type) as BObject; }
                catch (Exception exception) when (exception is InvalidDataException or FormatException or
                                                  YamlDotNet.Core.YamlException)
                {
                    Debug.LogWarning($"Could not load sub-asset '{document.Name}' from {owner.AssetPath}: " +
                                     exception.Message);
                    continue;
                }
                if (loaded is null) continue;
                loaded.Id = objectId;
                if (string.IsNullOrWhiteSpace(loaded.name)) loaded.name = document.Name;
                lock (SubAssetGate) EmbeddedSubAssetCache[objectId] = new WeakReference<BObject>(loaded);
            }
            if (loaded is BAsset asset)
                asset.BindSubAssetReference(owner.AssetPath, owner.Guid,
                    document.LocalIdentifier, objectId);
            BindSubAsset(loaded, new SubAssetBinding(owner.AssetPath, owner.AssetPath,
                owner.Guid, document.LocalIdentifier, true));
            result.Add(loaded);
        }
        return result;
    }

    private static bool SaveEmbeddedSubAsset(BAsset asset, SubAssetBinding binding)
    {
        var owner = EditorBridge.Host?.GetAsset(binding.ParentAssetPath);
        if (owner is null) return false;
        var meta = LoadMeta(owner.Value.SourcePath);
        var document = meta.SubAssets?.FirstOrDefault(item =>
            Guid.TryParse(item.Guid, out var id) && id == asset.Id);
        if (document is null) return false;
        AssetModificationProcessorDispatcher.OnWillSaveAssets([owner.Value.AssetPath]);
        document.Name = asset.name ?? string.Empty;
        document.TypeName = asset.GetType().AssemblyQualifiedName ??
                            asset.GetType().FullName ?? asset.GetType().Name;
        document.Data = YamlUtility.Serialize(asset);
        meta.Save(owner.Value.SourcePath + ".meta");
        EditorBridge.Host!.ImportAsset(owner.Value.AssetPath);
        EditorUtility.ClearDirty(asset);
        return true;
    }

    private static AssetMetaDocument LoadMeta(string sourcePath)
    {
        var metaPath = sourcePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            ? sourcePath
            : sourcePath + ".meta";
        var meta = Document.Load<AssetMetaDocument>(metaPath);
        meta.Settings ??= [];
        meta.SubAssets ??= [];
        return meta;
    }

    internal static AssetMetaDocument LoadOrCreateMeta(string sourcePath)
    {
        var metaPath = sourcePath + ".meta";
        if (File.Exists(metaPath)) return LoadMeta(sourcePath);
        var document = new AssetMetaDocument
        {
            Guid = Guid.NewGuid().ToString("N"),
            AssetType = AssetTypeRegistry.Resolve(sourcePath) ??
                        (Path.GetExtension(sourcePath).Equals(".png", StringComparison.OrdinalIgnoreCase)
                            ? "Texture"
                            : "DefaultAsset"),
            Importer = Path.GetExtension(sourcePath).Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? nameof(TextureImporter)
                : "DefaultImporter"
        };
        document.Save(metaPath);
        return document;
    }

    private static long NextLocalIdentifier(AssetMetaDocument meta)
    {
        var highestExisting = (meta.SubAssets ?? []).Select(item => item.LocalIdentifier)
            .Where(value => value > 0).DefaultIfEmpty(99999).Max();
        var candidate = Math.Max(100000, Math.Max(meta.NextLocalIdentifier, highestExisting + 1));
        meta.NextLocalIdentifier = checked(candidate + 1);
        return candidate;
    }

    private static void BindSubAsset(BObject asset, SubAssetBinding binding)
    {
        SubAssetBindings.Remove(asset);
        SubAssetBindings.Add(asset, binding);
    }

    private static bool TryGetSubAssetBinding(BObject asset, out SubAssetBinding binding)
    {
        if (SubAssetBindings.TryGetValue(asset, out binding!)) return true;
        if (asset is BAsset { parentAssetGuid: { } parentGuid } subAsset)
        {
            var localId = Math.Max(1, subAsset.localIdentifier);
            var owner = EditorBridge.Host?.GetAsset(parentGuid);
            if (owner is not null)
            {
                var ownerMeta = LoadMeta(owner.Value.SourcePath);
                var embedded = ownerMeta.SubAssets?.Any(item => item.LocalIdentifier == localId &&
                    Guid.TryParse(item.Guid, out var id) && id == subAsset.Id) == true;
                if (embedded)
                {
                    binding = new SubAssetBinding(owner.Value.AssetPath, owner.Value.AssetPath,
                        parentGuid, localId, true);
                    BindSubAsset(asset, binding);
                    return true;
                }
                var child = EditorBridge.Host!.FindAssets(string.Empty).FirstOrDefault(item =>
                    item.ParentGuid == parentGuid && item.LocalIdentifier == localId);
                if (child.Guid != Guid.Empty)
                {
                    binding = new SubAssetBinding(owner.Value.AssetPath, child.AssetPath,
                        parentGuid, localId, false);
                    BindSubAsset(asset, binding);
                    return true;
                }
            }
            binding = new SubAssetBinding(subAsset.assetPath, subAsset.assetPath, parentGuid,
                localId, false, true);
            BindSubAsset(asset, binding);
            return true;
        }
        binding = null!;
        return false;
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

    private static MonoScript CreateLegacyScript(string sourcePath)
    {
        var script = new MonoScript { name = Path.GetFileName(sourcePath) };
        script.SetImportedContents(File.ReadAllText(sourcePath),
            FindScriptClass(Path.GetFileNameWithoutExtension(sourcePath)));
        return script;
    }

    private sealed record SubAssetBinding(
        string ParentAssetPath,
        string SourceAssetPath,
        Guid ParentGuid,
        long LocalIdentifier,
        bool IsEmbedded,
        bool IsImportedRepresentation = false);
}
