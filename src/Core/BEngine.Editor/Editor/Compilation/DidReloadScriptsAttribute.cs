using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Method)]
public sealed class DidReloadScriptsAttribute(int callbackOrder = 0) : Attribute
{
    public int callbackOrder { get; } = callbackOrder;
}
