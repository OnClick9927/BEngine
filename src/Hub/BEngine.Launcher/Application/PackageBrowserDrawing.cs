namespace BEngine.Launcher;

internal static class PackageBrowserDrawing
{
    internal static void Lock(Graphics graphics, Pen pen, float x, float y, float scale)
    {
        pen.Width = scale;
        graphics.DrawArc(pen, x + 2 * scale, y, 6 * scale, 8 * scale, 180, 180);
        graphics.DrawRectangle(pen, x, y + 5 * scale, 10 * scale, 8 * scale);
        graphics.DrawLine(pen, x + 5 * scale, y + 8 * scale, x + 5 * scale, y + 10 * scale);
    }
}
