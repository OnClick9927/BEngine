using System.Reflection;
using BEngine.Serialization;

namespace BEngine.Editor;

internal sealed class ObjectState : IUndoState
{
    private readonly string _name;
    private readonly HideFlags _hideFlags;
    private readonly Dictionary<MemberInfo, object?> _members;
    private readonly Vector2? _localPosition;
    private readonly Fix64? _localRotation;
    private readonly Vector2? _localScale;
    private readonly int? _siblingIndex;
    private readonly (bool Active, string Tag, ulong Layer, bool IsStatic)? _gameObject;
    private readonly bool? _componentEnabled;
    private readonly Guid? _prefabAsset;
    private readonly Guid? _prefabSource;
    private readonly PrefabReferenceData? _prefabInstance;

    public BObject Target { get; }
    public long EstimatedBytes { get; }

    private ObjectState(BObject target)
    {
        Target = target;
        _prefabAsset = target.PrefabAssetId;
        _prefabSource = target.PrefabSourceId;
        if (target is GameObject prefabRoot && prefabRoot.PrefabInstance is { } reference)
            _prefabInstance = PrefabMerge.Clone(reference);
        _name = target.name;
        _hideFlags = target.hideFlags;
        _members = [];
        var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach (var member in GetSerializableMembers(target))
        {
            if (EditorFeatureGuard.TryInvoke(
                    $"Object state {target.GetType().FullName}.{member.Name}.get",
                    () => CloneValue(GetMemberValue(member, target), visited), fallback: null, out var value))
                _members[member] = value;
        }
        if (target is Transform transform)
        {
            _localPosition = transform.localPosition;
            _localRotation = transform.localRotation;
            _localScale = transform.localScale;
            _siblingIndex = transform.GetSiblingIndex();
        }
        if (target is Component component) _componentEnabled = component.enabled;
        if (target is GameObject gameObject)
        {
            _gameObject = (gameObject.activeSelf, gameObject.tag, gameObject.layer, gameObject.isStatic);
        }
        EstimatedBytes = 256L + _members.Count * 48L + UndoMemoryEstimate.Measure(_members.Values.ToArray(), _name, _prefabInstance);
    }

    public static ObjectState Capture(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return EditorPreviewRegistry.CaptureOriginalState(target, () => new ObjectState(target));
    }

    IUndoState IUndoState.CaptureInverse() => Capture(Target);

    public void Restore()
    {
        Target.name = _name;
        Target.PrefabAssetId = _prefabAsset;
        Target.PrefabSourceId = _prefabSource;
        if (Target is GameObject prefabRoot)
            prefabRoot.PrefabInstance = _prefabInstance is null ? null : PrefabMerge.Clone(_prefabInstance);
        Target.hideFlags = _hideFlags;
        var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach (var (member, value) in _members) SetMemberValue(member, Target, CloneValue(value, visited));
        if (Target is Component component && _componentEnabled is { } enabled) component.enabled = enabled;
        if (Target is Transform transform && _localPosition is { } position &&
            _localRotation is { } rotation && _localScale is { } scale)
        {
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
            if (_siblingIndex is { } siblingIndex) transform.SetSiblingIndex(siblingIndex);
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
        var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach (var member in destinationState._members.Keys)
        {
            if (sourceState._members.TryGetValue(member, out var value))
                SetMemberValue(member, destination, CloneValue(value, visited));
        }
        EditorUtility.SetDirty(destination);
    }

    internal static IEnumerable<FieldInfo> GetSerializableFields(Type type)
    {
        foreach (var field in RuntimeTypeCache.GetInstanceMembers(type).OfType<FieldInfo>())
        {
            if (field.DeclaringType != typeof(BObject) && RuntimeTypeCache.IsSerializableMember(field))
                yield return field;
        }
    }

    internal static IReadOnlyList<MemberInfo> GetSerializableMembers(BObject target)
    {
        if (target is Component component)
        {
            return ComponentFieldSerializer.GetSerializableMembers(component.GetType());
        }
        var fields = GetSerializableFields(target.GetType()).Cast<MemberInfo>();
        var properties = RuntimeTypeCache.GetInstanceMembers(target.GetType()).OfType<PropertyInfo>()
            .Where(property => RuntimeTypeCache.IsSerializableMember(property) &&
                               !property.IsDefined(typeof(YamlDotNet.Serialization.YamlIgnoreAttribute), true) &&
                               property.DeclaringType != typeof(BObject) &&
                               property.Name is not nameof(BObject.Id) and not nameof(BObject.name) and
                                   not nameof(BObject.hideFlags));
        return fields.Concat(properties).OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
    }

    private static object? GetMemberValue(MemberInfo member, object target) =>
        RuntimeTypeCache.GetMemberAccessor(member).Getter(target);

    private static void SetMemberValue(MemberInfo member, object target, object? value)
    {
        RuntimeTypeCache.GetMemberAccessor(member).Setter?.Invoke(target, value);
    }

    private static object? CloneValue(object? value, Dictionary<object, object> visited) =>
        EditorValueCloner.CloneSerializedBufferValue(value, visited);
}
