namespace BEngine.Editor;

internal sealed class EditorPlayModeReload : IDisposable
{
    private int _committed;
    private int _disposed;
    private readonly Dictionary<BObject, BObject> _sourceByReloadedObject;

    internal EditorPlayModeReload(
        Scene[] sourceScenes,
        EditorOpenScene[] openScenes,
        Dictionary<BObject, BObject> objectMap,
        Scene activeScene,
        Scene? mainScene,
        GameObject? mainSelection,
        PrefabStage? prefabStage,
        GameObject? selected,
        BObject? selectedAsset,
        BObject[] selectionObjects,
        BObject? selectionContext)
    {
        SourceScenes = sourceScenes;
        OpenScenes = openScenes;
        ObjectMap = objectMap;
        _sourceByReloadedObject = new Dictionary<BObject, BObject>(
            ReferenceEqualityComparer.Instance);
        foreach (var pair in objectMap)
            _sourceByReloadedObject.Add(pair.Value, pair.Key);
        ActiveScene = activeScene;
        MainScene = mainScene;
        MainSelection = mainSelection;
        PrefabStage = prefabStage;
        Selected = selected;
        SelectedAsset = selectedAsset;
        SelectionObjects = selectionObjects;
        SelectionContext = selectionContext;
    }

    internal Scene[] SourceScenes { get; }
    internal EditorOpenScene[] OpenScenes { get; }
    internal Dictionary<BObject, BObject> ObjectMap { get; }
    internal Scene ActiveScene { get; }
    internal Scene? MainScene { get; }
    internal GameObject? MainSelection { get; }
    internal PrefabStage? PrefabStage { get; }
    internal GameObject? Selected { get; }
    internal BObject? SelectedAsset { get; }
    internal BObject[] SelectionObjects { get; }
    internal BObject? SelectionContext { get; }

    internal BObject? ToReloaded(BObject? value)
    {
        if (value is null) return null;
        if (ObjectMap.TryGetValue(value, out var mapped)) return mapped;
        return value is Scene or GameObject or Component ? null : value;
    }

    internal BObject? ToSource(BObject? value)
    {
        if (value is null) return null;
        if (_sourceByReloadedObject.TryGetValue(value, out var mapped)) return mapped;
        return value is Scene or GameObject or Component ? null : value;
    }

    internal void MarkCommitted() => _committed = 1;

    internal void MarkRolledBack() => _committed = 0;

    public void Dispose()
    {
        if (Volatile.Read(ref _committed) != 0 || Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            foreach (var scene in ObjectMap.Values.OfType<Scene>()
                         .Distinct<Scene>(ReferenceEqualityComparer.Instance))
                if (scene.isCreated) EditorFeatureGuard.Invoke(scene, "Dispose failed Play Mode reload Scene", scene.Dispose);
        }
        finally { EditorPlayModeSession.ReleaseNonSceneObjects(ObjectMap.Values); }
    }
}
