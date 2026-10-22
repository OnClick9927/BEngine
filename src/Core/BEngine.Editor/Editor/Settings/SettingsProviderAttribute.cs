using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SettingsProviderAttribute : Attribute;
