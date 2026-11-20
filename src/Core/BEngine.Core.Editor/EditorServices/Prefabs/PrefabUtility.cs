using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
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
        EditInstance(context.Root, "Revert Prefab Instance", action, () => context.RestoreHierarchy(CopyObject));
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
        componentOrGameObject is PrefabAsset || PrefabAssetOperations.GetSourceIdentity(componentOrGameObject).Asset.HasValue;
    public static bool IsPartOfPrefabAsset(BObject? componentOrGameObject) => componentOrGameObject is PrefabAsset;
    public static bool IsPartOfPrefabInstance(BObject? componentOrGameObject) =>
        componentOrGameObject is not PrefabAsset && PrefabAssetOperations.GetSourceIdentity(componentOrGameObject).Asset.HasValue;
    public static bool IsAnyPrefabInstanceRoot(GameObject? gameObject) => PrefabAssetOperations.IsInstanceRoot(gameObject);
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
        if (PrefabAssetOperations.GetInstanceReference(root) is { } reference)
        {
            if (!reference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase)) return reference;
            var path = AssetDatabase.GUIDToAssetPath(reference[5..]);
            if (!string.IsNullOrWhiteSpace(path)) return path;
            return BAsset.LoadByGuid<PrefabAsset>(reference[5..])?.assetPath ?? string.Empty;
        }
        var assetId = PrefabAssetOperations.GetSourceIdentity(componentOrGameObject).Asset;
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
        var sourceId = PrefabAssetOperations.GetSourceIdentity(componentOrGameObject).Source;
        var path = GetPrefabAssetPathOfNearestInstanceRoot(componentOrGameObject);
        if (!sourceId.HasValue || string.IsNullOrWhiteSpace(path)) return null;
        var root = LoadPrefabContents(path);
        [global::BEngine.SkipLoopGuard]
        T? Query9149(global::System.Collections.Generic.IEnumerable<T> querySource9149)
        {
            ArgumentNullException.ThrowIfNull(querySource9149, "source");
            bool Matches9149(T item) => item.Id == sourceId.Value;
            global::System.ReadOnlySpan<T> queryValues9149;
            if (querySource9149.GetType() == typeof(T[]))
                queryValues9149 = (T[])querySource9149;
            else if (querySource9149.GetType() == typeof(global::System.Collections.Generic.List<T>))
                queryValues9149 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<T>)querySource9149);
            else
            {
                foreach (var item in querySource9149)
                    if (Matches9149(item))
                        return item;
                return default!;
            }
        
            foreach (var item in queryValues9149)
                if (Matches9149(item))
                    return item;
            return default!;
        }
        
        return Query9149(PrefabAssetOperations.Traverse(root).SelectMany(gameObject => new BObject[] { gameObject, gameObject.transform }.Concat(gameObject.components)).OfType<T>());
    }

    private static PrefabAsset? SaveAsset(GameObject root, string assetPath, bool connect, out bool success, PrefabAsset? variantBase = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        success = false;
        if (!EditorAssetWritePolicy.CanWrite || RuntimeAssetReferences.IsTransient(root))
            throw new InvalidOperationException(
                "Prefab assets cannot be saved while entering, running, or exiting Play Mode.");
        var (projectPath, fullPath) = ResolveAssetPath(assetPath);
        var existingGuid = AssetDatabase.AssetPathToGUID(projectPath);
        var assetId = Guid.TryParse(existingGuid, out var parsed) ? parsed : (Guid?)null;
        try
        {
            var saved = variantBase is null ? SaveDocument(root, fullPath, assetId) : PrefabService<IPrefabAuthoringBackend>().SaveVariant(root, variantBase, fullPath, assetId);
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
                else PrefabService<IPrefabAuthoringBackend>().SaveVariant(root, variantBase, fullPath, assetId);
                AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceUpdate);
                if (connect) PrefabAssetOperations.ConnectHierarchy(root, LoadPrefabAsset(projectPath));
            }
            EditorSceneManager.MarkSceneDirty();
            success = true;
            return AssetDatabase.LoadAssetAtPath<PrefabAsset>(projectPath) ??
                   PrefabService<IPrefabDocumentCodec>().Load(fullPath);
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
               PrefabService<IPrefabDocumentCodec>().Load(fullPath);
    }

    private static PrefabAsset SaveDocument(GameObject root, string path, Guid? assetId)
    {
        return PrefabService<IPrefabDocumentCodec>().Save(root, path, assetId);
    }

    private static T PrefabService<T>() where T : class =>
        DependencyInjection.RuntimeServiceContributions.GetDefault<T>() ??
        throw new InvalidOperationException($"No prefab service is registered for {typeof(T).Name}.");

    private static (string ProjectPath, string FullPath) ResolveAssetPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var host = EditorBridge.Host;
        var fullPath = EditorCapabilities.GetRequiredService<PathUtility>().IsPathRooted(path) ? EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(path) : host is null
            ? EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(path) : EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(EditorCapabilities.GetRequiredService<PathUtility>().Combine(host.ProjectRootPath, path.Replace('/', '\\')));
        if (host is null) return (fullPath, fullPath);
        var assetsRoot = EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(host.AssetsRootPath);
        if (!fullPath.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(assetsRoot + EditorCapabilities.GetRequiredService<PathUtility>().DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prefab assets must be stored inside the project Assets folder.");
        return (EditorCapabilities.GetRequiredService<PathUtility>().GetRelativePath(host.ProjectRootPath, fullPath).Replace('\\', '/'), fullPath);
    }

    private static GameObject? ToGameObject(BObject? target) => target switch
    { GameObject gameObject => gameObject, Component component => component.gameObject, _ => null };
    private static void RaiseInstanceUpdated(GameObject root)
        => EditorCallbacks.Dispatch(prefabInstanceUpdated, root, nameof(prefabInstanceUpdated));
}
