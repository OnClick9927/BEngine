using System.Reflection;
using BEngine.Serialization;

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
        _values = BuildIndex1046(ComponentFieldSerializer.GetSerializableMembers(_type).Where(member => component is not Transform || !IsTransformBaseMember(member)), null);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<global::System.Reflection.MemberInfo, object?> BuildIndex1046(global::System.Collections.Generic.IEnumerable<global::System.Reflection.MemberInfo> indexSource1046, global::System.Collections.Generic.IEqualityComparer<global::System.Reflection.MemberInfo>? indexComparer1046)
        {
            ArgumentNullException.ThrowIfNull(indexSource1046, "source");
            var knownCount = indexSource1046.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<global::System.Reflection.MemberInfo, object?>(indexComparer1046);
            global::System.ReadOnlySpan<global::System.Reflection.MemberInfo> values;
            if (knownCount && indexSource1046 is global::System.Reflection.MemberInfo[] array)
                values = array;
            else if (knownCount && indexSource1046 is global::System.Collections.Generic.List<global::System.Reflection.MemberInfo> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Reflection.MemberInfo, object?>(capacity, indexComparer1046);
                foreach (var item in indexSource1046)
                    result.Add(IndexKey1046(item), IndexValue1046(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Reflection.MemberInfo, object?>(values.Length, indexComparer1046);
                foreach (var item in values)
                    result.Add(IndexKey1046(item), IndexValue1046(item));
                return result;
            }
        
            global::System.Reflection.MemberInfo IndexKey1046(global::System.Reflection.MemberInfo member) => member;
            object? IndexValue1046(global::System.Reflection.MemberInfo member) => EditorValueCloner.CloneValue(ComponentFieldSerializer.GetMemberValue(member, component));
        }
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
