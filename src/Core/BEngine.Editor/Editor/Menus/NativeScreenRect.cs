namespace BEngine.Editor;

internal readonly record struct NativeScreenRect(int Left, int Top, int Right, int Bottom)
{
    internal bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    internal static NativeScreenRect Around(Vector2 point, int radius = 2)
    {
        var x = (int)point.x;
        var y = (int)point.y;
        return new NativeScreenRect(x - radius, y - radius, x + radius + 1, y + radius + 1);
    }

    internal static NativeScreenRect From(Rect rect) => new(
        (int)rect.x,
        (int)rect.y,
        (int)Math.Ceiling((double)rect.xMax),
        (int)Math.Ceiling((double)rect.yMax));
}
