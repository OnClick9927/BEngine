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
            runtimeByEditObject = EditorSceneIsolation.CloneScenes(
                editEntries.Select(item => item.Scene).Append(scene)
                    .Concat(mainScene is null ? [] : [mainScene]), services,
                [selectedAsset, selectionContext, .. lockedInspectorTargets, .. selectionObjects]);
            foreach (var pair in runtimeByEditObject)
                if (pair.Key is Scene editScene && pair.Value is Scene runtimeScene)
                    runtimeByEditScene.Add(editScene, runtimeScene);
            var projectionSource7146 = editEntries;
            ArgumentNullException.ThrowIfNull(projectionSource7146, "source");
            var projectionResult7146 = projectionSource7146.Length == 0 ? global::System.Array.Empty<global::BEngine.Editor.EditorOpenScene>() : new global::BEngine.Editor.EditorOpenScene[projectionSource7146.Length];
            for (var projectionIndex7146 = 0; projectionIndex7146 < projectionSource7146.Length; projectionIndex7146++)
            {
                var projectionItem7146 = projectionSource7146[projectionIndex7146];
                projectionResult7146[projectionIndex7146] = new EditorOpenScene(runtimeByEditScene[projectionItem7146.Scene], projectionItem7146.SourcePath, projectionItem7146.AssetPath, projectionItem7146.IsLoaded)
                {
                    IsDirty = projectionItem7146.IsDirty
                };
            }
            
            var runtimeEntries = projectionResult7146;

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

    internal void InvokeAfterDeserializeCallbacks() =>
        EditorSceneIsolation.InvokeAfterDeserialize(RuntimeObjects, RuntimeScene);

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
        var sourceScenes = ((((global::System.Collections.Generic.IEnumerable<global::BEngine.Scene>)(sourceEntries.Select(entry => entry.Scene).Append(activeScene).Concat(mainScene is null ? [] : [mainScene]))), (global::System.Collections.Generic.IEqualityComparer<global::BEngine.Scene>? )(ReferenceEqualityComparer.Instance)) switch
        {
            (global::BEngine.Scene[] { Length: 0 }, _) => global::System.Array.Empty<global::BEngine.Scene>(),
            var (distinctItems10873, distinctComparer10873) => distinctItems10873.ToHashSet<global::BEngine.Scene>(distinctComparer10873).ToArray()});
        var reloadedBySourceScene = new Dictionary<Scene, Scene>(ReferenceEqualityComparer.Instance);
        Dictionary<BObject, BObject>? reloadedBySourceObject = null;
        try
        {
            reloadedBySourceObject = EditorSceneIsolation.CloneScenes(sourceScenes, services,
                _runtimeByEditObject.Values.Cast<BObject?>()
                    .Concat([selectedAsset, selectionContext]).Concat(selectionObjects).Concat(lockedInspectorTargets));
            foreach (var pair in reloadedBySourceObject)
                if (pair.Key is Scene sourceScene && pair.Value is Scene reloadedScene)
                    reloadedBySourceScene.Add(sourceScene, reloadedScene);

            var projectionSource11394 = sourceEntries;
            ArgumentNullException.ThrowIfNull(projectionSource11394, "source");
            var projectionResult11394 = projectionSource11394.Length == 0 ? global::System.Array.Empty<global::BEngine.Editor.EditorOpenScene>() : new global::BEngine.Editor.EditorOpenScene[projectionSource11394.Length];
            for (var projectionIndex11394 = 0; projectionIndex11394 < projectionSource11394.Length; projectionIndex11394++)
            {
                var projectionItem11394 = projectionSource11394[projectionIndex11394];
                projectionResult11394[projectionIndex11394] = new EditorOpenScene(reloadedBySourceScene[projectionItem11394.Scene], projectionItem11394.SourcePath, projectionItem11394.AssetPath, projectionItem11394.IsLoaded)
                {
                    IsDirty = projectionItem11394.IsDirty
                };
            }
            
            var reloadedEntries = projectionResult11394;
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
        RuntimeObjects = ((((global::System.Collections.Generic.IEnumerable<global::BEngine.BObject>)(reload.ObjectMap.Values)), (global::System.Collections.Generic.IEqualityComparer<global::BEngine.BObject>? )(ReferenceEqualityComparer.Instance)) switch
        {
            (global::BEngine.BObject[] { Length: 0 }, _) => global::System.Array.Empty<global::BEngine.BObject>(),
            var (distinctItems15604, distinctComparer15604) => distinctItems15604.ToHashSet<global::BEngine.BObject>(distinctComparer15604).ToArray()});
        {
            var __projectionSource14440 = reload.OpenScenes;
            ArgumentNullException.ThrowIfNull(__projectionSource14440, "source");
            foreach (var __projectionItem14440 in __projectionSource14440)
            {
                var scene = __projectionItem14440.Scene;
                RuntimeScenes.Add(scene);
            }
        }
        reload.MarkCommitted();
    }

    internal void CompleteRuntimeReload(EditorPlayModeReload reload)
    {
        ArgumentNullException.ThrowIfNull(reload);
        foreach (var sourceScene in reload.SourceScenes) RuntimeScenes.Remove(sourceScene);
        ReleaseNonSceneObjects(reload.ObjectMap.Keys);
    }

    internal void ReleaseRuntimeAssets() => ReleaseNonSceneObjects(RuntimeObjects);

    internal static void ReleaseNonSceneObjects(IEnumerable<BObject> objects) =>
        EditorSceneIsolation.ReleaseAssets(objects);

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

        RuntimeOpenScenes = (reload.OpenScenes is { } projectionSource17814 ? projectionSource17814.Length == 0 ? global::System.Array.Empty<global::BEngine.Editor.EditorOpenScene>() : global::System.Array.ConvertAll<global::BEngine.Editor.EditorOpenScene, global::BEngine.Editor.EditorOpenScene>(projectionSource17814, entry => new EditorOpenScene(
            MapRequired<Scene>(entry.Scene, reload.ToSource, "source Play Mode scene"),
            entry.SourcePath,
            entry.AssetPath,
            entry.IsLoaded)
        {
            IsDirty = entry.IsDirty
        }) : throw new global::System.ArgumentNullException("source"));
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
        RuntimeObjects = ((((global::System.Collections.Generic.IEnumerable<global::BEngine.BObject>)(reload.ObjectMap.Keys)), (global::System.Collections.Generic.IEqualityComparer<global::BEngine.BObject>? )(ReferenceEqualityComparer.Instance)) switch
        {
            (global::BEngine.BObject[] { Length: 0 }, _) => global::System.Array.Empty<global::BEngine.BObject>(),
            var (distinctItems19227, distinctComparer19227) => distinctItems19227.ToHashSet<global::BEngine.BObject>(distinctComparer19227).ToArray()});
        {
            var __typedSource17483 = reload.ObjectMap.Values;
            ArgumentNullException.ThrowIfNull(__typedSource17483, "source");
            foreach (var __typedItem17483 in __typedSource17483)
            {
                if (__typedItem17483 is not Scene scene)
                    continue;
                RuntimeScenes.Remove(scene);
            }
        }
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

}
