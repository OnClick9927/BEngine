using BEngine.Editor;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal class BaseSceneGizmoProbe : Component
{
    internal int DrawCalls { get; private set; }
    internal int SelectedCalls { get; private set; }

    public override void OnDrawGizmos()
    {
        DrawCalls++;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + Vector2.right);
    }

    public override void OnDrawGizmosSelected()
    {
        SelectedCalls++;
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(transform.position, transform.position + Vector2.up);
    }
}

internal sealed class DerivedSceneGizmoProbe : BaseSceneGizmoProbe;

internal static class SceneGizmoDrawerProbe
{
    internal static int Calls { get; private set; }
    internal static int SelectedCalls { get; private set; }
    internal static int NonSelectedCalls { get; private set; }
    internal static bool ColorWasReset { get; private set; } = true;

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    public static void Draw(BaseSceneGizmoProbe component, GizmoType state)
    {
        Calls++;
        if ((state & GizmoType.Selected) != 0) SelectedCalls++;
        if ((state & GizmoType.NonSelected) != 0) NonSelectedCalls++;
        ColorWasReset &= Gizmos.color.Equals(Color.white);
        Gizmos.DrawLine(component.transform.position, component.transform.position + Vector2.left);
    }

    internal static void Reset()
    {
        Calls = 0;
        SelectedCalls = 0;
        NonSelectedCalls = 0;
        ColorWasReset = true;
    }
}
