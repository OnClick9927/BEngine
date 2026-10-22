using BEngine.Serialization;

namespace BEngine.Editor;

public static partial class PrefabUtility
{
    public static event Action<GameObject>? prefabInstanceUpdated;

    public static BObject InstantiatePrefab(BObject asset) => InstantiatePrefab(asset, EditorSceneManager.activeScene);

    public static BObject InstantiatePrefab(BObject asset, Scene? destinationScene)
    {
        if (asset is not PrefabAsset prefab)
            throw new ArgumentException("InstantiatePrefab requires a PrefabAsset.", nameof(asset));
        var scene = destinationScene ?? throw new InvalidOperationException("No destination scene is open.");
        var instance = PrefabAssetOperations.Instantiate(prefab, scene);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = instance;
        RaiseInstanceUpdated(instance);
        return instance;
    }

    public static GameObject? SaveAsPrefabAsset(GameObject instanceRoot, string assetPath) =>
        SaveAsPrefabAsset(instanceRoot, assetPath, out _);

    public static GameObject? SaveAsPrefabAsset(GameObject instanceRoot, string assetPath, out bool success)
    {
        var asset = SaveAsset(instanceRoot, assetPath, connect: false, out success);
        return asset is null ? null : PrefabAssetOperations.LoadContents(asset);
    }

    public static GameObject? SaveAsPrefabAssetAndConnect(GameObject instanceRoot, string assetPath,
        InteractionMode action) => SaveAsPrefabAssetAndConnect(instanceRoot, assetPath, action, out _);

    public static GameObject? SaveAsPrefabAssetAndConnect(GameObject instanceRoot, string assetPath,
        InteractionMode action, out bool success)
    {
        var asset = SaveAsset(instanceRoot, assetPath, connect: true, out success);
        if (asset is null) return null;
        RaiseInstanceUpdated(instanceRoot);
        return instanceRoot;
    }

    public static GameObject SavePrefabAsset(GameObject assetRoot) => SavePrefabAsset(assetRoot, out _);

    public static GameObject SavePrefabAsset(GameObject assetRoot, out bool success)
    {
        ArgumentNullException.ThrowIfNull(assetRoot);
        var path = GetPrefabAssetPathOfNearestInstanceRoot(assetRoot);
        if (string.IsNullOrWhiteSpace(path))
        {
            success = false;
            throw new InvalidOperationException("GameObject is not connected to a prefab asset.");
        }
        SaveAsset(GetNearestPrefabInstanceRoot(assetRoot) ?? assetRoot, path, connect: true, out success);
        return assetRoot;
    }

    public static void ApplyPrefabInstance(GameObject instanceRoot, InteractionMode action = InteractionMode.UserAction)
    {
        var root = GetNearestPrefabInstanceRoot(instanceRoot) ?? throw new InvalidOperationException("Object is not a prefab instance.");
        EditInstance(root, "Apply Prefab Instance", action, () =>
        {
            var path = GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (action == InteractionMode.UserAction) RegisterPrefabFileUndo(path, "Apply Prefab Instance");
            SaveAsset(root, path, connect: true, out _);
        });
    }

    public static void RevertPrefabInstance(GameObject instanceRoot,
        InteractionMode action = InteractionMode.UserAction)
    {
        EnsurePrefabEditing(instanceRoot);
        using var context = new OverrideContext(instanceRoot);
        EditInstance(context.Root, "Revert Prefab Instance", action, () => RestoreHierarchy(context));
    }

    public static void UnpackPrefabInstance(GameObject instanceRoot, PrefabUnpackMode unpackMode,
        InteractionMode action = InteractionMode.UserAction)
    {
        EnsurePrefabEditing(instanceRoot);
        var root = GetNearestPrefabInstanceRoot(instanceRoot) ?? instanceRoot;
        if (action == InteractionMode.UserAction) Undo.RegisterFullObjectHierarchyUndo(root, "Unpack Prefab");
        PrefabAssetOperations.DisconnectHierarchy(root, unpackMode == PrefabUnpackMode.Completely);
        EditorSceneManager.MarkSceneDirty(root.scene!);
        RaiseInstanceUpdated(root);
    }

    public static GameObject LoadPrefabContents(string assetPath)
    {
        var prefab = LoadPrefabAsset(assetPath);
        return PrefabAssetOperations.LoadContents(prefab);
    }

    public static void UnloadPrefabContents(GameObject contentsRoot)
    {
        ArgumentNullException.ThrowIfNull(contentsRoot);
        BObject.DestroyImmediate(contentsRoot);
    }

    public static GameObject SaveAsPrefabAsset(GameObject contentsRoot, string assetPath,
        out bool success, bool connectToInstance)
    {
        SaveAsset(contentsRoot, assetPath, connectToInstance, out success);
        return contentsRoot;
    }

    public static bool IsPartOfAnyPrefab(BObject? componentOrGameObject) =>
        componentOrGameObject is PrefabAsset || GetLink(componentOrGameObject).Asset.HasValue;
    public static bool IsPartOfPrefabAsset(BObject? componentOrGameObject) => componentOrGameObject is PrefabAsset;
    public static bool IsPartOfPrefabInstance(BObject? componentOrGameObject) =>
        componentOrGameObject is not PrefabAsset && GetLink(componentOrGameObject).Asset.HasValue;
    public static bool IsAnyPrefabInstanceRoot(GameObject? gameObject) => gameObject is not null &&
        gameObject.PrefabAssetId.HasValue && (gameObject.PrefabInstance is not null || gameObject.transform.parent is null ||
        gameObject.transform.parent.gameObject.PrefabAssetId != gameObject.PrefabAssetId);
    public static bool IsOutermostPrefabInstanceRoot(GameObject? gameObject) => gameObject is not null &&
        ReferenceEquals(GetOutermostPrefabInstanceRoot(gameObject), gameObject);

    public static GameObject? GetNearestPrefabInstanceRoot(BObject? componentOrGameObject)
    {
        var gameObject = ToGameObject(componentOrGameObject);
        for (var current = gameObject; current is not null; current = current.transform.parent?.gameObject)
            if (IsAnyPrefabInstanceRoot(current)) return current;
        return null;
    }

    public static GameObject? GetOutermostPrefabInstanceRoot(BObject? componentOrGameObject)
    {
        GameObject? result = null;
        for (var current = ToGameObject(componentOrGameObject); current is not null; current = current.transform.parent?.gameObject)
            if (IsAnyPrefabInstanceRoot(current)) result = current;
        return result;
    }

    public static string GetPrefabAssetPathOfNearestInstanceRoot(BObject? componentOrGameObject)
    {
        if (componentOrGameObject is PrefabAsset asset) return asset.assetPath;
        var root = GetNearestPrefabInstanceRoot(componentOrGameObject) ?? ToGameObject(componentOrGameObject);
        if (root?.PrefabInstance is { } reference)
        {
            if (!reference.Asset.StartsWith("guid:", StringComparison.OrdinalIgnoreCase)) return reference.Asset;
            var path = AssetDatabase.GUIDToAssetPath(reference.Asset[5..]);
            if (!string.IsNullOrWhiteSpace(path)) return path;
            return BAsset.LoadByGuid<PrefabAsset>(reference.Asset[5..])?.assetPath ?? string.Empty;
        }
        var assetId = GetLink(componentOrGameObject).Asset;
        return assetId.HasValue ? AssetDatabase.GUIDToAssetPath(assetId.Value.ToString("N")) : string.Empty;
    }

    public static PrefabAssetType GetPrefabAssetType(BObject? componentOrGameObject)
    {
        if (!IsPartOfAnyPrefab(componentOrGameObject)) return PrefabAssetType.NotAPrefab;
        var prefab = componentOrGameObject as PrefabAsset;
        if (prefab is null)
        {
            var path = GetPrefabAssetPathOfNearestInstanceRoot(componentOrGameObject);
            if (string.IsNullOrWhiteSpace(path)) return PrefabAssetType.MissingAsset;
            prefab = LoadPrefabAsset(path);
        }
        return prefab.isVariant ? PrefabAssetType.Variant : PrefabAssetType.Regular;
    }

    public static PrefabInstanceStatus GetPrefabInstanceStatus(BObject? componentOrGameObject)
    {
        if (!IsPartOfPrefabInstance(componentOrGameObject)) return PrefabInstanceStatus.NotAPrefab;
        return string.IsNullOrWhiteSpace(GetPrefabAssetPathOfNearestInstanceRoot(componentOrGameObject))
            ? PrefabInstanceStatus.MissingAsset : PrefabInstanceStatus.Connected;
    }

    public static bool HasPrefabInstanceAnyOverrides(GameObject instanceRoot, bool includeDefaultOverrides = false)
    {
        if (GetNearestPrefabInstanceRoot(instanceRoot) is null) return false;
        return GetOverrides(instanceRoot, includeDefaultOverrides).Count > 0;
    }

    public static T? GetCorrespondingObjectFromSource<T>(T componentOrGameObject) where T : BObject
    {
        var sourceId = GetLink(componentOrGameObject).Source;
        var path = GetPrefabAssetPathOfNearestInstanceRoot(componentOrGameObject);
        if (!sourceId.HasValue || string.IsNullOrWhiteSpace(path)) return null;
        var root = LoadPrefabContents(path);
        return PrefabAssetOperations.Traverse(root).SelectMany(gameObject =>
                new BObject[] { gameObject, gameObject.transform }.Concat(gameObject.components))
            .OfType<T>().FirstOrDefault(item => item.Id == sourceId.Value);
    }

    private static PrefabAsset? SaveAsset(GameObject root, string assetPath, bool connect, out bool success, PrefabAsset? variantBase = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        success = false;
        if (!EditorAssetWritePolicy.CanWrite || root.IsRuntimeOnly)
            throw new InvalidOperationException(
                "Prefab assets cannot be saved while entering, running, or exiting Play Mode.");
        var (projectPath, fullPath) = ResolveAssetPath(assetPath);
        var existingGuid = AssetDatabase.AssetPathToGUID(projectPath);
        var assetId = Guid.TryParse(existingGuid, out var parsed) ? parsed : (Guid?)null;
        try
        {
            var saved = variantBase is null ? SaveDocument(root, fullPath, assetId) : PrefabAssetSerialization.SaveVariant(root, variantBase, fullPath, assetId);
            if (EditorBridge.Host is null)
            {
                if (connect) PrefabAssetOperations.ConnectHierarchy(root, saved);
                success = true;
                return saved;
            }
            AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceSynchronousImport);
            var guid = AssetDatabase.AssetPathToGUID(projectPath);
            assetId = Guid.TryParse(guid, out parsed) ? parsed : assetId;
            if (assetId.HasValue)
            {
                if (variantBase is null) SaveDocument(root, fullPath, assetId);
                else PrefabAssetSerialization.SaveVariant(root, variantBase, fullPath, assetId);
                AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceUpdate);
                if (connect) PrefabAssetOperations.ConnectHierarchy(root, LoadPrefabAsset(projectPath));
            }
            EditorSceneManager.MarkSceneDirty();
            success = true;
            return AssetDatabase.LoadAssetAtPath<PrefabAsset>(projectPath) ??
                   PrefabAssetSerialization.Load(fullPath);
        }
        catch
        {
            throw;
        }
    }

    private static PrefabAsset LoadPrefabAsset(string assetPath)
    {
        var (projectPath, fullPath) = ResolveAssetPath(assetPath);
        return AssetDatabase.LoadAssetAtPath<PrefabAsset>(projectPath) ??
               PrefabAssetSerialization.Load(fullPath);
    }

    private static PrefabAsset SaveDocument(GameObject root, string path, Guid? assetId)
    {
        return PrefabAssetSerialization.Save(root, path, assetId);
    }

    private static (string ProjectPath, string FullPath) ResolveAssetPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var host = EditorBridge.Host;
        var fullPath = EditorHostServices.Paths.IsPathRooted(path) ? EditorHostServices.Paths.GetFullPath(path) : host is null
            ? EditorHostServices.Paths.GetFullPath(path) : EditorHostServices.Paths.GetFullPath(EditorHostServices.Paths.Combine(host.ProjectRootPath, path.Replace('/', '\\')));
        if (host is null) return (fullPath, fullPath);
        var assetsRoot = EditorHostServices.Paths.GetFullPath(host.AssetsRootPath);
        if (!fullPath.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(assetsRoot + EditorHostServices.Paths.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prefab assets must be stored inside the project Assets folder.");
        return (EditorHostServices.Paths.GetRelativePath(host.ProjectRootPath, fullPath).Replace('\\', '/'), fullPath);
    }

    private static (Guid? Asset, Guid? Source) GetLink(BObject? target) => target switch
    {
        GameObject gameObject => (gameObject.PrefabAssetId, gameObject.PrefabSourceId),
        Component component => (component.PrefabAssetId, component.PrefabSourceId),
        _ => (null, null)
    };
    private static GameObject? ToGameObject(BObject? target) => target switch
    { GameObject gameObject => gameObject, Component component => component.gameObject, _ => null };
    private static void RaiseInstanceUpdated(GameObject root)
        => EditorCallbackDispatcher.Invoke(prefabInstanceUpdated, root, nameof(prefabInstanceUpdated));
}
