using BEngine.Rendering;

namespace BEngine.Editor;

/// <summary>A Scene window GUI pass. Coordinates use the window's GUI space, including its toolbar.</summary>
public sealed class SceneViewToolContext(Rect viewport, RenderCamera camera)
{
    public Rect viewport { get; } = viewport;
    public RenderCamera camera { get; } = camera;
    public bool suppressDefaultTools { get; set; }
    public bool pointerInViewport => viewport.Contains(Event.current.mousePosition);
    public Vector2 WorldToGUI(Vector2 world)
    {
        var point = camera.WorldToViewport(world, Math.Max(1, (int)viewport.width), Math.Max(1, (int)viewport.height));
        return new Vector2(viewport.x + (Fix64)point.X, viewport.y + (Fix64)point.Y);
    }
    public Vector2 GUIToWorld(Vector2 point)
    {
        var units = (Fix64)(1f / camera.PixelsPerUnit(Math.Max(1, (int)viewport.height)));
        return camera.CameraToWorld(new Vector2((point.x - viewport.x - viewport.width / 2) * units,
            (viewport.y + viewport.height / 2 - point.y) * units));
    }
    public void DrawWorldLine(Vector2 from, Vector2 to, Color color, Fix64 thickness)
    {
        if (Event.current.type != EventType.Repaint) return;
        var start = WorldToGUI(from); var end = WorldToGUI(to);
        // Clip before tessellation so a very distant world point cannot allocate unbounded GUI commands.
        var dx = (double)(end.x - start.x); var dy = (double)(end.y - start.y);
        double min = 0, max = 1;
        if (!Clip(-dx, (double)(start.x - viewport.x), ref min, ref max) ||
            !Clip(dx, (double)(viewport.xMax - start.x), ref min, ref max) ||
            !Clip(-dy, (double)(start.y - viewport.y), ref min, ref max) ||
            !Clip(dy, (double)(viewport.yMax - start.y), ref min, ref max)) return;
        var length = Math.Sqrt(dx * dx + dy * dy) * (max - min);
        var size = Fix64.Max(1, thickness);
        if (Math.Abs(dx) < .01 || Math.Abs(dy) < .01)
        {
            var first = new Vector2(start.x + (Fix64)(dx * min), start.y + (Fix64)(dy * min));
            var second = new Vector2(start.x + (Fix64)(dx * max), start.y + (Fix64)(dy * max));
            GUI.DrawRect(new Rect(Fix64.Min(first.x, second.x) - size / 2, Fix64.Min(first.y, second.y) - size / 2,
                Fix64.Max(size, Fix64.Abs(second.x - first.x)), Fix64.Max(size, Fix64.Abs(second.y - first.y))), color);
            return;
        }
        var steps = Math.Clamp((int)Math.Ceiling(length / 2), 1, 4096);
        for (var i = 0; i <= steps; i++)
        {
            var t = min + (max - min) * i / steps;
            GUI.DrawRect(new Rect(start.x + (Fix64)(dx * t) - size / 2,
                start.y + (Fix64)(dy * t) - size / 2, size, size), color);
        }
    }
    private static bool Clip(double p, double q, ref double min, ref double max)
    {
        if (p == 0) return q >= 0;
        var t = q / p;
        if (p < 0) { if (t > max) return false; min = Math.Max(min, t); }
        else { if (t < min) return false; max = Math.Min(max, t); }
        return true;
    }
}
