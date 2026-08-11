using System.Reflection;
using BEngine.Serialization;

namespace BEngine.Editor;

public readonly record struct UndoRedoInfo(string undoName, int undoGroup, bool isRedo);

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
        UndoStack.Push(new UndoOperation(_group, _groupName, states));
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
        RecordObject(transform, name);
        transform.SetParent(newParent);
        EditorUtility.SetDirty(transform);
        EditorApplication.RaiseHierarchyChanged();
    }

    public static T AddComponent<T>(GameObject gameObject) where T : Component, new()
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        var component = gameObject.AddComponent<T>();
        EditorUtility.SetDirty(gameObject);
        EditorApplication.RaiseHierarchyChanged();
        return component;
    }

    public static void DestroyObjectImmediate(BObject objectToUndo)
    {
        ArgumentNullException.ThrowIfNull(objectToUndo);
        BObject.Destroy(objectToUndo);
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

    internal static void RegisterSnapshot(ObjectState state, string name)
    {
        _groupName = string.IsNullOrWhiteSpace(name) ? "Edit" : name;
        UndoStack.Push(new UndoOperation(_group, _groupName, [state]));
        RedoStack.Clear();
    }

    private static void Apply(Stack<UndoOperation> source, Stack<UndoOperation> destination, bool isRedo)
    {
        if (!source.TryPop(out var operation)) return;
        var inverse = operation.States.Select(state => ObjectState.Capture(state.Target)).ToArray();
        foreach (var state in operation.States) state.Restore();
        destination.Push(new UndoOperation(operation.Group, operation.Name, inverse));
        EditorSceneManager.MarkSceneDirty();
        undoRedoPerformed?.Invoke();
        undoRedoEvent?.Invoke(new UndoRedoInfo(operation.Name, operation.Group, isRedo));
    }

    private sealed record UndoOperation(int Group, string Name, ObjectState[] States);
}

internal sealed class ObjectState
{
    private readonly string _name;
    private readonly HideFlags _hideFlags;
    private readonly Dictionary<MemberInfo, object?> _members;
    private readonly Vector3? _localPosition;
    private readonly Vector3? _localEulerAngles;
    private readonly Vector3? _localScale;
    private readonly (bool Active, string Tag, int Layer, bool IsStatic)? _gameObject;
    private readonly bool? _componentEnabled;

    public BObject Target { get; }

    private ObjectState(BObject target)
    {
        Target = target;
        _name = target.name;
        _hideFlags = target.hideFlags;
        _members = GetSerializableMembers(target)
            .ToDictionary(member => member, member => CloneValue(GetMemberValue(member, target)));
        if (target is Transform transform)
        {
            _localPosition = transform.localPosition;
            _localEulerAngles = transform.localEulerAngles;
            _localScale = transform.localScale;
        }
        if (target is Component component) _componentEnabled = component.enabled;
        if (target is GameObject gameObject)
        {
            _gameObject = (gameObject.activeSelf, gameObject.tag, gameObject.layer, gameObject.isStatic);
        }
    }

    public static ObjectState Capture(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new ObjectState(target);
    }

    public void Restore()
    {
        Target.name = _name;
        Target.hideFlags = _hideFlags;
        foreach (var (member, value) in _members) SetMemberValue(member, Target, CloneValue(value));
        if (Target is Component component && _componentEnabled is { } enabled) component.enabled = enabled;
        if (Target is Transform transform && _localPosition is { } position &&
            _localEulerAngles is { } rotation && _localScale is { } scale)
        {
            transform.localPosition = position;
            transform.localEulerAngles = rotation;
            transform.localScale = scale;
        }
        if (Target is GameObject gameObject && _gameObject is { } state)
        {
            gameObject.SetActive(state.Active);
            gameObject.tag = state.Tag;
            gameObject.layer = state.Layer;
            gameObject.isStatic = state.IsStatic;
        }
        EditorUtility.SetDirty(Target);
    }

    public static void CopyValues(BObject source, BObject destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (source.GetType() != destination.GetType())
        {
            throw new ArgumentException("Source and destination must have the same type.");
        }
        var sourceState = Capture(source);
        var destinationState = new ObjectState(destination);
        destination.name = sourceState._name;
        destination.hideFlags = sourceState._hideFlags;
        foreach (var member in destinationState._members.Keys)
        {
            if (sourceState._members.TryGetValue(member, out var value))
                SetMemberValue(member, destination, CloneValue(value));
        }
        EditorUtility.SetDirty(destination);
    }

    internal static IEnumerable<FieldInfo> GetSerializableFields(Type type)
    {
        for (var current = type; current is not null && current != typeof(BObject); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsStatic || field.IsInitOnly ||
                    field.GetCustomAttribute<NonSerializedAttribute>() is not null) continue;
                if (!field.IsPublic && field.GetCustomAttribute<SerializeFieldAttribute>() is null) continue;
                yield return field;
            }
        }
    }

    internal static IReadOnlyList<MemberInfo> GetSerializableMembers(BObject target)
    {
        if (target is Component component)
        {
            return ComponentFieldSerializer.GetSerializableMembers(component.GetType());
        }
        var fields = GetSerializableFields(target.GetType()).Cast<MemberInfo>();
        var properties = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public |
                                                         BindingFlags.NonPublic)
            .Where(property => property.GetIndexParameters().Length == 0 &&
                               property.GetMethod?.IsPublic is true && property.SetMethod?.IsPublic is true &&
                               property.Name is not nameof(BObject.Id) and not nameof(BObject.name) and
                                   not nameof(BObject.hideFlags));
        return fields.Concat(properties).OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
    }

    private static object? GetMemberValue(MemberInfo member, object target) => member switch
    {
        FieldInfo field => field.GetValue(target),
        PropertyInfo property => property.GetValue(target),
        _ => null
    };

    private static void SetMemberValue(MemberInfo member, object target, object? value)
    {
        if (member is FieldInfo field) field.SetValue(target, value);
        else if (member is PropertyInfo property) property.SetValue(target, value);
    }

    private static object? CloneValue(object? value) => value switch
    {
        null => null,
        Array array => array.Clone(),
        ICloneable cloneable => cloneable.Clone(),
        _ => value
    };
}
