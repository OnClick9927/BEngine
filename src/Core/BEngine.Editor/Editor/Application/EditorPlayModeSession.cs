using BEngine.Serialization;

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
    internal EditorOpenScene[] RuntimeOpenScenes { get; private set; }
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
    private IReadOnlyList<BObject> RuntimeObjects { get; set; }

    internal Scene RuntimeScene { get; private set; }
    internal Scene? RuntimeMainScene { get; private set; }
    internal GameObject? RuntimeMainSelection { get; private set; }
    internal PrefabStage? RuntimePrefabStage { get; private set; }
    internal GameObject? RuntimeSelected { get; private set; }
    internal BObject? RuntimeSelectedAsset { get; private set; }
    internal BObject[] RuntimeSelectionObjects { get; private set; }
    internal BObject? RuntimeSelectionContext { get; private set; }
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
        IReadOnlyList<BObject> lockedInspectorTargets,
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
        ArgumentNullException.ThrowIfNull(lockedInspectorTargets);
        ArgumentNullException.ThrowIfNull(selectionObjects);
        var editEntries = openScenes.ToArray();
        var runtimeByEditScene = new Dictionary<Scene, Scene>();
        Dictionary<BObject, BObject>? runtimeByEditObject = null;
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
                    [selectedAsset, selectionContext, .. lockedInspectorTargets, .. selectionObjects]);
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
            ReleaseNonSceneObjects(runtimeByEditObject?.Values ?? Enumerable.Empty<BObject>());
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

    internal EditorPlayModeReload PrepareRuntimeReload(
        IServiceProvider services,
        IReadOnlyList<EditorOpenScene> openScenes,
        Scene activeScene,
        Scene? mainScene,
        GameObject? mainSelection,
        PrefabStage? prefabStage,
        GameObject? selected,
        BObject? selectedAsset,
        IReadOnlyList<BObject> selectionObjects,
        BObject? selectionContext,
        IReadOnlyList<BObject> lockedInspectorTargets)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(openScenes);
        ArgumentNullException.ThrowIfNull(activeScene);
        ArgumentNullException.ThrowIfNull(selectionObjects);
        ArgumentNullException.ThrowIfNull(lockedInspectorTargets);

        var sourceEntries = openScenes.ToArray();
        var sourceScenes = sourceEntries.Select(entry => entry.Scene)
            .Append(activeScene)
            .Concat(mainScene is null ? [] : [mainScene])
            .Distinct<Scene>(ReferenceEqualityComparer.Instance)
            .ToArray();
        var reloadedBySourceScene = new Dictionary<Scene, Scene>(ReferenceEqualityComparer.Instance);
        Dictionary<BObject, BObject>? reloadedBySourceObject = null;
        try
        {
            using (SerializationCallbackUtility.SuppressBeforeSerialize())
            using (SerializationCallbackUtility.SuppressAfterDeserialize())
            {
                foreach (var sourceScene in sourceScenes)
                    reloadedBySourceScene.Add(sourceScene, CloneScene(sourceScene, services));
                reloadedBySourceObject = CopyInMemoryState(
                    reloadedBySourceScene,
                    _runtimeByEditObject.Values.Cast<BObject?>()
                        .Concat([selectedAsset, selectionContext])
                        .Concat(selectionObjects)
                        .Concat(lockedInspectorTargets));
            }

            var reloadedEntries = sourceEntries.Select(entry => new EditorOpenScene(
                reloadedBySourceScene[entry.Scene],
                entry.SourcePath,
                entry.AssetPath,
                entry.IsLoaded)
            {
                IsDirty = entry.IsDirty
            }).ToArray();
            return new EditorPlayModeReload(
                sourceScenes,
                reloadedEntries,
                reloadedBySourceObject,
                MapRequired<Scene>(activeScene, reloadedBySourceObject),
                MapOptional<Scene>(mainScene, reloadedBySourceObject),
                MapOptional<GameObject>(mainSelection, reloadedBySourceObject),
                ReloadPrefabStage(prefabStage, reloadedBySourceObject),
                MapOptional<GameObject>(selected, reloadedBySourceObject),
                MapOptional(selectedAsset, reloadedBySourceObject),
                selectionObjects.Select(item => MapOptional(item, reloadedBySourceObject))
                    .Where(item => item is not null).Cast<BObject>().ToArray(),
                MapOptional(selectionContext, reloadedBySourceObject));
        }
        catch
        {
            ReleaseNonSceneObjects(reloadedBySourceObject?.Values ?? Enumerable.Empty<BObject>());
            foreach (var scene in reloadedBySourceScene.Values)
                if (scene.isCreated) scene.Dispose();
            throw;
        }
    }

    internal void CommitRuntimeReload(EditorPlayModeReload reload)
    {
        ArgumentNullException.ThrowIfNull(reload);
        foreach (var editScene in _runtimeByEditScene.Keys.ToArray())
        {
            var current = _runtimeByEditScene[editScene];
            // A Single load may already have unloaded this edit scene's runtime mirror.
            // Only scenes participating in the reload have replacement objects.
            if (!current.isCreated && !reload.ObjectMap.ContainsKey(current)) continue;
            _runtimeByEditScene[editScene] = MapRequired<Scene>(current, reload.ObjectMap);
        }
        foreach (var editObject in _runtimeByEditObject.Keys.ToArray())
        {
            var current = _runtimeByEditObject[editObject];
            if (reload.ObjectMap.TryGetValue(current, out var replacement))
                _runtimeByEditObject[editObject] = replacement;
        }
        _editByRuntimeObject.Clear();
        foreach (var pair in _runtimeByEditObject)
            _editByRuntimeObject[pair.Value] = pair.Key;

        RuntimeOpenScenes = reload.OpenScenes;
        RuntimeScene = reload.ActiveScene;
        RuntimeMainScene = reload.MainScene;
        RuntimeMainSelection = reload.MainSelection;
        RuntimePrefabStage = reload.PrefabStage;
        RuntimeSelected = reload.Selected;
        RuntimeSelectedAsset = reload.SelectedAsset;
        RuntimeSelectionObjects = reload.SelectionObjects;
        RuntimeSelectionContext = reload.SelectionContext;
        RuntimeObjects = reload.ObjectMap.Values
            .Distinct<BObject>(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var scene in reload.OpenScenes.Select(entry => entry.Scene)) RuntimeScenes.Add(scene);
        reload.MarkCommitted();
    }

    internal void CompleteRuntimeReload(EditorPlayModeReload reload)
    {
        ArgumentNullException.ThrowIfNull(reload);
        foreach (var sourceScene in reload.SourceScenes) RuntimeScenes.Remove(sourceScene);
        ReleaseNonSceneObjects(reload.ObjectMap.Keys);
    }

    internal void ReleaseRuntimeAssets() => ReleaseNonSceneObjects(RuntimeObjects);

    internal static void ReleaseNonSceneObjects(IEnumerable<BObject> objects)
    {
        foreach (var value in objects.Distinct<BObject>(ReferenceEqualityComparer.Instance).ToArray())
        {
            if (value is Scene or GameObject or Component || value.isDestroyed) continue;
            EditorFeatureGuard.Invoke(value, "Release Play Mode Asset", () => BObject.Destroy(value));
        }
    }

    internal void RollbackRuntimeReload(EditorPlayModeReload reload)
    {
        ArgumentNullException.ThrowIfNull(reload);
        foreach (var editScene in _runtimeByEditScene.Keys.ToArray())
        {
            var current = _runtimeByEditScene[editScene];
            if (!current.isCreated && reload.ToSource(current) is null) continue;
            _runtimeByEditScene[editScene] = MapRequired<Scene>(
                current, reload.ToSource, "source Play Mode scene");
        }
        foreach (var editObject in _runtimeByEditObject.Keys.ToArray())
        {
            var current = _runtimeByEditObject[editObject];
            if (reload.ToSource(current) is { } source)
                _runtimeByEditObject[editObject] = source;
        }
        _editByRuntimeObject.Clear();
        foreach (var pair in _runtimeByEditObject)
            _editByRuntimeObject[pair.Value] = pair.Key;

        RuntimeOpenScenes = reload.OpenScenes.Select(entry => new EditorOpenScene(
            MapRequired<Scene>(entry.Scene, reload.ToSource, "source Play Mode scene"),
            entry.SourcePath,
            entry.AssetPath,
            entry.IsLoaded)
        {
            IsDirty = entry.IsDirty
        }).ToArray();
        RuntimeScene = MapRequired<Scene>(
            reload.ActiveScene, reload.ToSource, "source active Play Mode scene");
        RuntimeMainScene = MapOptional<Scene>(reload.MainScene, reload.ToSource);
        RuntimeMainSelection = MapOptional<GameObject>(reload.MainSelection, reload.ToSource);
        RuntimePrefabStage = ReloadPrefabStage(reload.PrefabStage, reload.ToSource);
        RuntimeSelected = MapOptional<GameObject>(reload.Selected, reload.ToSource);
        RuntimeSelectedAsset = reload.ToSource(reload.SelectedAsset);
        RuntimeSelectionObjects = reload.SelectionObjects.Select(reload.ToSource)
            .Where(item => item is not null).Cast<BObject>().ToArray();
        RuntimeSelectionContext = reload.ToSource(reload.SelectionContext);
        RuntimeObjects = reload.ObjectMap.Keys
            .Distinct<BObject>(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var scene in reload.ObjectMap.Values.OfType<Scene>()) RuntimeScenes.Remove(scene);
        reload.MarkRolledBack();
    }

    private static PrefabStage? ReloadPrefabStage(
        PrefabStage? stage,
        IReadOnlyDictionary<BObject, BObject> objectMap)
    {
        if (stage is null) return null;
        return new PrefabStage(
            stage.assetPath,
            MapRequired<Scene>(stage.scene, objectMap),
            MapRequired<GameObject>(stage.prefabContentsRoot, objectMap));
    }

    private static PrefabStage? ReloadPrefabStage(
        PrefabStage? stage,
        Func<BObject?, BObject?> map)
    {
        if (stage is null) return null;
        return new PrefabStage(
            stage.assetPath,
            MapRequired<Scene>(stage.scene, map, "source Prefab Stage scene"),
            MapRequired<GameObject>(stage.prefabContentsRoot, map, "source Prefab Stage root"));
    }

    private static T? MapOptional<T>(T? value, Func<BObject?, BObject?> map)
        where T : BObject => value is null ? null : map(value) as T;

    private static T MapRequired<T>(
        T value,
        Func<BObject?, BObject?> map,
        string description)
        where T : BObject => map(value) as T ?? throw new InvalidDataException(
        $"Play Mode hot reload could not restore the {description} '{value.name}'.");

    private static T? MapOptional<T>(T? value, IReadOnlyDictionary<BObject, BObject> objectMap)
        where T : BObject => value is null ? null : MapRequired<T>(value, objectMap);

    private static BObject? MapOptional(
        BObject? value,
        IReadOnlyDictionary<BObject, BObject> objectMap) =>
        value is null ? null : objectMap.GetValueOrDefault(value, value);

    private static T MapRequired<T>(T value, IReadOnlyDictionary<BObject, BObject> objectMap)
        where T : BObject => objectMap.TryGetValue(value, out var mapped) && mapped is T result
            ? result
            : throw new InvalidDataException(
                $"Play Mode hot reload could not map {typeof(T).Name} '{value.name}'.");

    private Scene RuntimeFor(Scene editScene) =>
        _runtimeByEditScene.TryGetValue(editScene, out var runtimeScene)
            ? runtimeScene
            : throw new InvalidOperationException("The editor Scene has no Play Mode mirror.");

    private static Scene CloneScene(Scene source, IServiceProvider services)
    {
        if (!source.isCreated)
            throw new ObjectDisposedException(nameof(source), "An unloaded editor Scene cannot enter Play Mode.");
        var clone = SceneAssetSerialization.Clone(source, services);
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

        try
        {
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
                // Selecting an unopened .scene asset in Project is not a reference
                // from the running scene graph. It must not prevent entering Play.
                if (root is not BAsset || root is Scene || objectMap.ContainsKey(root)) continue;
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
        catch
        {
            ReleaseNonSceneObjects(objectMap.Values);
            throw;
        }
    }

    private static BObject CreateRuntimeObject(BObject source)
    {
        if (source is Material material) return new Material(material);
        if (source is Shader shader) return new Shader(shader.shaderName, shader.sourceCode);
        if (source is BEngine.TextAsset textAsset)
            return new BEngine.TextAsset(textAsset.text, textAsset.path);
        if (source is PrefabAsset prefab)
        {
            return PrefabAssetSerialization.Deserialize(PrefabAssetSerialization.Serialize(prefab),
                prefab.assetPath);
        }
        var sourceType = source.GetType();
        var runtimeType = RuntimeTypeCache.ResolveSerializedType(
            sourceType.AssemblyQualifiedName ?? sourceType.FullName ?? string.Empty) ?? sourceType;
        if (!typeof(BObject).IsAssignableFrom(runtimeType)) runtimeType = sourceType;
        if (source is ScriptableObject)
            return ScriptableObject.CreateInstance(runtimeType);
        if (RuntimeTypeCache.GetFactory(runtimeType)?.Invoke() is BObject runtimeObject)
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
        if (source is BAsset sourceAsset && destination is BAsset destinationAsset)
            destinationAsset.CopyAssetReferenceFrom(sourceAsset);
    }

    private static void CopyObjectMembers(
        BObject source,
        BObject destination,
        Func<BObject, BObject> objectMapper,
        Dictionary<object, object> visited)
    {
        CopyObjectMetadata(source, destination);
        if (source is BAsset sourceAsset && destination is BAsset destinationAsset)
            destinationAsset.CopyClonePayloadFrom(sourceAsset, objectMapper);
        var destinationMembers = ObjectState.GetSerializableMembers(destination)
            .GroupBy(member => member.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var member in ObjectState.GetSerializableMembers(source))
        {
            if (!destinationMembers.TryGetValue(member.Name, out var destinationMember)) continue;
            var sourceAccessor = RuntimeTypeCache.GetMemberAccessor(member);
            var destinationAccessor = RuntimeTypeCache.GetMemberAccessor(destinationMember);
            if (destinationAccessor.Setter is null) continue;
            try
            {
                var value = EditorValueCloner.CloneValue(
                    sourceAccessor.Getter(source), destinationAccessor.ValueType, objectMapper, visited);
                destinationAccessor.Setter(destination, value);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
            {
                throw new InvalidDataException(
                    $"Play Mode could not copy member '{member.Name}' from " +
                    $"{EditorValueCloner.DescribeType(source.GetType())} to " +
                    $"{EditorValueCloner.DescribeType(destination.GetType())}. {exception.Message}", exception);
            }
        }
        if (source is Sprite sourceSprite && destination is Sprite destinationSprite)
            destinationSprite.CopyClonePayloadFrom(sourceSprite, objectMapper);
        if (destination is BAsset asset) asset.MarkOwnedAssetsRuntimeOnly();
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
