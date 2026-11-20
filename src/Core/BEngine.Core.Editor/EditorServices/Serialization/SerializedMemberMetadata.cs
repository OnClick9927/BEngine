using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal sealed class SerializedMemberMetadata
{
    private static readonly ConditionalWeakTable<MemberInfo, SerializedMemberMetadata> Cache = new();
    private static readonly SerializedMemberMetadata Empty = new(null);

    internal string? DisplayName { get; }
    internal string Tooltip { get; }
    internal Attribute[] Attributes { get; }
    internal PropertyAttribute[] PropertyAttributes { get; }
    internal bool IsHidden { get; }

    private SerializedMemberMetadata(MemberInfo? member)
    {
        Attributes = member?.GetCustomAttributes(inherit: true).OfType<Attribute>().ToArray() ?? [];
        PropertyAttributes = Attributes.Length == 0
            ? []
            : Attributes.OfType<PropertyAttribute>().OrderBy(attribute => attribute.order).ToArray();
        InspectorNameAttribute? displayName = null;
        TooltipAttribute? tooltip = null;
        foreach (var attribute in Attributes)
        {
            if (attribute is InspectorNameAttribute name && displayName is null) displayName = name;
            if (attribute is TooltipAttribute hint && tooltip is null) tooltip = hint;
            if (attribute is HideInInspectorAttribute) IsHidden = true;
        }
        DisplayName = displayName?.displayName;
        Tooltip = tooltip?.tooltip ?? string.Empty;
    }

    internal static SerializedMemberMetadata For(MemberInfo? member) =>
        member is null ? Empty : Cache.GetValue(member, static key => new SerializedMemberMetadata(key));

    internal static void Warmup()
    {
        foreach (var type in RuntimeTypeCache.GetAllTypes())
            if (typeof(BObject).IsAssignableFrom(type))
                foreach (var member in SerializationMetadata.GetObjectMembers(type)) _ = For(member);
    }

    internal bool HasAttribute<T>() where T : Attribute
    {
        foreach (var attribute in Attributes)
            if (attribute is T) return true;
        return false;
    }
}
