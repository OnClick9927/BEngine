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
        if (targets.Length == 0 || Matches1573())
        {
            throw new ArgumentException("At least one non-null target is required.", nameof(targets));
        }
        
        [global::BEngine.SkipLoopGuard]
        bool Matches1573()
        {
            var matchSource1573 = targets;
            ArgumentNullException.ThrowIfNull(matchSource1573, "source");
            global::System.ReadOnlySpan<global::BEngine.BObject> matchValues1573 = matchSource1573;
            foreach (var target in matchValues1573)
            {
                if (target is null)
                    return true;
            }
        
            return false;
        }
        targetObjects = [.. targets];
        targetObject = targetObjects[0];
        var members = EditorObjectSnapshot.GetSerializableMembers(targetObject);
        _propertyPaths = new string[members.Count];
        for (var index = 0; index < members.Count; index++) _propertyPaths[index] = members[index].Name;
        var visibleProperties = new SerializedProperty[_propertyPaths.Length];
        var visibleCount = 0;
        foreach (var path in _propertyPaths)
            if (IsVisible(path)) visibleProperties[visibleCount++] = new SerializedProperty(this, path);
        if (visibleCount != visibleProperties.Length) Array.Resize(ref visibleProperties, visibleCount);
        _visibleProperties = visibleProperties;
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
        var modifiedTargets = (((global::System.Collections.Generic.IEnumerable<global::BEngine.BObject>)(pendingRoots.Select(root => root.Target))) switch
        {
            global::BEngine.BObject[] { Length: 0 } => global::System.Array.Empty<global::BEngine.BObject>(),
            var distinctItems4207 => distinctItems4207.ToHashSet<global::BEngine.BObject>().ToArray()});
        EditorPreviewRegistry.PrepareInspectorChanges(_pendingModifications.Values.ToArray());
        // Capture at commit time: another inspector or script may have changed an
        // unrelated member since this SerializedObject started editing.
        foreach (var target in modifiedTargets)
        {
            if (target.isDestroyed)
                throw new InvalidOperationException("Cannot apply serialized changes to a destroyed object.");
            _pendingUndoStates[target.GetInstanceID()] = ObjectState.Capture(target);
        }
        var projectionSource4377 = pendingRoots;
        ArgumentNullException.ThrowIfNull(projectionSource4377, "source");
        var projectionResult4377 = projectionSource4377.Length == 0 ? global::System.Array.Empty<object?>() : new object?[projectionSource4377.Length];
        for (var projectionIndex4377 = 0; projectionIndex4377 < projectionSource4377.Length; projectionIndex4377++)
        {
            var projectionItem4377 = projectionSource4377[projectionIndex4377];
            projectionResult4377[projectionIndex4377] = projectionItem4377.GetAccessor().GetValue();
        }
        
        var previousRoots = projectionResult4377;
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
                var approvedTargets = new HashSet<int>();
                foreach (var modification in modifications)
                {
                    approvedTargets.Add(modification.previousValue.Target.GetInstanceID());
                    approvedTargets.Add(modification.currentValue.Target.GetInstanceID());
                }
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
            EditorCallbacks.Invoke(behaviour, nameof(MonoBehaviour.OnValidate), behaviour.OnValidate);
        }

        if (undoStates.Length > 0)
        {
            var projectionSource6786 = undoStates;
            ArgumentNullException.ThrowIfNull(projectionSource6786, "source");
            var projectionResult6786 = projectionSource6786.Length == 0 ? global::System.Array.Empty<global::BEngine.Editor.ObjectState>() : new global::BEngine.Editor.ObjectState[projectionSource6786.Length];
            for (var projectionIndex6786 = 0; projectionIndex6786 < projectionSource6786.Length; projectionIndex6786++)
            {
                var projectionItem6786 = projectionSource6786[projectionIndex6786];
                projectionResult6786[projectionIndex6786] = ObjectState.Capture(projectionItem6786.Target);
            }
            
            var redoStates = projectionResult6786;
            var projectionSource6892 = undoStates;
            ArgumentNullException.ThrowIfNull(projectionSource6892, "source");
            var projectionResult6892 = projectionSource6892.Length == 0 ? global::System.Array.Empty<global::BEngine.BObject>() : new global::BEngine.BObject[projectionSource6892.Length];
            for (var projectionIndex6892 = 0; projectionIndex6892 < projectionSource6892.Length; projectionIndex6892++)
            {
                var projectionItem6892 = projectionSource6892[projectionIndex6892];
                projectionResult6892[projectionIndex6892] = projectionItem6892.Target;
            }
            
            var targets = projectionResult6892;
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

    internal bool HasVisibleChildren(SerializedProperty property)
    {
        if (!TryGetValues(property.propertyPath, out var values) || values[0] is not { } value) return false;
        if (value is IList list)
        {
            if (list.Count == 0) return false;
            for (var index = 1; index < values.Length; index++)
                if (values[index] is not IList { Count: > 0 }) return false;
            return CanResolveForAllTargets($"{property.propertyPath}[0]");
        }
        var runtimeType = value.GetType();
        if (property.propertyType != SerializedPropertyType.Generic || value is string or BObject ||
            typeof(Delegate).IsAssignableFrom(runtimeType)) return false;
        for (var index = 1; index < values.Length; index++)
            if (values[index] is null || !runtimeType.IsInstanceOfType(values[index])) return false;
        foreach (var member in GetSerializableChildMembers(runtimeType))
            if (!SerializedMemberMetadata.For(member).IsHidden &&
                CanResolveForAllTargets($"{property.propertyPath}.{member.Name}")) return true;
        return false;
    }

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
        for (var index = 1; index < values.Length; index++)
            if (values[index] is null || !runtimeType.IsInstanceOfType(values[index])) return [];

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
            var inlineProperties = new SerializedProperty[members.Length];
            var inlineCount = 0;
            foreach (var member in members)
            {
                var path = $"{property.propertyPath}.{member}";
                if (CanResolveForAllTargets(path)) inlineProperties[inlineCount++] = GetOrCreateProperty(path);
            }
            if (inlineCount != inlineProperties.Length) Array.Resize(ref inlineProperties, inlineCount);
            return inlineProperties;
        }

        var childMembers = GetSerializableChildMembers(runtimeType);
        if (childMembers.Length == 0) return [];
        var properties = new SerializedProperty[childMembers.Length];
        var propertyCount = 0;
        foreach (var member in childMembers)
        {
            if (visibleOnly && SerializedMemberMetadata.For(member).IsHidden) continue;
            var path = $"{property.propertyPath}.{member.Name}";
            if (CanResolveForAllTargets(path)) properties[propertyCount++] = GetOrCreateProperty(path);
        }
        if (propertyCount != properties.Length) Array.Resize(ref properties, propertyCount);
        return properties;
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

        var accessors = new PropertyAccessor[targetObjects.Length];
        for (var index = 0; index < targetObjects.Length; index++)
            accessors[index] = ResolveAccessor(targetObjects[index], path);
        List<int>? changedIndices = null;
        for (var index = 0; index < accessors.Length; index++)
            if (!Equals(accessors[index].GetValue(), values[index]))
                (changedIndices ??= []).Add(index);
        if (changedIndices is null) return;

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
        var previousValues = new object?[changedIndices.Count];
        for (var changedIndex = 0; changedIndex < changedIndices.Count; changedIndex++)
            previousValues[changedIndex] = accessors[changedIndices[changedIndex]].GetValue();
        for (var changedIndex = 0; changedIndex < changedIndices.Count; changedIndex++)
        {
            var index = changedIndices[changedIndex];
            var target = targetObjects[index];
            var accessor = ResolveAccessor(target, path, stageChanges: true);
            accessor.SetValue(EditorValueCloner.CloneSerializedBufferValue(values[index]));
            TrackModification(target, path, previousValues[changedIndex], accessor.GetValue());
        }
        _modified = true;
        _changeVersion++;
        EditorCallbacks.Dispatch(changed, this, nameof(changed));
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

    internal bool HasMultipleDifferentValues(string path)
    {
        if (targetObjects.Length < 2) return false;
        if (targetObjects.Length == 2)
        {
            if (!TryGetValue(targetObjects[0], path, out var left) ||
                !TryGetValue(targetObjects[1], path, out var right)) return false;
            return !Equals(left, right);
        }
        // Read every getter before invoking user equality, and stop equality checks
        // after the first difference, as with the original multi-target snapshot.
        if (!TryGetValues(path, out var values)) return false;
        var first = values[0];
        for (var index = 1; index < values.Length; index++)
            if (!Equals(first, values[index])) return true;
        return false;
    }

    internal int GetMinimumCollectionSize(string path)
    {
        var minimum = int.MaxValue;
        if (targetObjects.Length <= 2)
        {
            if (!TryGetValue(targetObjects[0], path, out var first)) return 0;
            object? second = null;
            if (targetObjects.Length == 2 && !TryGetValue(targetObjects[1], path, out second)) return 0;
            if (!IncludeCollectionSize(first, ref minimum) ||
                targetObjects.Length == 2 && !IncludeCollectionSize(second, ref minimum)) return 0;
        }
        else
        {
            if (!TryGetValues(path, out var values)) return 0;
            foreach (var value in values) if (!IncludeCollectionSize(value, ref minimum)) return 0;
        }
        return minimum != int.MaxValue ? minimum : 0;
    }

    private static bool IncludeCollectionSize(object? value, ref int minimum)
    {
        if (value is null) minimum = 0;
        else if (value is IList list) minimum = Math.Min(minimum, list.Count);
        else return false;
        return true;
    }

    private bool TryGetValue(BObject target, string path, out object? value) =>
        EditorCallbacks.TryInvoke(ReadFeatureName(target, path),
            (Owner: this, Target: target, Path: path),
            static state => state.Owner.ResolveAccessor(state.Target, state.Path).GetValue(), fallback: null, out value);

    private PropertyAccessor ResolveAccessor(BObject target, string path, bool stageChanges = false)
    {
        if (!stageChanges && _pendingRoots.Count == 0) return PropertyPath.Resolve(target, path);
        return ResolveStagedAccessor(target, path, stageChanges);
    }

    private PropertyAccessor ResolveStagedAccessor(BObject target, string path, bool stageChanges) =>
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
        return accessor.WithValueAccess(root.Getter, root.Setter);
    }

    private PendingCommit[] GetPendingCommits()
    {
        if (_pendingModifications.Count <= 1)
        {
            foreach (var modification in _pendingModifications.Values)
            {
                var value = modification.currentValue;
                var path = PropertyPath.Normalize(value.PropertyPath);
                return [new PendingCommit(value.Target, path, ResolveAccessor(value.Target, path).GetValue())];
            }
            return [];
        }

        var changes = new (BObject Target, string Path)[_pendingModifications.Count];
        var paths = new HashSet<ModificationKey>(_pendingModifications.Count);
        var changeCount = 0;
        foreach (var modification in _pendingModifications.Values)
        {
            var value = modification.currentValue;
            var path = PropertyPath.Normalize(value.PropertyPath);
            if (paths.Add(new ModificationKey(value.Target.GetInstanceID(), path)))
                changes[changeCount++] = (value.Target, path);
        }

        // Finish the path snapshot before invoking getters, which can run user code.
        var commitCount = 0;
        for (var index = 0; index < changeCount; index++)
        {
            var change = changes[index];
            if (!HasChangedParent(paths, change.Target, change.Path)) changes[commitCount++] = change;
        }
        var commits = new PendingCommit[commitCount];
        for (var index = 0; index < commits.Length; index++)
        {
            var change = changes[index];
            commits[index] = new PendingCommit(change.Target, change.Path,
                ResolveAccessor(change.Target, change.Path).GetValue());
        }
        return commits;

        static bool HasChangedParent(HashSet<ModificationKey> paths, BObject target, string path)
        {
            for (var parent = PropertyPath.ParentPath(path); parent.Length > 0; parent = PropertyPath.ParentPath(parent))
                if (paths.Contains(new ModificationKey(target.GetInstanceID(), parent))) return true;
            return false;
        }
    }

    private bool TryResolve(BObject target, string path, out PropertyAccessor accessor)
    {
        var succeeded = EditorCallbacks.TryInvoke(ReadFeatureName(target, path),
            (Owner: this, Target: target, Path: path),
            static state => state.Owner.TryResolveMappedAccessor(state.Target, state.Path),
            default, out var resolved);
        accessor = resolved.Accessor;
        return succeeded && resolved.Found;
    }

    private (bool Found, PropertyAccessor Accessor) TryResolveMappedAccessor(BObject target, string path)
    {
        if (_pendingRoots.Count != 0) return TryResolveStagedRead(target, path);
        var found = PropertyPath.TryResolve(target, path, out var accessor);
        return (found, accessor);
    }

    private (bool Found, PropertyAccessor Accessor) TryResolveStagedRead(BObject target, string path)
    {
        var found = PropertyPath.TryResolve(target, path, out var accessor,
            root => MapRootAccessor(target, root, stageChanges: false));
        return (found, accessor);
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

    private sealed class PendingRoot
    {
        internal PendingRoot(BObject target, PropertyAccessor accessor, object? value)
        {
            Target = target;
            Accessor = accessor;
            Value = value;
            Getter = GetValue;
            Setter = SetValue;
        }

        internal BObject Target { get; }
        internal PropertyAccessor Accessor { get; }
        internal object? Value { get; set; }
        internal Func<object?> Getter { get; }
        internal Action<object?> Setter { get; }

        private object? GetValue() => Value;
        private void SetValue(object? value) => Value = value;
    }

    private static MemberInfo[] GetSerializableChildMembers(Type type)
    {
        return ChildMemberCache.GetValue(type, static childType =>
            SerializationMetadata.GetObjectMembers(childType)
                .GroupBy(member => member.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .ToArray());
    }

}
