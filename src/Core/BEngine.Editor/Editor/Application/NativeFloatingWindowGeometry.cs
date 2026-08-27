namespace BEngine.Editor;

internal static class NativeFloatingWindowGeometry
{
    private static readonly Fix64 MinimumReachableWidth = 64;
    private static readonly Fix64 ReachableTitleHeight = 32;

    internal static Rect ConstrainSize(Rect bounds, Vector2 minimumSize, Vector2 maximumSize)
    {
        var minimumWidth = Fix64.Max(1, minimumSize.x);
        var minimumHeight = Fix64.Max(1, minimumSize.y);
        var maximumWidth = Fix64.Max(minimumWidth, maximumSize.x);
        var maximumHeight = Fix64.Max(minimumHeight, maximumSize.y);
        return new Rect(bounds.x, bounds.y,
            Fix64.Clamp(bounds.width, minimumWidth, maximumWidth),
            Fix64.Clamp(bounds.height, minimumHeight, maximumHeight));
    }

    internal static IReadOnlyList<Rect> CurrentWorkAreas()
    {
        try
        {
            return System.Windows.Forms.Screen.AllScreens.Select(screen => screen.WorkingArea)
                .Select(area => new Rect(area.X, area.Y, area.Width, area.Height))
                .ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                           System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    internal static Rect RestoreToVisibleWorkArea(Rect bounds, IReadOnlyList<Rect> workAreas)
    {
        var available = workAreas.Where(IsValid).ToArray();
        if (available.Length == 0 || available.Any(area => HasReachableTitle(bounds, area)))
            return bounds;

        var target = available.MinBy(area => DistanceSquared(bounds.center, area))!;
        var width = Fix64.Max(1, bounds.width);
        var height = Fix64.Max(1, bounds.height);
        var x = Fix64.Clamp(bounds.x, target.x, Fix64.Max(target.x, target.xMax - width));
        var y = Fix64.Clamp(bounds.y, target.y, Fix64.Max(target.y, target.yMax - height));
        return new Rect(x, y, width, height);
    }

    private static bool HasReachableTitle(Rect bounds, Rect workArea)
    {
        var title = new Rect(bounds.x, bounds.y,
            Fix64.Max(1, bounds.width), Fix64.Min(Fix64.Max(1, bounds.height), ReachableTitleHeight));
        var left = Fix64.Max(title.x, workArea.x);
        var right = Fix64.Min(title.xMax, workArea.xMax);
        var top = Fix64.Max(title.y, workArea.y);
        var bottom = Fix64.Min(title.yMax, workArea.yMax);
        return right - left >= Fix64.Min(MinimumReachableWidth, title.width) && bottom > top;
    }

    private static Fix64 DistanceSquared(Vector2 point, Rect area)
    {
        var x = Fix64.Clamp(point.x, area.x, area.xMax);
        var y = Fix64.Clamp(point.y, area.y, area.yMax);
        return (point - new Vector2(x, y)).sqrMagnitude;
    }

    private static bool IsValid(Rect area) => area.width > 0 && area.height > 0;
}
