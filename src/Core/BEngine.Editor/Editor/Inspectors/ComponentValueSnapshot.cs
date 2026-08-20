using System.Reflection;
using BEngine.Documents;

namespace BEngine.Editor;

internal sealed class ComponentValueSnapshot
{
    private readonly Type _type;
    private readonly Dictionary<MemberInfo, object?> _values;
    private readonly Dictionary<string, string>? _missingFields;
    private readonly string? _missingType;
    private readonly bool _enabled;
    private readonly (Vector2 Position, Fix64 Rotation, Vector2 Scale)? _transform;

    private ComponentValueSnapshot(Component component)
    {
        _type = component.GetType();
        _enabled = component.enabled;
        if (component is MissingComponent missing)
        {
            _missingType = missing.originalType;
            _missingFields = new Dictionary<string, string>(missing.serializedFields, StringComparer.Ordinal);
            _values = [];
            return;
        }
        _transform = component is Transform transform
            ? (transform.localPosition, transform.localRotation, transform.localScale)
            : null;
        _values = ComponentFieldSerializer.GetSerializableMembers(_type)
            .Where(member => component is not Transform || !IsTransformBaseMember(member))
            .ToDictionary(member => member,
                member => EditorValueCloner.CloneValue(ComponentFieldSerializer.GetMemberValue(member, component)));
    }

    internal static ComponentValueSnapshot Capture(Component component) => new(component);

    internal bool CanApplyTo(Component component) => component.GetType() == _type &&
        (component is not MissingComponent missing ||
         string.Equals(missing.originalType, _missingType, StringComparison.Ordinal));

    internal void ApplyTo(Component component)
    {
        if (!CanApplyTo(component))
            throw new ArgumentException("The component snapshot type does not match the destination.",
                nameof(component));
        if (component is MissingComponent missing)
        {
            ComponentFieldSerializer.Deserialize(missing, _missingFields ?? new Dictionary<string, string>());
        }
        else
        {
            var values = _values.ToDictionary(static pair => pair.Key,
                static pair => EditorValueCloner.CloneValue(pair.Value));
            foreach (var (member, value) in values)
                ComponentFieldSerializer.SetMemberValue(member, component, value);
        }
        if (component is Transform transform && _transform is { } state)
        {
            transform.localPosition = state.Position;
            transform.localRotation = state.Rotation;
            transform.localScale = state.Scale;
        }
        component.enabled = _enabled;
    }

    private static bool IsTransformBaseMember(MemberInfo member) =>
        member.DeclaringType?.IsAssignableFrom(typeof(Transform)) is true;
}
