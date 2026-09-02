using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public sealed class SerializedObject : IDisposable
{
    private static readonly ConditionalWeakTable<Type, MemberInfo[]> ChildMemberCache = new();
    private bool _modified;
    private int _changeVersion;
    private string _undoName = "Edit";
    private readonly string[] _propertyPaths;
    private readonly SerializedProperty[] _visibleProperties;
    private readonly Dictionary<string, SerializedProperty> _properties = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ObjectState> _pendingUndoStates = [];
    private readonly Dictionary<ModificationKey, UndoPropertyModification> _pendingModifications = [];

    public BObject targetObject { get; }
    public BObject[] targetObjects { get; }
    public bool isEditingMultipleObjects => targetObjects.Length > 1;
    public bool hasModifiedProperties => _modified;
    public event Action<SerializedObject>? changed;

    public SerializedObject(BObject target) : this([target]) { }

    public static SerializedObject Create(BObject target) => new(target);

    public SerializedObject(BObject[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Length == 0 || targets.Any(target => target is null))
        {
            throw new ArgumentException("At least one non-null target is required.", nameof(targets));
        }
        targetObjects = [.. targets];
        targetObject = targetObjects[0];
        _propertyPaths = ObjectState.GetSerializableMembers(targetObject)
            .Select(member => member.Name)
            .ToArray();
        _visibleProperties = _propertyPaths.Where(IsVisible)
            .Select(path => new SerializedProperty(this, path)).ToArray();
        foreach (var property in _visibleProperties) _properties[property.propertyPath] = property;
    }

    public SerializedProperty? FindProperty(string propertyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
        if (_properties.TryGetValue(propertyPath, out var cached)) return cached;
        return TryResolve(targetObject, propertyPath, out _)
            ? GetOrCreateProperty(propertyPath)
            : null;
    }

    public SerializedProperty GetIterator() => SerializedProperty.CreateIterator(this, GetPropertyPaths());
    public SerializedProperty GetVisibleIterator() => GetIterator();

    public SerializedProperty[] GetVisibleProperties() => _visibleProperties;

    public void Update()
    {
        _modified = false;
        ClearPendingUndo();
    }
    public void UpdateIfRequiredOrScript() => Update();

    public bool ApplyModifiedProperties() => ApplyModifiedProperties(recordUndo: true);

    public bool ApplyModifiedPropertiesWithoutUndo() => ApplyModifiedProperties(recordUndo: false);

    private bool ApplyModifiedProperties(bool recordUndo)
    {
        if (!_modified) return false;

        ObjectState[] undoStates = [];
        if (recordUndo && _pendingUndoStates.Count > 0)
        {
            var modifications = Undo.InvokePostprocessModifications(_pendingModifications.Values.ToArray());
            if (modifications.Length > 0)
            {
                var approvedTargets = modifications
                    .SelectMany(static modification => new[]
                    {
                        modification.previousValue.Target,
                        modification.currentValue.Target
                    })
                    .Select(static target => target.GetInstanceID())
                    .ToHashSet();
                undoStates = _pendingUndoStates
                    .Where(pair => approvedTargets.Contains(pair.Key))
                    .Select(static pair => pair.Value)
                    .ToArray();
            }
        }

        foreach (var target in targetObjects)
        {
            EditorUtility.SetDirty(target);
            if (target is not MonoBehaviour behaviour) continue;
            EditorFeatureGuard.Invoke(behaviour, nameof(MonoBehaviour.OnValidate), behaviour.OnValidate);
        }

        if (undoStates.Length > 0)
        {
            var redoStates = undoStates.Select(static state => ObjectState.Capture(state.Target)).ToArray();
            var targets = undoStates.Select(static state => state.Target).ToArray();
            Undo.RegisterOperation(_undoName,
                () => RestoreStates(undoStates),
                () => RestoreStates(redoStates),
                targets);
        }

        _modified = false;
        ClearPendingUndo();
        return true;
    }

    public void Dispose() => ClearPendingUndo();

    internal object? GetValue(string path) => PropertyPath.Resolve(targetObject, path).GetValue();
    internal bool TryGetValue(string path, out object? value) =>
        TryGetValue(targetObject, path, out value);
    internal int changeVersion => _changeVersion;

    internal IReadOnlyList<SerializedProperty> GetVisibleChildren(SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!TryGetValues(property.propertyPath, out var values) || values[0] is not { } value) return [];

        if (value is IList list)
        {
            var count = list.Count;
            for (var index = 1; index < values.Length; index++)
            {
                if (values[index] is not IList candidate) return [];
                count = Math.Min(count, candidate.Count);
            }
            var children = new List<SerializedProperty>(count);
            for (var index = 0; index < count; index++)
            {
                var path = $"{property.propertyPath}[{index}]";
                if (CanResolveForAllTargets(path)) children.Add(GetOrCreateProperty(path));
            }
            return children;
        }

        var runtimeType = value.GetType();
        if (runtimeType.IsValueType || value is string or BObject || typeof(Delegate).IsAssignableFrom(runtimeType))
            return [];
        if (values.Skip(1).Any(candidate => candidate is null || !runtimeType.IsInstanceOfType(candidate))) return [];

        return GetSerializableChildMembers(runtimeType)
            .Select(member => $"{property.propertyPath}.{member.Name}")
            .Where(CanResolveForAllTargets)
            .Select(GetOrCreateProperty)
            .ToArray();
    }

    internal bool IsVisible(string path) =>
        !SerializedMemberMetadata.For(PropertyPath.Resolve(targetObject, path).MemberInfo).IsHidden;

    internal void SetValue(string path, object? value)
    {
        var values = new object?[targetObjects.Length];
        if (targetObjects.Length > 1 && value is IList)
        {
            for (var index = 0; index < values.Length; index++)
                values[index] = EditorValueCloner.CloneValue(value);
        }
        else Array.Fill(values, value);
        SetValues(path, values);
    }

    internal void SetValues(string path, IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != targetObjects.Length)
            throw new ArgumentException("A value is required for every serialized target.", nameof(values));

        var accessors = targetObjects.Select(target => PropertyPath.Resolve(target, path)).ToArray();
        var changedIndices = Enumerable.Range(0, accessors.Length)
            .Where(index => !Equals(accessors[index].GetValue(), values[index]))
            .ToArray();
        if (changedIndices.Length == 0) return;

        foreach (var index in changedIndices)
        {
            var accessor = accessors[index];
            var value = values[index];
            if (!accessor.CanWrite)
                throw new InvalidOperationException($"Property '{path}' is read-only.");
            var valueType = accessor.ValueType;
            if (value is null)
            {
                if (valueType.IsValueType && Nullable.GetUnderlyingType(valueType) is null)
                    throw new ArgumentException($"Null cannot be assigned to {valueType.Name}.", nameof(value));
            }
            else if (!valueType.IsInstanceOfType(value))
                throw new ArgumentException(
                    $"Object of type {value.GetType().Name} cannot be assigned to {valueType.Name}.",
                    nameof(value));
        }

        if (!_modified) _undoName = $"Modify {PropertyPath.LeafName(path)}";
        var previousValues = changedIndices.ToDictionary(index => index, index => accessors[index].GetValue());
        foreach (var index in changedIndices)
        {
            var target = targetObjects[index];
            var targetId = target.GetInstanceID();
            if (!_pendingUndoStates.ContainsKey(targetId))
                _pendingUndoStates[targetId] = ObjectState.Capture(target);
        }
        foreach (var index in changedIndices)
        {
            var target = targetObjects[index];
            accessors[index].SetValue(values[index]);
            TrackModification(target, path, previousValues[index], accessors[index].GetValue());
        }
        _modified = true;
        _changeVersion++;
        EditorCallbackDispatcher.Invoke(changed, this, nameof(changed));
    }

    private string[] GetPropertyPaths() => _propertyPaths;

    internal SerializedProperty GetOrCreateProperty(string path)
    {
        if (_properties.TryGetValue(path, out var cached)) return cached;
        return _properties[path] = new SerializedProperty(this, path);
    }

    private bool CanResolveForAllTargets(string path)
    {
        foreach (var target in targetObjects)
            if (!TryResolve(target, path, out _)) return false;
        return true;
    }

    internal bool TryGetValues(string path, out object?[] values)
    {
        values = new object?[targetObjects.Length];
        for (var index = 0; index < targetObjects.Length; index++)
        {
            if (!TryGetValue(targetObjects[index], path, out values[index])) return false;
        }
        return true;
    }

    private bool TryGetValue(BObject target, string path, out object? value) =>
        EditorFeatureGuard.TryInvoke(ReadFeatureName(target, path),
            () => PropertyPath.Resolve(target, path).GetValue(), fallback: null, out value);

    private bool TryResolve(BObject target, string path, out PropertyAccessor accessor)
    {
        var resolved = default(PropertyAccessor);
        var wasResolved = false;
        var succeeded = EditorFeatureGuard.Invoke(ReadFeatureName(target, path),
            () => wasResolved = PropertyPath.TryResolve(target, path, out resolved));
        accessor = resolved;
        return succeeded && wasResolved;
    }

    private static string ReadFeatureName(BObject target, string path) =>
        $"SerializedProperty {target.GetType().FullName ?? target.GetType().Name}.{path}.get";

    private void TrackModification(BObject target, string path, object? previousValue, object? currentValue)
    {
        var key = new ModificationKey(target.GetInstanceID(), path);
        var current = new PropertyModification(target, path, FormatModificationValue(currentValue));
        if (_pendingModifications.TryGetValue(key, out var existing))
        {
            _pendingModifications[key] = existing with { currentValue = current };
            return;
        }
        var previous = new PropertyModification(target, path, FormatModificationValue(previousValue));
        _pendingModifications[key] = new UndoPropertyModification(previous, current);
    }

    private void ClearPendingUndo()
    {
        _pendingUndoStates.Clear();
        _pendingModifications.Clear();
        _undoName = "Edit";
    }

    private static void RestoreStates(IEnumerable<ObjectState> states)
    {
        foreach (var state in states) state.Restore();
    }

    private static string FormatModificationValue(object? value) => value switch
    {
        null => "null",
        string text => text,
        char character => character.ToString(),
        bool boolean => boolean ? "true" : "false",
        BObject engineObject => $"{engineObject.GetType().FullName}:{engineObject.GetInstanceID()}",
        IList list => $"[{string.Join(",", list.Cast<object?>().Select(FormatModificationValue))}]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private readonly record struct ModificationKey(int TargetId, string PropertyPath);

    private static MemberInfo[] GetSerializableChildMembers(Type type)
    {
        return ChildMemberCache.GetValue(type, static childType =>
            RuntimeTypeCache.GetInstanceMembers(childType)
                .Where(IsSerializableChildMember)
                .Where(member => !SerializedMemberMetadata.For(member).IsHidden)
                .GroupBy(member => member.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool IsSerializableChildMember(MemberInfo member) =>
        RuntimeTypeCache.IsSerializableMember(member);
}
