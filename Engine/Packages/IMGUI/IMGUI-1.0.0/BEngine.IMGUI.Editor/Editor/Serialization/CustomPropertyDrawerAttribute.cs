using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CustomPropertyDrawerAttribute(Type type, bool useForChildren = false) : Attribute
{
    public Type type { get; } = type;
    public bool useForChildren { get; } = useForChildren;
}
