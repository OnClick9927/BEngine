using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Class)]
public sealed class InitializeOnLoadAttribute : Attribute;
