using System.Reflection;

namespace BEngine.Editor;

internal readonly record struct GizmoDrawerCommand(
    GizmoType DrawOptions,
    Action<Component, GizmoType> Callback,
    string Feature,
    MethodInfo Method);
