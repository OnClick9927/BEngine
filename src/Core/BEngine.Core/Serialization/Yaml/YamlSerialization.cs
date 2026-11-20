using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BEngine;

/// <summary>Default text serialization support used by platform and editor hosts.</summary>
public static class YamlSerialization
{
    private static readonly YamlTextCodec Codec = new();
    private static readonly MetadataProvider Metadata = new();
    public static void Initialize()
    {
        TextDataSerialization.RegisterCodec(Codec);
        SerializationMetadata.RegisterProvider(Metadata);
    }
    private sealed class MetadataProvider : ISerializationMetadataProvider
    {
        public SerializationMemberInfo GetMember(MemberInfo member)
        {
            var basic = SerializationMetadata.GetDefaultMember(member);
            var setting = member.GetCustomAttribute<YamlMemberAttribute>();
            var name = !string.IsNullOrEmpty(setting?.Alias) ? setting.Alias :
                setting is { ApplyNamingConventions: false } ? member.Name :
                setting is not null ? CamelCaseNamingConvention.Instance.Apply(member.Name) : basic.Name;
            return new(basic.Ignored || member.IsDefined(typeof(YamlIgnoreAttribute), inherit: true),
                name, setting?.Order ?? basic.Order);
        }
    }
}
