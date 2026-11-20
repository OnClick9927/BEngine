using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.TypeInspectors;

namespace BEngine;

/// <summary>Translates neutral engine attributes while retaining legacy YAML attribute support.</summary>
internal sealed class CoreYamlTypeInspector(ITypeInspector inner) : TypeInspectorSkeleton
{
    private readonly ITypeInspector _inner = new YamlAttributesTypeInspector(new NeutralInspector(inner));
    public override IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container) =>
        _inner.GetProperties(type, container);
    public override string GetEnumName(Type enumType, string name) => _inner.GetEnumName(enumType, name);
    public override string GetEnumValue(object enumValue) => _inner.GetEnumValue(enumValue);
    public override bool HasParseMethod(Type type) => _inner.HasParseMethod(type);
    public override object? Parse(string value, Type type) => _inner.Parse(value, type);

    private sealed class NeutralInspector(ITypeInspector source) : TypeInspectorSkeleton
    {
        public override IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container) =>
            source.GetProperties(type, container).Select(static descriptor => new NeutralDescriptor(descriptor));
        public override string GetEnumName(Type enumType, string name) => source.GetEnumName(enumType, name);
        public override string GetEnumValue(object enumValue) => source.GetEnumValue(enumValue);
        public override bool HasParseMethod(Type type) => source.HasParseMethod(type);
        public override object? Parse(string value, Type type) => source.Parse(value, type);
    }
    private sealed class NeutralDescriptor(IPropertyDescriptor inner) : IPropertyDescriptor
    {
        public string Name => inner.Name;
        public bool AllowNulls => inner.AllowNulls;
        public bool CanWrite => inner.CanWrite;
        public Type Type => inner.Type;
        public Type? TypeOverride { get => inner.TypeOverride; set => inner.TypeOverride = value; }
        public int Order { get => inner.Order; set => inner.Order = value; }
        public ScalarStyle ScalarStyle { get => inner.ScalarStyle; set => inner.ScalarStyle = value; }
        public bool Required => inner.Required;
        public Type? ConverterType => inner.ConverterType;
        public IObjectDescriptor Read(object target) => inner.Read(target);
        public void Write(object target, object? value) => inner.Write(target, value);
        public T? GetCustomAttribute<T>() where T : Attribute
        {
            var attribute = inner.GetCustomAttribute<T>();
            if (attribute is not null) return attribute;
            if (typeof(T) == typeof(YamlIgnoreAttribute) &&
                inner.GetCustomAttribute<SerializationIgnoreAttribute>() is not null)
                return (T)(Attribute)new YamlIgnoreAttribute();
            if (typeof(T) == typeof(YamlMemberAttribute) &&
                inner.GetCustomAttribute<SerializationMemberAttribute>() is { } member)
                return (T)(Attribute)new YamlMemberAttribute
                {
                    Alias = member.Alias!, Order = member.Order, ApplyNamingConventions = member.ApplyNamingConventions
                };
            return null;
        }
    }
}
