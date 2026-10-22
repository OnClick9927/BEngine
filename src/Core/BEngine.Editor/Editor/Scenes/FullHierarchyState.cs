namespace BEngine.Editor;

internal sealed class FullHierarchyState : IUndoState
{
    private readonly GameObject _root;
    private readonly Dictionary<GameObject, GameObjectSnapshot> _objects;
    private readonly HashSet<GameObject> _valueScope;

    internal BObject[] Targets { get; }

    private FullHierarchyState(
        GameObject root,
        IReadOnlyDictionary<GameObject, GameObjectSnapshot>? previousSnapshots,
        IEnumerable<GameObject>? previousValueScope)
    {
        _root = root;
        var currentHierarchy = Enumerate(root).ToHashSet();
        _valueScope = new HashSet<GameObject>(previousValueScope ?? []);
        _valueScope.UnionWith(currentHierarchy);

        var tracked = new HashSet<GameObject>(previousSnapshots?.Keys ?? []);
        tracked.UnionWith(currentHierarchy);

        _objects = [];
        foreach (var gameObject in tracked)
        {
            GameObjectSnapshot? previous = null;
            var hadPreviousSnapshot = previousSnapshots?.TryGetValue(gameObject, out previous) == true;
            var exists = gameObject.scene is not null ||
                         hadPreviousSnapshot && previous!.Exists && previous.Scene is null ||
                         !hadPreviousSnapshot && currentHierarchy.Contains(gameObject);
            _objects.Add(gameObject,
                CaptureObject(gameObject, _valueScope.Contains(gameObject), exists));
        }

        Targets = _valueScope
            .SelectMany(static item => item.components.Cast<BObject>().Prepend(item))
            .DistinctBy(static item => item.GetInstanceID())
            .ToArray();
    }

    internal static FullHierarchyState Capture(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new FullHierarchyState(root, null, null);
    }

    public IUndoState CaptureInverse()
    {
        var inverse = new FullHierarchyState(_root, _objects, _valueScope);
        foreach (var (gameObject, snapshot) in inverse._objects)
        {
            if (!snapshot.Exists || _objects.ContainsKey(gameObject)) continue;
            _objects.Add(gameObject, GameObjectSnapshot.Absent);
        }
        return inverse;
    }

    public void Restore()
    {
        var desired = _objects.Where(static pair => pair.Value.Exists)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
        RemoveObjectsAbsentFromSnapshot(desired);
        RestoreSceneMembership(desired);
        RestoreParentsAndSiblingOrder(desired);

        foreach (var (gameObject, snapshot) in desired)
        {
            if (!snapshot.CaptureValues) continue;
            RestoreComponents(gameObject, snapshot.Components);
        }

        foreach (var (gameObject, snapshot) in desired)
        {
            if (!snapshot.CaptureValues) continue;
            snapshot.GameObjectState?.Restore();
            foreach (var component in snapshot.Components)
                if (snapshot.ComponentStates.TryGetValue(component, out var state)) state.Restore();
        }

        EditorApplication.RaiseHierarchyChanged();
    }

    private void RemoveObjectsAbsentFromSnapshot(
        IReadOnlyDictionary<GameObject, GameObjectSnapshot> desired)
    {
        var removals = new HashSet<GameObject>();
        foreach (var current in Enumerate(_root))
            if (!desired.ContainsKey(current)) removals.Add(current);
        foreach (var (gameObject, snapshot) in _objects)
            if (!snapshot.Exists && (gameObject.scene is not null || gameObject.transform.parent is not null))
                removals.Add(gameObject);
        if (removals.Count == 0) return;

        foreach (var gameObject in removals)
        foreach (var child in gameObject.transform.children.ToArray())
            if (!removals.Contains(child.gameObject)) child.SetParent(null, false);

        foreach (var gameObject in removals
                     .Where(item => item.transform.parent is null ||
                                    !removals.Contains(item.transform.parent.gameObject))
                     .ToArray())
        {
            if (gameObject.scene is { } scene) scene.Destroy(gameObject);
            else if (gameObject.transform.parent is not null) gameObject.transform.SetParent(null, false);
        }
    }

    private static void RestoreSceneMembership(
        IReadOnlyDictionary<GameObject, GameObjectSnapshot> desired)
    {
        foreach (var (gameObject, snapshot) in desired
                     .OrderBy(pair => SnapshotDepth(pair.Value, desired)))
        {
            if (snapshot.Scene is not { } scene)
            {
                if (gameObject.scene is { } detachedFromScene) detachedFromScene.Destroy(gameObject);
                continue;
            }
            if (ReferenceEquals(gameObject.scene, scene)) continue;
            if (gameObject.transform.parent is not null) gameObject.transform.SetParent(null, false);
            if (gameObject.scene is { } sourceScene)
            {
                if (!ReferenceEquals(sourceScene, scene)) Scene.MoveGameObjectToScene(gameObject, scene);
            }
            else
                scene.Add(gameObject);
        }
    }

    private static void RestoreParentsAndSiblingOrder(
        IReadOnlyDictionary<GameObject, GameObjectSnapshot> desired)
    {
        var ordered = desired.OrderBy(pair => SnapshotDepth(pair.Value, desired)).ToArray();
        foreach (var (gameObject, snapshot) in ordered)
            if (!ReferenceEquals(gameObject.transform.parent, snapshot.Parent))
                gameObject.transform.SetParent(snapshot.Parent, false);

        foreach (var group in ordered.GroupBy(static pair => pair.Value.Parent))
        foreach (var (gameObject, snapshot) in group.OrderBy(static pair => pair.Value.SiblingIndex))
            gameObject.transform.SetSiblingIndex(snapshot.SiblingIndex);
    }

    private static int SnapshotDepth(
        GameObjectSnapshot snapshot,
        IReadOnlyDictionary<GameObject, GameObjectSnapshot> desired)
    {
        var depth = 0;
        var parent = snapshot.Parent;
        var visited = new HashSet<Transform>();
        while (parent is not null && visited.Add(parent))
        {
            depth++;
            parent = desired.TryGetValue(parent.gameObject, out var parentSnapshot)
                ? parentSnapshot.Parent
                : parent.parent;
        }
        return depth;
    }

    private static void RestoreComponents(GameObject gameObject, Component[] desired)
    {
        var current = gameObject.components.ToArray();
        if (current.SequenceEqual(desired)) return;

        var pending = current.Where(static component => component is not Transform).ToList();
        while (pending.Count > 0)
        {
            var removedAny = false;
            for (var index = pending.Count - 1; index >= 0; index--)
            {
                var component = pending[index];
                if (!gameObject.CanRemoveComponent(component)) continue;
                if (!gameObject.RemoveComponent(component)) continue;
                pending.RemoveAt(index);
                removedAny = true;
            }
            if (!removedAny)
                throw new InvalidOperationException(
                    $"Could not restore the component order on '{gameObject.name}'.");
        }

        foreach (var component in desired)
        {
            if (component is Transform) continue;
            if (!gameObject.RestoreComponent(component, gameObject.components.Count))
                throw new InvalidOperationException(
                    $"Could not restore component '{component.GetType().Name}' on '{gameObject.name}'.");
        }
    }

    private static GameObjectSnapshot CaptureObject(
        GameObject gameObject,
        bool captureValues,
        bool exists)
    {
        if (!exists) return GameObjectSnapshot.Absent;

        var components = captureValues ? gameObject.components.ToArray() : [];
        var componentStates = new Dictionary<Component, ObjectState>();
        foreach (var component in components) componentStates.Add(component, ObjectState.Capture(component));
        return new GameObjectSnapshot(
            true,
            gameObject.scene,
            gameObject.transform.parent,
            gameObject.transform.GetSiblingIndex(),
            captureValues,
            captureValues ? ObjectState.Capture(gameObject) : null,
            components,
            componentStates);
    }

    private static IEnumerable<GameObject> Enumerate(GameObject root)
    {
        yield return root;
        foreach (var child in root.transform.children)
        foreach (var descendant in Enumerate(child.gameObject))
            yield return descendant;
    }

    private sealed record GameObjectSnapshot(
        bool Exists,
        Scene? Scene,
        Transform? Parent,
        int SiblingIndex,
        bool CaptureValues,
        ObjectState? GameObjectState,
        Component[] Components,
        Dictionary<Component, ObjectState> ComponentStates)
    {
        internal static readonly GameObjectSnapshot Absent = new(
            false, null, null, -1, false, null, [],
            new Dictionary<Component, ObjectState>());
    }
}
