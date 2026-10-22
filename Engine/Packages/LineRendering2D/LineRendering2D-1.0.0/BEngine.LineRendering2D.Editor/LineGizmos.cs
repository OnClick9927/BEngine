namespace BEngine.Editor;

internal static class LineGizmos
{
    [DrawGizmo(GizmoType.Selected)]
    private static void DrawLine(LineRenderer2D line, GizmoType state)
    {
        if (line.positionCount < 2) return;
        Gizmos.color = Color.white;
        var previous = ResolveLinePoint(line, 0);
        for (var index = 1; index < line.positionCount; index++)
        {
            var current = ResolveLinePoint(line, index);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }
        if (line.loop && line.positionCount > 2)
            Gizmos.DrawLine(previous, ResolveLinePoint(line, 0));
    }

    private static Vector2 ResolveLinePoint(LineRenderer2D line, int index)
    {
        var point = line.GetPosition(index);
        return line.useWorldSpace ? point : line.transform.TransformPoint(point);
    }

}
