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
    // A pending Inspector edit owns its value graph. Only modified paths are committed,
    // so Update/Dispose can discard edits without rolling back live objects.
    private readonly Dictionary<ModificationKey, PendingRoot> _pendingRoots = [];

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
        if (_modified) _changeVersion++;
        _modified = false;
        ClearPendingUndo();
    }
    public void UpdateIfRequiredOrScript() => Update();

    public bool ApplyModifiedProperties() => ApplyModifiedProperties(recordUndo: true);

    public bool ApplyModifiedPropertiesWithoutUndo() => ApplyModifiedProperties(recordUndo: false);

    private bool ApplyModifiedProperties(bool recordUndo)
    {
        if (!_modified) return false;

        var pendingRoots = GetPendingCommits();
        var modifiedTargets = pendingRoots.Select(root => root.Target).Distinct().ToArray();
        EditorPreviewRegistry.PrepareInspectorChanges(_pendingModifications.Values.ToArray());
        // Capture at commit time: another inspector or script may have changed an
        // unrelated member since this SerializedObject started editing.
        foreach (var target in modifiedTargets)
        {
            if (target.isDestroyed)
                throw new InvalidOperationException("Cannot apply serialized changes to a destroyed object.");
            _pendingUndoStates[target.GetInstanceID()] = ObjectState.Capture(target);
        }
        var previousRoots = pendingRoots.Select(root => root.GetAccessor().GetValue()).ToArray();
        var appliedCount = 0;
        try
        {
            foreach (var root in pendingRoots)
            {
                appliedCount++;
                root.GetAccessor().SetValue(root.Value);
            }
        }
        catch (Exception applyException)
        {
            List<Exception>? rollbackExceptions = null;
            for (var index = appliedCount - 1; index >= 0; index--)
            {
                try { pendingRoots[index].GetAccessor().SetValue(previousRoots[index]); }
                catch (Exception rollbackException) { (rollbackExceptions ??= []).Add(rollbackException); }
            }
            _pendingUndoStates.Clear();
            if (rollbackExceptions is not null)
                throw new AggregateException("Applying serialized changes failed, and one or more members could not be restored.",
                    new[] { applyException }.Concat(rollbackExceptions));
            throw;
        }

        var recordedTargets = EditorPreviewRegistry.RecordInspectorChanges(_pendingModifications.Values);
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
                    .Where(pair => approvedTargets.Contains(pair.Key) && !recordedTargets.Contains(pair.Value.Target))
                    .Select(static pair => pair.Value)
                    .ToArray();
            }
        }

        foreach (var target in modifiedTargets)
        {
            if (!recordedTargets.Contains(target)) EditorUtility.SetDirty(target);
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

    public void Dispose() => Update();

    internal PropertyAccessor GetAccessor(string path) => ResolveAccessor(targetObject, path);
    internal object? GetValue(string path) => GetAccessor(path).GetValue();
    internal bool TryGetValue(string path, out object? value) =>
        TryGetValue(targetObject, path, out value);
    internal int changeVersion => _changeVersion;

    internal IReadOnlyList<SerializedProperty> GetVisibleChildren(SerializedProperty property)
        => GetChildren(property, visibleOnly: true, includeInlineValues: false);

    internal IReadOnlyList<SerializedProperty> GetTraversalChildren(SerializedProperty property, bool visibleOnly)
        => GetChildren(property, visibleOnly, includeInlineValues: true);

    private IReadOnlyList<SerializedProperty> GetChildren(SerializedProperty property, bool visibleOnly,
        bool includeInlineValues)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!TryGetValues(property.propertyPath, out var values) || values[0] is not { } value) return [];
        if (includeInlineValues && !value.GetType().IsValueType)
        {
            for (var parent = PropertyPath.ParentPath(property.propertyPath); parent.Length > 0;
                 parent = PropertyPath.ParentPath(parent))
                if (TryGetValue(parent, out var ancestor) && ReferenceEquals(value, ancestor)) return [];
        }

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
        if (property.propertyType != SerializedPropertyType.Generic &&
            !(includeInlineValues && property.propertyType is SerializedPropertyType.Vector2 or
                SerializedPropertyType.Vector4 or SerializedPropertyType.Color or SerializedPropertyType.Rect) ||
            value is string or BObject ||
            typeof(Delegate).IsAssignableFrom(runtimeType))
            return [];
        if (values.Skip(1).Any(candidate => candidate is null || !runtimeType.IsInstanceOfType(candidate))) return [];

        if (includeInlineValues && property.propertyType != SerializedPropertyType.Generic)
        {
            string[] members = property.propertyType switch
            {
                SerializedPropertyType.Vector2 => ["x", "y"],
                SerializedPropertyType.Vector4 => ["x", "y", "z", "w"],
                SerializedPropertyType.Color => ["r", "g", "b", "a"],
                SerializedPropertyType.Rect => ["x", "y", "width", "height"],
                _ => []
            };
            return members.Select(member => $"{property.propertyPath}.{member}")
                .Where(CanResolveForAllTargets).Select(GetOrCreateProperty).ToArray();
        }

        return GetSerializableChildMembers(runtimeType)
            .Where(member => !visibleOnly || !SerializedMemberMetadata.For(member).IsHidden)
            .Select(member => $"{property.propertyPath}.{member.Name}")
            .Where(CanResolveForAllTargets)
            .Select(GetOrCreateProperty)
            .ToArray();
    }

    internal bool IsVisible(string path) =>
        !SerializedMemberMetadata.For(GetAccessor(path).MemberInfo).IsHidden;

    internal void SetValue(string path, object? value)
    {
        var values = new object?[targetObjects.Length];
        Array.Fill(values, value);
        SetValues(path, values);
    }

    internal void SetValues(string path, IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != targetObjects.Length)
            throw new ArgumentException("A value is required for every serialized target.", nameof(values));

        var accessors = targetObjects.Select(target => ResolveAccessor(target, path)).ToArray();
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
            var accessor = ResolveAccessor(target, path, stageChanges: true);
            accessor.SetValue(EditorValueCloner.CloneSerializedBufferValue(values[index]));
            TrackModification(target, path, previousValues[index], accessor.GetValue());
        }
        _modified = true;
        _changeVersion++;
        EditorCallbackDispatcher.Invoke(changed, this, nameof(changed));
    }

    internal string[] GetPropertyPaths() => _propertyPaths;

    internal void DetachPropertyCursor(SerializedProperty property)
    {
        if (!_properties.TryGetValue(property.propertyPath, out var cached) || !ReferenceEquals(cached, property)) return;
        var replacement = property.Copy();
        _properties[property.propertyPath] = replacement;
        for (var index = 0; index < _visibleProperties.Length; index++)
            if (ReferenceEquals(_visibleProperties[index], property)) _visibleProperties[index] = replacement;
    }

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
            () => ResolveAccessor(target, path).GetValue(), fallback: null, out value);

    private PropertyAccessor ResolveAccessor(BObject target, string path, bool stageChanges = false) =>
        PropertyPath.Resolve(target, path, accessor => MapRootAccessor(target, accessor, stageChanges));

    private PropertyAccessor MapRootAccessor(BObject target, PropertyAccessor accessor, bool stageChanges)
    {
        var key = new ModificationKey(target.GetInstanceID(), accessor.MemberInfo!.Name);
        if (!_pendingRoots.TryGetValue(key, out var root))
        {
            if (!stageChanges) return accessor;
            root = new PendingRoot(target, accessor, EditorValueCloner.CloneSerializedBufferValue(accessor.GetValue()));
            _pendingRoots.Add(key, root);
        }
        return accessor.WithValueAccess(() => root.Value, value => root.Value = value);
    }

    private PendingCommit[] GetPendingCommits()
    {
        var changes = _pendingModifications.Values.Select(item => item.currentValue)
            .Select(item => (item.Target, Path: PropertyPath.Normalize(item.PropertyPath)))
            .DistinctBy(item => new ModificationKey(item.Target.GetInstanceID(), item.Path)).ToArray();
        var paths = changes.Select(item => new ModificationKey(item.Target.GetInstanceID(), item.Path)).ToHashSet();
        return changes.Where(item => !HasChangedParent(item.Target, item.Path)).Select(item => new PendingCommit(
            item.Target, item.Path, ResolveAccessor(item.Target, item.Path).GetValue())).ToArray();

        bool HasChangedParent(BObject target, string path)
        {
            for (var parent = PropertyPath.ParentPath(path); parent.Length > 0; parent = PropertyPath.ParentPath(parent))
                if (paths.Contains(new ModificationKey(target.GetInstanceID(), parent))) return true;
            return false;
        }
    }

    private bool TryResolve(BObject target, string path, out PropertyAccessor accessor)
    {
        var resolved = default(PropertyAccessor);
        var wasResolved = false;
        var succeeded = EditorFeatureGuard.Invoke(ReadFeatureName(target, path),
            () => wasResolved = PropertyPath.TryResolve(target, path, out resolved,
                accessor => MapRootAccessor(target, accessor, stageChanges: false)));
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
        _pendingRoots.Clear();
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
    private readonly record struct PendingCommit(BObject Target, string Path, object? Value)
    {
        // Resolve per write so edits to two fields of one boxed struct compose correctly.
        internal PropertyAccessor GetAccessor() => PropertyPath.Resolve(Target, Path);
    }

    private sealed class PendingRoot(BObject target, PropertyAccessor accessor, object? value)
    {
        internal BObject Target { get; } = target;
        internal PropertyAccessor Accessor { get; } = accessor;
        internal object? Value { get; set; } = value;
    }

    private static MemberInfo[] GetSerializableChildMembers(Type type)
    {
        return ChildMemberCache.GetValue(type, static childType =>
            RuntimeTypeCache.GetInstanceMembers(childType)
                .Where(IsSerializableChildMember)
                .GroupBy(member => member.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool IsSerializableChildMember(MemberInfo member) =>
        RuntimeTypeCache.IsSerializableMember(member);
}
