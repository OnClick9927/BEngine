using System;
using System.Diagnostics.CodeAnalysis;

namespace BEngine.App.Pref;

/// <summary>Converts one declared preference type. A generated converter can avoid reflection entirely.</summary>
public interface IPrefConverter
{
    string Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);
    object Deserialize([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, string text);
}
