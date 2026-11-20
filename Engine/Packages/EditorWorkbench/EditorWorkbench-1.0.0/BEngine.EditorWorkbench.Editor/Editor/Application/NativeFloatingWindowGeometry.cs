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

    internal static IReadOnlyList<Rect> CurrentWorkAreas() => DesktopUtility.Current.GetWorkAreas();

    internal static Rect RestoreToVisibleWorkArea(Rect bounds, IReadOnlyList<Rect> workAreas)
    {
        var available = workAreas.Where(IsValid).ToArray();
        if (available.Length == 0 || Query1041())
            return bounds;
        bool Query1041()
        {
            var querySource1041 = available;
            ArgumentNullException.ThrowIfNull(querySource1041, "source");
            if (querySource1041.GetType() == typeof(global::BEngine.Rect[]) || querySource1041.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Rect>))
            {
                global::System.ReadOnlySpan<global::BEngine.Rect> queryValues1041 = querySource1041.GetType() == typeof(global::BEngine.Rect[]) ? (global::BEngine.Rect[])(object)querySource1041 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Rect>)(object)querySource1041);
                foreach (var area in queryValues1041)
                {
                    if (HasReachableTitle(bounds, area))
                        return true;
                }
            }
            else
            {
                foreach (var area in querySource1041)
                {
                    if (HasReachableTitle(bounds, area))
                        return true;
                }
            }
        
            return false;
        }

        var target = available.MinBy(area => DistanceSquared(bounds.center, area))!;
        var width = Fix64.Max(1, bounds.width);
        var height = Fix64.Max(1, bounds.height);
        var x = Fix64.Clamp(bounds.x, target.x, Fix64.Max(target.x, target.xMax - width));
        var y = Fix64.Clamp(bounds.y, target.y, Fix64.Max(target.y, target.yMax - height));
        return new Rect(x, y, width, height);
    }

    internal static Rect SelectPersistentBounds(
        Rect currentBounds,
        Rect previousBounds,
        IReadOnlyList<Rect> workAreas)
    {
        var available = workAreas.Where(IsValid).ToArray();
        if (available.Length == 0 || Query1792())
            return currentBounds;
        bool Query1792()
        {
            var querySource1792 = available;
            ArgumentNullException.ThrowIfNull(querySource1792, "source");
            if (querySource1792.GetType() == typeof(global::BEngine.Rect[]) || querySource1792.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Rect>))
            {
                global::System.ReadOnlySpan<global::BEngine.Rect> queryValues1792 = querySource1792.GetType() == typeof(global::BEngine.Rect[]) ? (global::BEngine.Rect[])(object)querySource1792 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Rect>)(object)querySource1792);
                foreach (var area in queryValues1792)
                {
                    if (HasReachableTitle(currentBounds, area))
                        return true;
                }
            }
            else
            {
                foreach (var area in querySource1792)
                {
                    if (HasReachableTitle(currentBounds, area))
                        return true;
                }
            }
        
            return false;
        }
        bool __queryResult1901;
        {
            var __querySource1901 = available;
            ArgumentNullException.ThrowIfNull(__querySource1901, "source");
            __queryResult1901 = false;
            foreach (var __queryItem1901 in __querySource1901)
            {
                if ((HasReachableTitle(previousBounds, __queryItem1901)))
                {
                    __queryResult1901 = true;
                    break;
                }
            }
        }
        
        if (__queryResult1901)
            return previousBounds;
        return RestoreToVisibleWorkArea(currentBounds, available);
    }

    internal static Rect CreateUndockedBounds(
        Vector2 screenOrigin,
        Vector2 dockedClientSize,
        Vector2? previousFloatingSize,
        Vector2 minimumSize,
        Vector2 maximumSize,
        IReadOnlyList<Rect> workAreas)
    {
        var desired = previousFloatingSize is { } remembered &&
                      remembered.x > 0 && remembered.y > 0
            ? remembered
            : new Vector2(Fix64.Max(1, dockedClientSize.x * 2),
                Fix64.Max(1, dockedClientSize.y * 2));
        var bounds = ConstrainSize(new Rect(screenOrigin.x, screenOrigin.y, desired.x, desired.y),
            minimumSize, maximumSize);
        var available = workAreas.Where(IsValid).ToArray();
        if (available.Length == 0) return bounds;

        var target = Matches5575();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Rect Matches5575()
        {
            var matchSource5575 = available;
            ArgumentNullException.ThrowIfNull(matchSource5575, "source");
            global::System.ReadOnlySpan<global::BEngine.Rect> matchValues5575 = matchSource5575;
            foreach (var area in matchValues5575)
            {
                if (area.Contains(screenOrigin))
                    return area;
            }
        
            return default!;
        }
        if (!IsValid(target)) target = available.MinBy(area => DistanceSquared(screenOrigin, area));
        var width = Fix64.Min(bounds.width, target.width);
        var height = Fix64.Min(bounds.height, target.height);
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
