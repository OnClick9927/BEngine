using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Method)]
public sealed class DrawGizmoAttribute(GizmoType gizmo) : Attribute
{
    public GizmoType drawOptions { get; } = gizmo;
}
