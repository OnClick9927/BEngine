using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Method)]
public sealed class InitializeOnLoadMethodAttribute : Attribute;
