using System.Reflection;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class Undo
{
    private static readonly Stack<UndoOperation> UndoStack = [];
    private static readonly Stack<UndoOperation> RedoStack = [];
    private static int _group;
    private static string _groupName = "Edit";

    public static event Action? undoRedoPerformed;
    public static event Action<UndoRedoInfo>? undoRedoEvent;
    public static bool canUndo => UndoStack.Count > 0;
    public static bool canRedo => RedoStack.Count > 0;

    public static void RecordObject(BObject objectToUndo, string name) =>
        RecordObjects([objectToUndo], name);

    public static void RecordObjects(BObject[] objectsToUndo, string name)
    {
        ArgumentNullException.ThrowIfNull(objectsToUndo);
        var states = objectsToUndo.Where(item => item is not null)
            .DistinctBy(item => item.Id)
            .Select(ObjectState.Capture)
            .ToArray();
        if (states.Length == 0) return;
        _groupName = string.IsNullOrWhiteSpace(name) ? "Edit" : name;
        UndoStack.Push(new UndoOperation(_group, _groupName, states, null, null));
        RedoStack.Clear();
    }

    public static void RegisterCompleteObjectUndo(BObject objectToUndo, string name) =>
        RecordObject(objectToUndo, name);

    public static void RegisterCompleteObjectUndo(BObject[] objectsToUndo, string name) =>
        RecordObjects(objectsToUndo, name);

    public static void RegisterFullObjectHierarchyUndo(BObject objectToUndo, string name)
    {
        if (objectToUndo is GameObject gameObject)
        {
            RecordObjects(gameObject.GetComponentsInChildren<Component>(includeInactive: true)
                .Cast<BObject>().Prepend(gameObject).ToArray(), name);
            return;
        }
        RecordObject(objectToUndo, name);
    }

    public static void SetTransformParent(Transform transform, Transform? newParent, string name)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var oldParent = transform.parent;
        var oldSiblingIndex = transform.GetSiblingIndex();
        transform.SetParent(newParent);
        var newSiblingIndex = transform.GetSiblingIndex();
        RegisterOperation(name,
            () => RestoreParent(transform, oldParent, oldSiblingIndex),
            () => RestoreParent(transform, newParent, newSiblingIndex));
        EditorUtility.SetDirty(transform);
        EditorApplication.RaiseHierarchyChanged();
    }

    public static void MoveGameObjectToScene(GameObject gameObject, Scene destination, string name)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentNullException.ThrowIfNull(destination);
        var source = gameObject.scene ?? throw new InvalidOperationException("GameObject does not belong to a Scene.");
        if (ReferenceEquals(source, destination)) return;
        var sourceSiblingIndex = gameObject.transform.GetSiblingIndex();
        Scene.MoveGameObjectToScene(gameObject, destination);
        var destinationSiblingIndex = gameObject.transform.GetSiblingIndex();
        RegisterOperation(name,
            () => RestoreScene(gameObject, source, sourceSiblingIndex),
            () => RestoreScene(gameObject, destination, destinationSiblingIndex));
        EditorUtility.SetDirty(gameObject);
        EditorApplication.RaiseHierarchyChanged();
    }

    public static T AddComponent<T>(GameObject gameObject) where T : Component, new()
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        return ObjectFactory.AddComponent<T>(gameObject);
    }

    public static Component AddComponent(GameObject gameObject, Type type)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        return ObjectFactory.AddComponent(gameObject, type);
    }

    public static void RegisterCreatedObjectUndo(BObject objectToUndo, string name)
    {
        ArgumentNullException.ThrowIfNull(objectToUndo);
        RegisterCreatedObjectsUndo([objectToUndo], name);
    }

    internal static void RegisterCreatedObjectsUndo(IEnumerable<BObject> objectsToUndo, string name)
    {
        ArgumentNullException.ThrowIfNull(objectsToUndo);
        var states = objectsToUndo.Where(static item => item is not null)
            .DistinctBy(static item => item.Id)
            .Select(StructuralObjectState.Capture)
            .ToArray();
        if (states.Length == 0) return;
        RegisterOperation(name,
            () =>
            {
                for (var index = states.Length - 1; index >= 0; index--) states[index].Remove();
            },
            () =>
            {
                foreach (var state in states) state.Restore();
            });
    }

    public static void RegisterImporterUndo(BObject importer, string name) => RecordObject(importer, name);
    public static void RevertAllInCurrentGroup()
    {
        while (UndoStack.TryPeek(out var operation) && operation.Group == _group) PerformUndo();
    }

    public static void DestroyObjectImmediate(BObject objectToUndo)
    {
        ArgumentNullException.ThrowIfNull(objectToUndo);
        var structural = StructuralObjectState.Capture(objectToUndo);
        RegisterOperation($"Delete {objectToUndo.name}", structural.Restore, structural.Remove);
        structural.Remove();
        EditorSceneManager.MarkSceneDirty();
        EditorApplication.RaiseHierarchyChanged();
    }

    public static void PerformUndo() => Apply(UndoStack, RedoStack, isRedo: false);
    public static void PerformRedo() => Apply(RedoStack, UndoStack, isRedo: true);
    public static int GetCurrentGroup() => _group;
    public static string GetCurrentGroupName() => _groupName;
    public static void SetCurrentGroupName(string name) => _groupName = name ?? string.Empty;
    public static int IncrementCurrentGroup() => ++_group;
    public static void CollapseUndoOperations(int groupIndex) { }
    public static void FlushUndoRecordObjects() { }

    public static void ClearAll()
    {
        UndoStack.Clear();
        RedoStack.Clear();
        _group = 0;
        _groupName = "Edit";
    }

    internal static HistorySnapshot CaptureAndClear()
    {
        var snapshot = new HistorySnapshot(
            UndoStack.ToArray(), RedoStack.ToArray(), _group, _groupName);
        ClearAll();
        return snapshot;
    }

    internal static void Restore(HistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        UndoStack.Clear();
        foreach (var operation in snapshot.UndoOperations.Reverse()) UndoStack.Push(operation);
        RedoStack.Clear();
        foreach (var operation in snapshot.RedoOperations.Reverse()) RedoStack.Push(operation);
        _group = snapshot.Group;
        _groupName = snapshot.GroupName;
    }

    internal static void RegisterSnapshot(ObjectState state, string name)
    {
        _groupName = string.IsNullOrWhiteSpace(name) ? "Edit" : name;
        UndoStack.Push(new UndoOperation(_group, _groupName, [state], null, null));
        RedoStack.Clear();
    }

    internal static void RegisterOperation(string name, Action undo, Action redo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(redo);
        _groupName = string.IsNullOrWhiteSpace(name) ? "Edit" : name;
        UndoStack.Push(new UndoOperation(_group, _groupName, [], undo, redo));
        RedoStack.Clear();
    }

    private static void Apply(Stack<UndoOperation> source, Stack<UndoOperation> destination, bool isRedo)
    {
        if (!source.TryPop(out var operation)) return;
        try
        {
            if (operation.UndoAction is not null && operation.RedoAction is not null)
            {
                (isRedo ? operation.RedoAction : operation.UndoAction)();
                destination.Push(operation);
            }
            else
            {
                var inverse = operation.States.Select(state => ObjectState.Capture(state.Target)).ToArray();
                foreach (var state in operation.States) state.Restore();
                destination.Push(new UndoOperation(operation.Group, operation.Name, inverse, null, null));
            }
        }
        catch
        {
            source.Push(operation);
            throw;
        }
        EditorSceneManager.MarkSceneDirty();
        EditorCallbackDispatcher.Invoke(undoRedoPerformed, nameof(undoRedoPerformed));
        EditorCallbackDispatcher.Invoke(undoRedoEvent,
            new UndoRedoInfo(operation.Name, operation.Group, isRedo), nameof(undoRedoEvent));
    }

    private static void RestoreParent(Transform transform, Transform? parent, int siblingIndex)
    {
        transform.SetParent(parent);
        transform.SetSiblingIndex(siblingIndex);
        EditorUtility.SetDirty(transform);
        EditorApplication.RaiseHierarchyChanged();
    }

    private static void RestoreScene(GameObject gameObject, Scene scene, int siblingIndex)
    {
        if (!ReferenceEquals(gameObject.scene, scene)) Scene.MoveGameObjectToScene(gameObject, scene);
        gameObject.transform.SetSiblingIndex(siblingIndex);
        EditorUtility.SetDirty(gameObject);
        EditorApplication.RaiseHierarchyChanged();
    }

    internal sealed record UndoOperation(int Group, string Name, ObjectState[] States,
        Action? UndoAction, Action? RedoAction);

    internal sealed class HistorySnapshot
    {
        private readonly UndoOperation[] _undoOperations;
        private readonly UndoOperation[] _redoOperations;

        internal IEnumerable<UndoOperation> UndoOperations => _undoOperations;
        internal IEnumerable<UndoOperation> RedoOperations => _redoOperations;
        internal int Group { get; }
        internal string GroupName { get; }

        internal HistorySnapshot(
            UndoOperation[] undoOperations,
            UndoOperation[] redoOperations,
            int group,
            string groupName)
        {
            _undoOperations = undoOperations;
            _redoOperations = redoOperations;
            Group = group;
            GroupName = groupName;
        }
    }
}
