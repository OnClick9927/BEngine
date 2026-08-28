using System.Reflection;
using BEngine.Documents;
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

    public BObject Target { get; }

    private ObjectState(BObject target)
    {
        Target = target;
        _name = target.name;
        _hideFlags = target.hideFlags;
        _members = [];
        foreach (var member in GetSerializableMembers(target))
        {
            if (EditorFeatureGuard.TryInvoke(
                    $"Object state {target.GetType().FullName}.{member.Name}.get",
                    () => CloneValue(GetMemberValue(member, target)), fallback: null, out var value))
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
    }

    public static ObjectState Capture(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new ObjectState(target);
    }

    IUndoState IUndoState.CaptureInverse() => Capture(Target);

    public void Restore()
    {
        Target.name = _name;
        Target.hideFlags = _hideFlags;
        foreach (var (member, value) in _members) SetMemberValue(member, Target, CloneValue(value));
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
        foreach (var member in destinationState._members.Keys)
        {
            if (sourceState._members.TryGetValue(member, out var value))
                SetMemberValue(member, destination, CloneValue(value));
        }
        EditorUtility.SetDirty(destination);
    }

    internal static IEnumerable<FieldInfo> GetSerializableFields(Type type)
    {
        foreach (var field in RuntimeTypeCache.GetInstanceMembers(type).OfType<FieldInfo>())
        {
            if (field.DeclaringType == typeof(BObject) || field.IsStatic || field.IsInitOnly) continue;
            var metadata = SerializedMemberMetadata.For(field);
            if (metadata.HasAttribute<NonSerializedAttribute>()) continue;
            if (!field.IsPublic && !metadata.HasAttribute<SerializeFieldAttribute>()) continue;
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
            .Where(property => property.GetIndexParameters().Length == 0 &&
                               property.GetMethod?.IsPublic is true && property.SetMethod?.IsPublic is true &&
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

    private static object? CloneValue(object? value) => value switch
    {
        null => null,
        _ => EditorValueCloner.CloneValue(value)
    };
}
