using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class PrefabUtility
{
    public static event Action<GameObject>? prefabInstanceUpdated;

    public static BObject InstantiatePrefab(BObject asset) => InstantiatePrefab(asset, EditorSceneManager.activeScene);

    public static BObject InstantiatePrefab(BObject asset, Scene? destinationScene)
    {
        if (asset is not PrefabAsset prefab)
            throw new ArgumentException("InstantiatePrefab requires a PrefabAsset.", nameof(asset));
        var scene = destinationScene ?? throw new InvalidOperationException("No destination scene is open.");
        var instance = PrefabDocumentOperations.Instantiate(prefab, scene);
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
        return asset is null ? null : PrefabDocumentOperations.LoadContents(asset);
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
        SaveAsset(GetOutermostPrefabInstanceRoot(assetRoot) ?? assetRoot, path, connect: true, out success);
        return assetRoot;
    }

    public static void ApplyPrefabInstance(GameObject instanceRoot, InteractionMode action = InteractionMode.UserAction)
    {
        SavePrefabAsset(GetOutermostPrefabInstanceRoot(instanceRoot) ?? instanceRoot, out var success);
        if (success) RaiseInstanceUpdated(instanceRoot);
    }

    public static void RevertPrefabInstance(GameObject instanceRoot,
        InteractionMode action = InteractionMode.UserAction)
    {
        ArgumentNullException.ThrowIfNull(instanceRoot);
        var root = GetOutermostPrefabInstanceRoot(instanceRoot) ??
                   throw new InvalidOperationException("GameObject is not a prefab instance.");
        var path = GetPrefabAssetPathOfNearestInstanceRoot(root);
        var prefab = AssetDatabase.LoadAssetAtPath<PrefabAsset>(path) ??
                     throw new InvalidDataException($"Prefab asset is missing: {path}");
        var scene = root.scene ?? throw new InvalidOperationException("Prefab instance is not in a Scene.");
        var parent = root.transform.parent;
        var replacement = PrefabDocumentOperations.Instantiate(prefab, scene, parent);
        scene.Destroy(root);
        Selection.activeGameObject = replacement;
        EditorSceneManager.MarkSceneDirty(scene);
        RaiseInstanceUpdated(replacement);
    }

    public static void UnpackPrefabInstance(GameObject instanceRoot, PrefabUnpackMode unpackMode,
        InteractionMode action = InteractionMode.UserAction)
    {
        var root = GetOutermostPrefabInstanceRoot(instanceRoot) ?? instanceRoot;
        PrefabDocumentOperations.DisconnectHierarchy(root);
        EditorSceneManager.MarkSceneDirty(root.scene!);
        RaiseInstanceUpdated(root);
    }

    public static GameObject LoadPrefabContents(string assetPath)
    {
        var prefab = LoadPrefabAsset(assetPath);
        return PrefabDocumentOperations.LoadContents(prefab);
    }

    public static void UnloadPrefabContents(GameObject contentsRoot)
    {
        ArgumentNullException.ThrowIfNull(contentsRoot);
        if (contentsRoot.scene is { } scene) scene.Destroy(contentsRoot);
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
        gameObject.PrefabAssetId.HasValue && (gameObject.transform.parent is null ||
        gameObject.transform.parent.gameObject.PrefabAssetId != gameObject.PrefabAssetId);
    public static bool IsOutermostPrefabInstanceRoot(GameObject? gameObject) => IsAnyPrefabInstanceRoot(gameObject);

    public static GameObject? GetNearestPrefabInstanceRoot(BObject? componentOrGameObject)
    {
        var gameObject = ToGameObject(componentOrGameObject);
        if (gameObject?.PrefabAssetId is not { } assetId) return null;
        for (var current = gameObject; current is not null; current = current.transform.parent?.gameObject)
        {
            if (current.transform.parent?.gameObject.PrefabAssetId != assetId) return current;
        }
        return gameObject;
    }

    public static GameObject? GetOutermostPrefabInstanceRoot(BObject? componentOrGameObject) =>
        GetNearestPrefabInstanceRoot(componentOrGameObject);

    public static string GetPrefabAssetPathOfNearestInstanceRoot(BObject? componentOrGameObject)
    {
        if (componentOrGameObject is PrefabAsset asset) return asset.assetPath;
        var assetId = GetLink(componentOrGameObject).Asset;
        return assetId.HasValue ? AssetDatabase.GUIDToAssetPath(assetId.Value.ToString("N")) : string.Empty;
    }

    public static PrefabAssetType GetPrefabAssetType(BObject? componentOrGameObject) =>
        IsPartOfAnyPrefab(componentOrGameObject) ? PrefabAssetType.Regular : PrefabAssetType.NotAPrefab;

    public static PrefabInstanceStatus GetPrefabInstanceStatus(BObject? componentOrGameObject)
    {
        if (!IsPartOfPrefabInstance(componentOrGameObject)) return PrefabInstanceStatus.NotAPrefab;
        return string.IsNullOrWhiteSpace(GetPrefabAssetPathOfNearestInstanceRoot(componentOrGameObject))
            ? PrefabInstanceStatus.MissingAsset : PrefabInstanceStatus.Connected;
    }

    public static bool HasPrefabInstanceAnyOverrides(GameObject instanceRoot, bool includeDefaultOverrides = false)
    {
        var path = GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
        if (string.IsNullOrWhiteSpace(path)) return false;
        var prefab = AssetDatabase.LoadAssetAtPath<PrefabAsset>(path);
        if (prefab is null) return false;
        var currentDocument = Document.FromBObject<PrefabDocument>(
            GetOutermostPrefabInstanceRoot(instanceRoot) ?? instanceRoot);
        currentDocument.Id = prefab.assetId;
        var current = currentDocument.ToYaml();
        var source = prefab.Document.ToYaml();
        return !string.Equals(current, source, StringComparison.Ordinal);
    }

    public static T? GetCorrespondingObjectFromSource<T>(T componentOrGameObject) where T : BObject
    {
        var sourceId = GetLink(componentOrGameObject).Source;
        var path = GetPrefabAssetPathOfNearestInstanceRoot(componentOrGameObject);
        if (!sourceId.HasValue || string.IsNullOrWhiteSpace(path)) return null;
        var root = LoadPrefabContents(path);
        return PrefabDocumentOperations.Traverse(root).SelectMany(gameObject =>
                new BObject[] { gameObject, gameObject.transform }.Concat(gameObject.components))
            .OfType<T>().FirstOrDefault(item => item.Id == sourceId.Value);
    }

    private static PrefabAsset? SaveAsset(GameObject root, string assetPath, bool connect, out bool success)
    {
        ArgumentNullException.ThrowIfNull(root);
        var (projectPath, fullPath) = ResolveAssetPath(assetPath);
        var existingGuid = AssetDatabase.AssetPathToGUID(projectPath);
        var assetId = Guid.TryParse(existingGuid, out var parsed) ? parsed : (Guid?)null;
        try
        {
            var saved = SaveDocument(root, fullPath, assetId);
            if (EditorBridge.Host is null)
            {
                if (connect) PrefabDocumentOperations.ConnectHierarchy(root, saved.assetId);
                success = true;
                return saved;
            }
            AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceSynchronousImport);
            var guid = AssetDatabase.AssetPathToGUID(projectPath);
            assetId = Guid.TryParse(guid, out parsed) ? parsed : assetId;
            if (assetId.HasValue)
            {
                SaveDocument(root, fullPath, assetId);
                AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceUpdate);
                if (connect) PrefabDocumentOperations.ConnectHierarchy(root, assetId.Value);
            }
            EditorSceneManager.MarkSceneDirty();
            success = true;
            return AssetDatabase.LoadAssetAtPath<PrefabAsset>(projectPath) ??
                   Document.LoadBObject<PrefabDocument, PrefabAsset>(fullPath);
        }
        catch
        {
            success = false;
            throw;
        }
    }

    private static PrefabAsset LoadPrefabAsset(string assetPath)
    {
        var (projectPath, fullPath) = ResolveAssetPath(assetPath);
        return AssetDatabase.LoadAssetAtPath<PrefabAsset>(projectPath) ??
               Document.LoadBObject<PrefabDocument, PrefabAsset>(fullPath);
    }

    private static PrefabAsset SaveDocument(GameObject root, string path, Guid? assetId)
    {
        var document = Document.FromBObject<PrefabDocument>(root, new DocumentConversionContext(path));
        if (assetId.HasValue) document.Id = assetId.Value;
        document.Save(path);
        return (PrefabAsset)document.ToBObject(new DocumentConversionContext(path));
    }

    private static (string ProjectPath, string FullPath) ResolveAssetPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var host = EditorBridge.Host;
        var fullPath = Path.IsPathRooted(path) ? Path.GetFullPath(path) : host is null
            ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(host.ProjectRootPath, path.Replace('/', '\\')));
        if (host is null) return (fullPath, fullPath);
        var assetsRoot = Path.GetFullPath(host.AssetsRootPath);
        if (!fullPath.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prefab assets must be stored inside the project Assets folder.");
        return (Path.GetRelativePath(host.ProjectRootPath, fullPath).Replace('\\', '/'), fullPath);
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
