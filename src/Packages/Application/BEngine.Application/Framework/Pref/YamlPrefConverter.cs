using System;
using System.Diagnostics.CodeAnalysis;

namespace BEngine.App.Pref;

/// <summary>Uses the engine's YAML settings and converters. Preserve nested DTO members in trimmed/AOT players,
/// or supply a generated IPrefConverter for application-specific data.</summary>
public sealed class YamlPrefConverter : IPrefConverter
{
    public string Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);
        if (value.GetType() != type)
            throw new ArgumentException("YAML preferences must use their concrete declared type. Use a custom converter for polymorphic data.", nameof(value));
        return YamlUtility.Serialize(value);
    }
    public object Deserialize([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, string text) =>
        YamlUtility.Deserialize(text, type);
}
