using System.Reflection;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CanEditMultipleObjectsAttribute : Attribute;
