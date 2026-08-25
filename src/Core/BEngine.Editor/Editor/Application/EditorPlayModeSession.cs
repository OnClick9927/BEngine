using BEngine.Documents;

namespace BEngine.Editor;

internal sealed class EditorPlayModeSession
{
    private readonly Dictionary<Scene, Scene> _runtimeByEditScene;
    private readonly Dictionary<BObject, BObject> _runtimeByEditObject;
    private readonly Dictionary<BObject, BObject> _editByRuntimeObject;

    private EditorPlayModeSession(
        EditorOpenScene[] editOpenScenes,
        EditorOpenScene[] runtimeOpenScenes,
        Dictionary<Scene, Scene> runtimeByEditScene,
        Scene editScene,
        string editScenePath,
        Scene? editMainScene,
        GameObject? editMainSelection,
        PrefabStage? editPrefabStage,
        GameObject? editSelected,
        BObject? editSelectedAsset,
        string? editSelectedAssetPath,
        bool editDirty,
        bool editMainSceneDirty,
        BObject[] editSelectionObjects,
        BObject? editSelectionContext,
        Undo.HistorySnapshot undoHistory,
        ComponentValueSnapshot? componentClipboard,
        EditorUtility.DirtyState dirtyState,
        GameObject? copiedGameObject,
        EditorRuntimeStateSnapshot runtimeState,
        Dictionary<BObject, BObject> runtimeByEditObject)
    {
        EditOpenScenes = editOpenScenes;
        RuntimeOpenScenes = runtimeOpenScenes;
        _runtimeByEditScene = runtimeByEditScene;
        _runtimeByEditObject = runtimeByEditObject;
        _editByRuntimeObject = new Dictionary<BObject, BObject>(ReferenceEqualityComparer.Instance);
        foreach (var pair in runtimeByEditObject)
            _editByRuntimeObject.Add(pair.Value, pair.Key);
        EditScene = editScene;
        EditScenePath = editScenePath;
        EditMainScene = editMainScene;
        EditMainSelection = editMainSelection;
        EditPrefabStage = editPrefabStage;
        EditSelected = editSelected;
        EditSelectedAsset = editSelectedAsset;
        EditSelectedAssetPath = editSelectedAssetPath;
        EditDirty = editDirty;
        EditMainSceneDirty = editMainSceneDirty;
        EditSelectionObjects = editSelectionObjects;
        EditSelectionContext = editSelectionContext;
        UndoHistory = undoHistory;
        ComponentClipboard = componentClipboard;
        DirtyState = dirtyState;
        CopiedGameObject = copiedGameObject;
        RuntimeObjects = runtimeByEditObject.Values.ToArray();
        RuntimeState = runtimeState;

        RuntimeScene = RuntimeFor(editScene);
        RuntimeMainScene = editMainScene is null ? null : RuntimeFor(editMainScene);
        RuntimeMainSelection = ToRuntime(editMainSelection) as GameObject;
        RuntimeSelected = ToRuntime(editSelected) as GameObject;
        RuntimeSelectedAsset = ToRuntime(editSelectedAsset);
        RuntimeSelectionObjects = editSelectionObjects
            .Select(ToRuntime)
            .Where(item => item is not null)
            .Cast<BObject>()
            .ToArray();
        RuntimeSelectionContext = ToRuntime(editSelectionContext);
        if (editPrefabStage is not null)
        {
            var runtimeRoot = ToRuntime(editPrefabStage.prefabContentsRoot) as GameObject ??
                              throw new InvalidOperationException(
                                  "The Prefab Stage root could not be mapped into the Play Mode scene.");
            RuntimePrefabStage = new PrefabStage(
                editPrefabStage.assetPath, RuntimeFor(editPrefabStage.scene), runtimeRoot);
        }

        foreach (var scene in runtimeByEditScene.Values) RuntimeScenes.Add(scene);
    }

    internal EditorOpenScene[] EditOpenScenes { get; }
    internal EditorOpenScene[] RuntimeOpenScenes { get; }
    internal Scene EditScene { get; }
    internal string EditScenePath { get; }
    internal Scene? EditMainScene { get; }
    internal GameObject? EditMainSelection { get; }
    internal PrefabStage? EditPrefabStage { get; }
    internal GameObject? EditSelected { get; }
    internal BObject? EditSelectedAsset { get; }
    internal string? EditSelectedAssetPath { get; }
    internal bool EditDirty { get; }
    internal bool EditMainSceneDirty { get; }
    internal BObject[] EditSelectionObjects { get; }
    internal BObject? EditSelectionContext { get; }
    internal Undo.HistorySnapshot UndoHistory { get; }
    internal ComponentValueSnapshot? ComponentClipboard { get; }
    internal EditorUtility.DirtyState DirtyState { get; }
    internal GameObject? CopiedGameObject { get; }
    internal EditorRuntimeStateSnapshot RuntimeState { get; }
    private IReadOnlyList<BObject> RuntimeObjects { get; }

    internal Scene RuntimeScene { get; }
    internal Scene? RuntimeMainScene { get; }
    internal GameObject? RuntimeMainSelection { get; }
    internal PrefabStage? RuntimePrefabStage { get; }
    internal GameObject? RuntimeSelected { get; }
    internal BObject? RuntimeSelectedAsset { get; }
    internal BObject[] RuntimeSelectionObjects { get; }
    internal BObject? RuntimeSelectionContext { get; }
    internal HashSet<Scene> RuntimeScenes { get; } = [];

    internal static EditorPlayModeSession Create(
        IServiceProvider services,
        IReadOnlyList<EditorOpenScene> openScenes,
        Scene scene,
        string scenePath,
        Scene? mainScene,
        GameObject? mainSelection,
        PrefabStage? prefabStage,
        GameObject? selected,
        BObject? selectedAsset,
        string? selectedAssetPath,
        BObject? lockedInspectorTarget,
        bool dirty,
        bool mainSceneDirty,
        BObject[] selectionObjects,
        BObject? selectionContext,
        Undo.HistorySnapshot undoHistory,
        ComponentValueSnapshot? componentClipboard,
        EditorUtility.DirtyState dirtyState,
        GameObject? copiedGameObject,
        EditorRuntimeStateSnapshot runtimeState)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(openScenes);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selectionObjects);
        var editEntries = openScenes.ToArray();
        var runtimeByEditScene = new Dictionary<Scene, Scene>();
        Dictionary<BObject, BObject> runtimeByEditObject;
        try
        {
            using (SerializationCallbackUtility.SuppressBeforeSerialize())
            using (SerializationCallbackUtility.SuppressAfterDeserialize())
            {
                foreach (var editScene in editEntries.Select(item => item.Scene)
                             .Append(scene)
                             .Concat(mainScene is null ? [] : [mainScene])
                             .Distinct())
                    runtimeByEditScene.Add(editScene, CloneScene(editScene, services));
                runtimeByEditObject = CopyInMemoryState(
                    runtimeByEditScene,
                    [selectedAsset, selectionContext, lockedInspectorTarget, .. selectionObjects]);
            }
            var runtimeEntries = editEntries.Select(entry =>
                new EditorOpenScene(
                    runtimeByEditScene[entry.Scene],
                    entry.SourcePath,
                    entry.AssetPath,
                    entry.IsLoaded)
                {
                    IsDirty = entry.IsDirty
                }).ToArray();

            return new EditorPlayModeSession(
                editEntries,
                runtimeEntries,
                runtimeByEditScene,
                scene,
                scenePath,
                mainScene,
                mainSelection,
                prefabStage,
                selected,
                selectedAsset,
                selectedAssetPath,
                dirty,
                mainSceneDirty,
                selectionObjects,
                selectionContext,
                undoHistory,
                componentClipboard,
                dirtyState,
                copiedGameObject,
                runtimeState,
                runtimeByEditObject);
        }
        catch
        {
            foreach (var runtimeScene in runtimeByEditScene.Values)
                if (runtimeScene.isCreated) runtimeScene.Dispose();
            throw;
        }
    }

    internal BObject? ToRuntime(BObject? value)
    {
        if (value is null) return null;
        if (_runtimeByEditObject.TryGetValue(value, out var runtimeObject)) return runtimeObject;
        if (value is Scene or GameObject or Component) return null;
        return value;
    }

    internal BObject? ToEdit(BObject? value)
    {
        if (value is null) return null;
        if (_editByRuntimeObject.TryGetValue(value, out var editObject)) return editObject;
        if (value is Scene or GameObject or Component) return null;
        return value;
    }

    internal void InvokeAfterDeserializeCallbacks()
    {
        foreach (var group in RuntimeObjects.GroupBy(GetOwningScene))
        {
            using var context = SceneRuntime.EnterSceneContext(group.Key ?? RuntimeScene);
            foreach (var runtimeObject in group)
                SerializationCallbackUtility.AfterDeserialize(runtimeObject, validateInEditor: false);
        }
    }

    private Scene RuntimeFor(Scene editScene) =>
        _runtimeByEditScene.TryGetValue(editScene, out var runtimeScene)
            ? runtimeScene
            : throw new InvalidOperationException("The editor Scene has no Play Mode mirror.");

    private static Scene CloneScene(Scene source, IServiceProvider services)
    {
        if (!source.isCreated)
            throw new ObjectDisposedException(nameof(source), "An unloaded editor Scene cannot enter Play Mode.");
        var context = new DocumentConversionContext(source.path, services);
        var document = Document.FromBObject<SceneDocument>(source, context);
        var clone = document.ToBObject(context) as Scene ??
                    throw new InvalidDataException("The in-memory Scene snapshot did not produce a Scene.");
        clone.path = source.path;
        clone.isLoaded = source.isLoaded;
        clone.MarkRuntimeOnly();
        return clone;
    }

    private static Dictionary<BObject, BObject> CopyInMemoryState(
        IReadOnlyDictionary<Scene, Scene> runtimeByEditScene,
        IEnumerable<BObject?> additionalRoots)
    {
        var objectMap = new Dictionary<BObject, BObject>(ReferenceEqualityComparer.Instance);
        foreach (var (editScene, runtimeScene) in runtimeByEditScene)
        {
            objectMap.Add(editScene, runtimeScene);
            foreach (var editObject in editScene.gameObjects)
            {
                var runtimeObject = runtimeScene.Find(editObject.Id) ??
                                    throw new InvalidDataException(
                                        $"Play Mode clone is missing GameObject {editObject.Id}.");
                objectMap.Add(editObject, runtimeObject);
                foreach (var editComponent in editObject.components)
                {
                    var runtimeComponent = runtimeObject.components.FirstOrDefault(
                        item => item.Id == editComponent.Id) ??
                                           throw new InvalidDataException(
                                               $"Play Mode clone is missing Component {editComponent.Id}.");
                    objectMap.Add(editComponent, runtimeComponent);
                }
            }
        }

        var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        BObject MapObject(BObject value)
        {
            if (objectMap.TryGetValue(value, out var mapped)) return mapped;
            if (value is Scene or GameObject or Component)
                throw new InvalidDataException(
                    $"Play Mode cannot mirror a reference to scene object '{value.name}' outside the open Scenes.");
            var runtimeObject = CreateRuntimeObject(value);
            objectMap.Add(value, runtimeObject);
            CopyObjectMembers(value, runtimeObject, MapObject, visited);
            return runtimeObject;
        }

        foreach (var (source, destination) in objectMap.ToArray())
        {
            CopyObjectMetadata(source, destination);
            if (source is GameObject editObject && destination is GameObject runtimeObject)
            {
                runtimeObject.SetActive(editObject.activeSelf);
                runtimeObject.tag = editObject.tag;
                runtimeObject.layer = editObject.layer;
                runtimeObject.isStatic = editObject.isStatic;
            }
            if (source is Component editComponent && destination is Component runtimeComponent)
            {
                CopyObjectMembers(editComponent, runtimeComponent, MapObject, visited);
                runtimeComponent.enabled = editComponent.enabled;
            }
        }

        foreach (var root in additionalRoots)
        {
            if (root is not BAsset || objectMap.ContainsKey(root)) continue;
            MapObject(root);
        }

        foreach (var (editScene, runtimeScene) in runtimeByEditScene)
        {
            var editRoots = editScene.rootGameObjects.ToArray();
            for (var index = 0; index < editRoots.Length; index++)
            {
                var runtimeRoot = runtimeScene.Find(editRoots[index].Id) ??
                                  throw new InvalidDataException(
                                      $"Play Mode clone is missing root GameObject {editRoots[index].Id}.");
                runtimeRoot.transform.SetSiblingIndex(index);
                RestoreChildOrder(editRoots[index].transform, runtimeRoot.transform);
            }
        }
        return objectMap;
    }

    private static BObject CreateRuntimeObject(BObject source)
    {
        if (source is Material material) return new Material(material);
        if (source is Shader shader) return new Shader(shader.shaderName);
        if (source is BEngine.TextAsset textAsset)
            return new BEngine.TextAsset(textAsset.text, textAsset.path);
        if (source is PrefabAsset prefab)
        {
            var document = Document.FromYaml<PrefabDocument>(prefab.Document.ToYaml());
            return new PrefabAsset(document, prefab.assetPath);
        }
        if (source is ScriptableObject scriptable)
            return ScriptableObject.CreateInstance(scriptable.GetType());
        if (RuntimeTypeCache.GetFactory(source.GetType())?.Invoke() is BObject runtimeObject)
            return runtimeObject;
        throw new NotSupportedException(
            $"Play Mode cannot isolate referenced object type '{source.GetType().FullName}'.");
    }

    private static void CopyObjectMetadata(BObject source, BObject destination)
    {
        destination.IsRuntimeOnly = true;
        destination.name = source.name;
        destination.hideFlags = source.hideFlags;
        destination.PrefabAssetId = source.PrefabAssetId;
        destination.PrefabSourceId = source.PrefabSourceId;
    }

    private static void CopyObjectMembers(
        BObject source,
        BObject destination,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        CopyObjectMetadata(source, destination);
        foreach (var member in ObjectState.GetSerializableMembers(source))
        {
            var accessor = RuntimeTypeCache.GetMemberAccessor(member);
            if (accessor.Setter is null) continue;
            var value = EditorValueCloner.CloneValue(accessor.Getter(source), objectMapper, visited);
            accessor.Setter(destination, value);
        }
    }

    private static void RestoreChildOrder(Transform editParent, Transform runtimeParent)
    {
        var editChildren = editParent.children.ToArray();
        for (var index = 0; index < editChildren.Length; index++)
        {
            var runtimeChild = runtimeParent.children.FirstOrDefault(
                item => item.gameObject.Id == editChildren[index].gameObject.Id) ??
                               throw new InvalidDataException(
                                   $"Play Mode clone is missing child GameObject {editChildren[index].gameObject.Id}.");
            runtimeChild.SetSiblingIndex(index);
            RestoreChildOrder(editChildren[index], runtimeChild);
        }
    }

    private static Scene? GetOwningScene(BObject value) => value switch
    {
        Scene scene => scene,
        GameObject gameObject => gameObject.scene,
        Component component => component.gameObject.scene,
        _ => null
    };
}
