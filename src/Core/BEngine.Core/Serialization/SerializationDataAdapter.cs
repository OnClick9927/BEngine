using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Serialization;

/// <summary>Declares a detached data representation for a type whose implementation stays opaque.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SerializationDataAdapterAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type adapterType) : Attribute
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type AdapterType { get; } = adapterType;
}

/// <summary>A serialization bridge, selected by the requested CLR type, never by document input.
/// The adapter owns its wire schema; callers use the containing object's public operations.</summary>
public abstract class SerializationDataAdapter
{
    public abstract Type ObjectType { get; }
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    public abstract Type DataType { get; }
    public abstract object CaptureData(object value);
    public abstract object RestoreData(object data);

    private sealed record Entry(SerializationDataAdapter? Adapter);
    private static readonly ConditionalWeakTable<Type, Entry> Adapters = new();

    public static SerializationDataAdapter? Find(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsClass || type == typeof(string) || type == typeof(object) || type.IsArray) return null;
        return Adapters.GetValue(type, static requested =>
        {
            if (requested.GetCustomAttribute<SerializationDataAdapterAttribute>() is not { } attribute)
                return new(null);
            if (Activator.CreateInstance(attribute.AdapterType) is not SerializationDataAdapter adapter ||
                adapter.ObjectType != requested || adapter.DataType == requested || adapter.DataType == typeof(void) ||
                adapter.DataType.GetCustomAttribute<SerializationDataAdapterAttribute>() is not null)
                throw new InvalidOperationException($"Invalid serialization data adapter for '{requested.FullName}'.");
            return new(adapter);
        }).Adapter;
    }
}
