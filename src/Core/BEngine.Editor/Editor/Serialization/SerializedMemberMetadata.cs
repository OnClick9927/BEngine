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
        PropertyAttributes = Attributes.OfType<PropertyAttribute>().OrderBy(attribute => attribute.order).ToArray();
        DisplayName = Attributes.OfType<InspectorNameAttribute>().FirstOrDefault()?.displayName;
        Tooltip = Attributes.OfType<TooltipAttribute>().FirstOrDefault()?.tooltip ?? string.Empty;
        IsHidden = Attributes.Any(attribute => attribute is HideInInspectorAttribute);
    }

    internal static SerializedMemberMetadata For(MemberInfo? member) =>
        member is null ? Empty : Cache.GetValue(member, static key => new SerializedMemberMetadata(key));

    internal static void Warmup()
    {
        foreach (var type in RuntimeTypeCache.GetAllTypes().Where(type => typeof(BObject).IsAssignableFrom(type)))
        foreach (var member in RuntimeTypeCache.GetInstanceMembers(type))
            _ = For(member);
    }

    internal bool HasAttribute<T>() where T : Attribute => Attributes.Any(attribute => attribute is T);
}
