using System.Reflection;

namespace BEngine;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SerializationIgnoreAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SerializationMemberAttribute : Attribute
{
    public string? Alias { get; set; }
    public int Order { get; set; }
    public bool ApplyNamingConventions { get; set; } = true;
}

public readonly record struct SerializationMemberInfo(bool Ignored, string Name, int Order);

/// <summary>Optional format metadata adapter, evaluated when building a cached member plan.</summary>
public interface ISerializationMetadataProvider
{
    SerializationMemberInfo GetMember(MemberInfo member);
}

public static class SerializationMetadata
{
    private static ISerializationMetadataProvider? _provider;
    private static Func<Type, IEnumerable<MemberInfo>>? _objectMembers;

    internal static void RegisterObjectMembers(Func<Type, IEnumerable<MemberInfo>> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Volatile.Write(ref _objectMembers, source);
    }

    /// <summary>Enumerates persistent fields and properties of an object, including inherited
    /// private serialized fields. Component-root filtering and Inspector presentation are separate policies.
    /// The enumeration does not expose or copy the mutable reflection-cache arrays.</summary>
    public static IEnumerable<MemberInfo> GetObjectMembers(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Volatile.Read(ref _objectMembers)?.Invoke(type) ??
            throw new InvalidOperationException("The engine object member source has not been registered.");
    }

    public static void RegisterProvider(ISerializationMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Volatile.Write(ref _provider, provider);
    }
    public static SerializationMemberInfo GetMember(MemberInfo member) =>
        Volatile.Read(ref _provider)?.GetMember(member) ?? GetDefaultMember(member);

    public static SerializationMemberInfo GetDefaultMember(MemberInfo member)
    {
        var setting = member.GetCustomAttribute<SerializationMemberAttribute>();
        var name = !string.IsNullOrEmpty(setting?.Alias) ? setting.Alias :
            setting is { ApplyNamingConventions: false } ? member.Name : CamelCase(member.Name);
        return new(member.IsDefined(typeof(SerializationIgnoreAttribute), inherit: true), name, setting?.Order ?? 0);
    }
    private static string CamelCase(string name)
    {
        if (name.Length == 0) return name;
        if (name.AsSpan().IndexOfAny('_', '-') >= 0)
        {
            var builder = new System.Text.StringBuilder(name.Length);
            for (var index = 0; index < name.Length; index++)
                builder.Append(name[index] is '_' or '-' && index + 1 < name.Length
                    ? char.ToUpperInvariant(name[++index]) : name[index]);
            name = builder.ToString();
        }
        return name.Length == 0 || !char.IsUpper(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
