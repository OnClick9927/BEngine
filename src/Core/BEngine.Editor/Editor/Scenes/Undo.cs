namespace BEngine.Editor;

public static class Undo
{
    public delegate void UndoRedoCallback();
    public delegate void UndoRedoEventCallback(in UndoRedoInfo undo);
    public delegate void WillFlushUndoRecord();
    public delegate UndoPropertyModification[] PostprocessModifications(
        UndoPropertyModification[] modifications);

    private static readonly Stack<UndoOperation> UndoStack = [];
    private static readonly Stack<UndoOperation> RedoStack = [];
    private static BObject[] _snapshotTargets = [];
    private static ObjectState[] _snapshotStates = [];
    private static string _snapshotName = "Edit";
    private static int _group;
    private static string _groupName = string.Empty;
    private static bool _groupNameExplicit;
    private static bool _currentGroupHasRecords;
    private static bool _isProcessing;
    private static long _historyVersion;

    public static event UndoRedoCallback? undoRedoPerformed;
    public static event UndoRedoEventCallback? undoRedoEvent;
    public static event WillFlushUndoRecord? willFlushUndoRecord;
    public static event PostprocessModifications? postprocessModifications;

    public static bool canUndo => UndoStack.Count > 0;
    public static bool canRedo => RedoStack.Count > 0;
    public static bool isProcessing => _isProcessing;
    internal static long historyVersion => _historyVersion;

    public static void RecordObject(BObject objectToUndo, string name) =>
        RecordObjects([objectToUndo], name);

    public static void RecordObjects(BObject[] objectsToUndo, string name)
    {
        ArgumentNullException.ThrowIfNull(objectsToUndo);
        var targets = DistinctTargets(objectsToUndo);
        if (targets.Length == 0) return;
        Push(new UndoOperation(_group, OperationName(name),
            targets.Select(ObjectState.Capture).Cast<IUndoState>().ToArray(), targets,
            null, null, null, TargetsAffectScene(targets)));
    }

    public static void RegisterCompleteObjectUndo(BObject objectToUndo, string name) =>
        RecordObject(objectToUndo, name);

    public static void RegisterCompleteObjectUndo(BObject[] objectsToUndo, string name) =>
        RecordObjects(objectsToUndo, name);

    [Obsolete("Use Undo.RegisterCompleteObjectUndo instead")]
    public static void RegisterUndo(BObject objectToUndo, string name) =>
        RegisterCompleteObjectUndo(objectToUndo, name);

    [Obsolete("Use Undo.RegisterCompleteObjectUndo instead")]
    public static void RegisterUndo(BObject[] objectsToUndo, string name) =>
        RegisterCompleteObjectUndo(objectsToUndo, name);

    public static void RegisterFullObjectHierarchyUndo(BObject objectToUndo, string name)
    {
        ArgumentNullException.ThrowIfNull(objectToUndo);
        if (objectToUndo is not GameObject gameObject)
        {
            RecordObject(objectToUndo, name);
            return;
        }

        var state = FullHierarchyState.Capture(gameObject);
        Push(new UndoOperation(_group, OperationName(name), [state], state.Targets,
            null, null, null, true));
    }

    [Obsolete("Use Undo.RegisterFullObjectHierarchyUndo(BObject, string) instead")]
    public static void RegisterFullObjectHierarchyUndo(BObject objectToUndo) =>
        RegisterFullObjectHierarchyUndo(objectToUndo, "Full Object Hierarchy");

    public static void SetTransformParent(Transform transform, Transform? newParent, string name) =>
        SetTransformParent(transform, newParent, worldPositionStays: true, name);

    public static void SetTransformParent(
        Transform transform,
        Transform? newParent,
        bool worldPositionStays,
        string name)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (ReferenceEquals(transform.parent, newParent)) return;
        var oldParent = transform.parent;
        var oldSiblingIndex = transform.GetSiblingIndex();
        var before = ObjectState.Capture(transform);
        transform.SetParent(newParent, worldPositionStays);
        var newSiblingIndex = transform.GetSiblingIndex();
        var after = ObjectState.Capture(transform);
        RegisterOperation(name,
            () => RestoreParent(transform, oldParent, oldSiblingIndex, before),
            () => RestoreParent(transform, newParent, newSiblingIndex, after),
            transform);
        EditorUtility.SetDirty(transform);
        EditorApplication.RaiseHierarchyChanged();
    }

    public static void SetSiblingIndex(Transform transform, int siblingIndex, string name)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var oldIndex = transform.GetSiblingIndex();
        transform.SetSiblingIndex(siblingIndex);
        var newIndex = transform.GetSiblingIndex();
        if (oldIndex == newIndex) return;
        RegisterOperation(name,
            () => RestoreSiblingIndex(transform, oldIndex),
            () => RestoreSiblingIndex(transform, newIndex),
            transform);
        EditorUtility.SetDirty(transform);
        EditorApplication.RaiseHierarchyChanged();
    }

    public static void MoveGameObjectToScene(GameObject gameObject, Scene destination, string name)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentNullException.ThrowIfNull(destination);
        var source = gameObject.scene ??
                     throw new InvalidOperationException("GameObject does not belong to a Scene.");
        if (ReferenceEquals(source, destination)) return;
        var sourceSiblingIndex = gameObject.transform.GetSiblingIndex();
        Scene.MoveGameObjectToScene(gameObject, destination);
        var destinationSiblingIndex = gameObject.transform.GetSiblingIndex();
        RegisterOperation(name,
            () => RestoreScene(gameObject, source, sourceSiblingIndex),
            () => RestoreScene(gameObject, destination, destinationSiblingIndex),
            gameObject);
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
        if (objectToUndo is Transform)
            throw new ArgumentException("A Transform cannot be created independently from its GameObject.",
                nameof(objectToUndo));
        RegisterCreatedObjectsUndo([objectToUndo], name);
    }

    internal static void RegisterCreatedObjectsUndo(IEnumerable<BObject> objectsToUndo, string name)
    {
        ArgumentNullException.ThrowIfNull(objectsToUndo);
        var targets = DistinctTargets(objectsToUndo);
        var states = targets.Select(StructuralObjectState.Capture).ToArray();
        if (states.Length == 0) return;
        RegisterOperation(name,
            () =>
            {
                for (var index = states.Length - 1; index >= 0; index--) states[index].Remove();
            },
            () =>
            {
                foreach (var state in states) state.Restore();
            },
            targets);
    }

    public static void DestroyObjectImmediate(BObject objectToUndo)
    {
        ArgumentNullException.ThrowIfNull(objectToUndo);
        if (objectToUndo is Transform)
            throw new ArgumentException("A Transform cannot be destroyed independently from its GameObject.",
                nameof(objectToUndo));
        var structural = StructuralObjectState.Capture(objectToUndo);
        if (!structural.TryRemove()) return;
        RegisterOperation($"Delete {objectToUndo.name}", structural.Restore, structural.Remove, objectToUndo);
        EditorSceneManager.MarkSceneDirty();
        EditorApplication.RaiseHierarchyChanged();
    }

    public static void RegisterChildrenOrderUndo(BObject objectToUndo, string name)
    {
        ArgumentNullException.ThrowIfNull(objectToUndo);
        var transforms = objectToUndo switch
        {
            GameObject gameObject => gameObject.transform.children.ToArray(),
            Transform transform => transform.children.ToArray(),
            Scene scene => scene.rootGameObjects.Select(static item => item.transform).ToArray(),
            _ => []
        };
        if (transforms.Length > 0) RecordObjects(transforms.Cast<BObject>().ToArray(), name);
    }

    public static void RegisterImporterUndo(BObject importer, string name) => RecordObject(importer, name);

    public static void RegisterImporterUndo(string path, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var projectPath = path.Replace('\\', '/');
        var sourcePath = AssetDatabase.ResolveAssetPath(projectPath);
        RegisterFileUndo(sourcePath + ".meta", projectPath, name);
    }

    internal static void RegisterAssetFileUndo(string path, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var projectPath = path.Replace('\\', '/');
        RegisterFileUndo(AssetDatabase.ResolveAssetPath(projectPath), projectPath, name);
    }

    internal static void RegisterFileChangeUndo(
        Guid assetGuid,
        byte[] oldFileContent,
        byte[] newFileContent)
    {
        ArgumentNullException.ThrowIfNull(oldFileContent);
        ArgumentNullException.ThrowIfNull(newFileContent);
        var path = AssetDatabase.GUIDToAssetPath(assetGuid.ToString("N"));
        if (path.Length == 0) return;
        var fullPath = AssetDatabase.ResolveAssetPath(path);
        RegisterOperation("Modify Asset",
            () => RestoreFile(fullPath, oldFileContent, path),
            () => RestoreFile(fullPath, newFileContent, path), affectsScene: false);
    }

    public static void PerformUndo() => Apply(UndoStack, RedoStack, isRedo: false);
    public static void PerformRedo() => Apply(RedoStack, UndoStack, isRedo: true);
    public static bool HasUndo() => canUndo;
    public static bool HasRedo() => canRedo;
    public static int GetCurrentGroup() => _group;
    public static string GetCurrentGroupName() => UndoStack
        .FirstOrDefault(operation => operation.Group == _group)?.Name ?? string.Empty;

    public static void SetCurrentGroupName(string name)
    {
        _groupName = name ?? string.Empty;
        _groupNameExplicit = true;
        RenameGroup(UndoStack, _group, _groupName);
        RenameGroup(RedoStack, _group, _groupName);
        _historyVersion++;
    }

    public static void IncrementCurrentGroup()
    {
        _currentGroupHasRecords = false;
        _groupNameExplicit = false;
        _groupName = string.Empty;
        _group++;
    }

    internal static void EndCurrentEventGroup()
    {
        if (_isProcessing || !_currentGroupHasRecords) return;
        IncrementCurrentGroup();
    }

    public static void RevertAllInCurrentGroup() =>
        RevertWhile(static operation => operation.Group == _group);

    public static void RevertAllDownToGroup(int group) =>
        RevertWhile(operation => operation.Group >= group);

    public static void CollapseUndoOperations(int groupIndex)
    {
        var operations = new List<UndoOperation>();
        while (UndoStack.TryPeek(out var operation) && operation.Group >= groupIndex)
            operations.Add(UndoStack.Pop());
        if (operations.Count == 0) return;
        operations.Reverse();
        if (operations.Count == 1)
        {
            UndoStack.Push(operations[0]);
            return;
        }

        var targets = operations.SelectMany(static operation => operation.Targets)
            .DistinctBy(static target => target.GetInstanceID()).ToArray();
        UndoStack.Push(new UndoOperation(groupIndex, operations[^1].Name, [], targets,
            null, null, operations.ToArray(), operations.Any(static operation => operation.AffectsScene)));
        RedoStack.Clear();
        _historyVersion++;
    }

    public static void ClearUndo(BObject identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        var previousCount = UndoStack.Count + RedoStack.Count;
        FilterStack(UndoStack, operation => !ContainsTarget(operation, identifier));
        FilterStack(RedoStack, operation => !ContainsTarget(operation, identifier));
        if (UndoStack.Count + RedoStack.Count != previousCount) _historyVersion++;
    }

    public static void FlushUndoRecordObjects() =>
        InvokeCallbacks(willFlushUndoRecord, static callback => callback(),
            nameof(willFlushUndoRecord));

    public static void ClearAll()
    {
        UndoStack.Clear();
        RedoStack.Clear();
        _snapshotTargets = [];
        _snapshotStates = [];
        _group = 0;
        _groupName = string.Empty;
        _groupNameExplicit = false;
        _currentGroupHasRecords = false;
        _isProcessing = false;
        _historyVersion++;
    }

    [Obsolete("Use Undo.RecordObject instead")]
    public static void SetSnapshotTarget(BObject objectToUndo, string name) =>
        SetSnapshotTarget([objectToUndo], name);

    [Obsolete("Use Undo.RecordObjects instead")]
    public static void SetSnapshotTarget(BObject[] objectsToUndo, string name)
    {
        _snapshotTargets = DistinctTargets(objectsToUndo);
        _snapshotStates = [];
        _snapshotName = OperationName(name);
    }

    [Obsolete("Use Undo.RecordObject instead")]
    public static void ClearSnapshotTarget()
    {
        _snapshotTargets = [];
        _snapshotStates = [];
    }

    [Obsolete("Use Undo.RecordObject instead")]
    public static void CreateSnapshot() =>
        _snapshotStates = _snapshotTargets.Select(ObjectState.Capture).ToArray();

    [Obsolete("Use Undo.RecordObject instead")]
    public static void RestoreSnapshot()
    {
        foreach (var state in _snapshotStates) state.Restore();
    }

    [Obsolete("Use Undo.RecordObject instead")]
    public static void RegisterSnapshot()
    {
        if (_snapshotStates.Length == 0) return;
        Push(new UndoOperation(_group, _snapshotName, _snapshotStates,
            _snapshotTargets, null, null, null, TargetsAffectScene(_snapshotTargets)));
        _snapshotStates = [];
    }

    [Obsolete("Use object-specific Undo APIs instead")]
    public static void RegisterSceneUndo(string name)
    {
        if (EditorSceneManager.GetActiveScene() is not { } scene) return;
        var targets = scene.rootGameObjects.SelectMany(EnumerateHierarchy)
            .SelectMany(static item => item.components.Cast<BObject>().Prepend(item)).ToArray();
        RecordObjects(targets, name);
    }

    internal static UndoPropertyModification[] InvokePostprocessModifications(
        UndoPropertyModification[] modifications)
    {
        ArgumentNullException.ThrowIfNull(modifications);
        var result = modifications;
        if (postprocessModifications is null) return result;
        foreach (PostprocessModifications callback in postprocessModifications.GetInvocationList())
            result = callback(result) ?? [];
        return result;
    }

    internal static void GetRecords(List<string> undoRecords, out int undoCursor)
    {
        ArgumentNullException.ThrowIfNull(undoRecords);
        undoRecords.Clear();
        undoRecords.AddRange(UndoStack.Reverse().Select(static operation => operation.Name));
        undoCursor = undoRecords.Count;
    }

    internal static void GetRecords(List<string> undoRecords, List<string> redoRecords)
    {
        GetRecords(undoRecords, out _);
        ArgumentNullException.ThrowIfNull(redoRecords);
        redoRecords.Clear();
        redoRecords.AddRange(RedoStack.Select(static operation => operation.Name));
    }

    internal static IReadOnlyList<UndoHistoryEntry> GetHistory(out int cursor)
    {
        var undo = BuildHistory(UndoStack.Reverse(), isRedo: false);
        var redo = BuildHistory(RedoStack, isRedo: true);
        cursor = undo.Count;
        return [.. undo, .. redo];
    }

    internal static bool MoveToHistoryCursor(int targetCursor)
    {
        var history = GetHistory(out var cursor);
        if (targetCursor < 0 || targetCursor > history.Count)
            throw new ArgumentOutOfRangeException(nameof(targetCursor));
        if (targetCursor == cursor) return false;

        while (cursor > targetCursor)
        {
            if (!canUndo) return false;
            PerformUndo();
            cursor--;
        }
        while (cursor < targetCursor)
        {
            if (!canRedo) return false;
            PerformRedo();
            cursor++;
        }
        return true;
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
        _historyVersion++;
    }

    internal static void RegisterSnapshot(ObjectState state, string name)
    {
        ArgumentNullException.ThrowIfNull(state);
        Push(new UndoOperation(_group, OperationName(name), [state], [state.Target],
            null, null, null, TargetsAffectScene([state.Target])));
    }

    internal static void RegisterOperation(
        string name,
        Action undo,
        Action redo,
        params BObject[] targets)
        => RegisterOperation(name, undo, redo,
            targets.Length == 0 || TargetsAffectScene(targets), targets);

    private static void RegisterOperation(
        string name,
        Action undo,
        Action redo,
        bool affectsScene,
        params BObject[] targets)
    {
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(redo);
        Push(new UndoOperation(_group, OperationName(name), [], DistinctTargets(targets),
            undo, redo, null, affectsScene));
    }

    private static void Push(UndoOperation operation)
    {
        if (_groupNameExplicit) operation = operation with { Name = _groupName };
        _groupName = operation.Name;
        UndoStack.Push(operation);
        RedoStack.Clear();
        _currentGroupHasRecords = true;
        _historyVersion++;
    }

    private static void Apply(Stack<UndoOperation> source, Stack<UndoOperation> destination, bool isRedo)
    {
        if (!source.TryPop(out var operation)) return;
        try
        {
            _isProcessing = true;
            var inverse = ApplyOperation(operation, isRedo);
            destination.Push(inverse);
        }
        catch
        {
            source.Push(operation);
            throw;
        }
        finally { _isProcessing = false; }
        _historyVersion++;
        NotifyApplied(operation, isRedo);
    }

    private static UndoOperation ApplyOperation(UndoOperation operation, bool isRedo)
    {
        if (operation.Children is { Length: > 0 } children)
        {
            var inverseChildren = new UndoOperation[children.Length];
            if (isRedo)
            {
                for (var index = 0; index < children.Length; index++)
                    inverseChildren[index] = ApplyOperation(children[index], true);
            }
            else
            {
                for (var index = children.Length - 1; index >= 0; index--)
                    inverseChildren[index] = ApplyOperation(children[index], false);
            }
            return operation with { Children = inverseChildren };
        }

        if (operation.UndoAction is not null && operation.RedoAction is not null)
        {
            (isRedo ? operation.RedoAction : operation.UndoAction)();
            return operation;
        }

        var inverse = operation.States.Select(static state => state.CaptureInverse()).ToArray();
        foreach (var state in operation.States) state.Restore();
        return operation with { States = inverse };
    }

    private static void RevertWhile(Func<UndoOperation, bool> predicate)
    {
        var reverted = new List<UndoOperation>();
        try
        {
            _isProcessing = true;
            while (UndoStack.TryPeek(out var operation) && predicate(operation))
            {
                UndoStack.Pop();
                ApplyOperation(operation, isRedo: false);
                reverted.Add(operation);
            }
        }
        finally { _isProcessing = false; }
        if (reverted.Count == 0) return;
        RedoStack.Clear();
        _historyVersion++;
        NotifyApplied(reverted[^1], isRedo: false);
    }

    private static void NotifyApplied(UndoOperation operation, bool isRedo)
    {
        _groupName = operation.Name;
        if (operation.AffectsScene) EditorSceneManager.MarkSceneDirty();
        EditorApplication.RaiseHierarchyChanged();
        InvokeCallbacks(undoRedoPerformed, static callback => callback(),
            nameof(undoRedoPerformed));
        var info = new UndoRedoInfo(operation.Name, operation.Group, isRedo);
        InvokeCallbacks(undoRedoEvent, callback => callback(in info), nameof(undoRedoEvent));
    }

    private static void InvokeCallbacks<TCallback>(
        TCallback? callbacks,
        Action<TCallback> invoke,
        string callbackName)
        where TCallback : Delegate
    {
        if (callbacks is null) return;
        foreach (TCallback callback in callbacks.GetInvocationList())
        {
            var method = callback.Method;
            var feature = $"Editor callback {callbackName} " +
                          $"[{method.Module.ModuleVersionId:N}:{method.MetadataToken}]";
            EditorFeatureGuard.Invoke(feature, () => invoke(callback));
        }
    }

    private static void RestoreParent(
        Transform transform,
        Transform? parent,
        int siblingIndex,
        ObjectState state)
    {
        transform.SetParent(parent, worldPositionStays: false);
        state.Restore();
        transform.SetSiblingIndex(siblingIndex);
        EditorUtility.SetDirty(transform);
        EditorApplication.RaiseHierarchyChanged();
    }

    private static void RestoreSiblingIndex(Transform transform, int siblingIndex)
    {
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

    private static void RegisterFileUndo(string fullPath, string projectPath, string name)
    {
        byte[]? before = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
        byte[]? after = null;
        RegisterOperation(name,
            () =>
            {
                after = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
                RestoreFile(fullPath, before, projectPath);
            },
            () =>
            {
                before = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
                RestoreFile(fullPath, after, projectPath);
            }, affectsScene: false);
    }

    private static void RestoreFile(string fullPath, byte[]? content, string projectPath)
    {
        if (content is null)
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
            File.WriteAllBytes(fullPath, content);
        }
        AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceUpdate);
    }

    private static BObject[] DistinctTargets(IEnumerable<BObject> targets) => targets
        .Where(static item => item is not null)
        .DistinctBy(static item => item.GetInstanceID())
        .ToArray();

    private static string OperationName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Edit" : name;

    private static IEnumerable<GameObject> EnumerateHierarchy(GameObject root)
    {
        yield return root;
        foreach (var child in root.transform.children)
        foreach (var descendant in EnumerateHierarchy(child.gameObject))
            yield return descendant;
    }

    private static bool ContainsTarget(UndoOperation operation, BObject target) =>
        operation.Targets.Any(candidate => ReferenceEquals(candidate, target)) ||
        operation.Children?.Any(child => ContainsTarget(child, target)) == true;

    private static void FilterStack(
        Stack<UndoOperation> stack,
        Func<UndoOperation, bool> predicate)
    {
        var kept = stack.Where(predicate).Reverse().ToArray();
        stack.Clear();
        foreach (var operation in kept) stack.Push(operation);
    }

    private static void RenameGroup(Stack<UndoOperation> stack, int group, string name)
    {
        var operations = stack.ToArray();
        stack.Clear();
        for (var index = operations.Length - 1; index >= 0; index--)
            stack.Push(operations[index].Group == group
                ? operations[index] with { Name = name }
                : operations[index]);
    }

    private static bool TargetsAffectScene(IEnumerable<BObject> targets) =>
        targets.Any(static target => target is Scene or GameObject or Component);

    private static List<UndoHistoryEntry> BuildHistory(
        IEnumerable<UndoOperation> operations,
        bool isRedo)
    {
        var result = new List<UndoHistoryEntry>();
        foreach (var operation in operations)
        {
            var operationCount = CountRecordedOperations(operation);
            var targets = operation.Targets
                .DistinctBy(static target => target.GetInstanceID()).ToArray();
            var targetNames = targets.Select(static target =>
                    $"{target.name} ({ObjectNames.NicifyVariableName(target.GetType().Name)})")
                .ToArray();
            var details = targetNames.Length == 0
                ? $"{operationCount:N0} recorded operation(s)"
                : $"{operationCount:N0} operation(s) on {targetNames.Length:N0} object(s): " +
                  string.Join(", ", targetNames);
            result.Add(new UndoHistoryEntry(operation.Name, operation.Group, isRedo,
                operationCount, targetNames.Length, operation.AffectsScene,
                targetNames, details));
        }
        return result;
    }

    private static int CountRecordedOperations(UndoOperation operation) =>
        operation.Children is { Length: > 0 } children
            ? children.Sum(CountRecordedOperations)
            : 1;

    internal sealed record UndoOperation(
        int Group,
        string Name,
        IUndoState[] States,
        BObject[] Targets,
        Action? UndoAction,
        Action? RedoAction,
        UndoOperation[]? Children,
        bool AffectsScene);

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
