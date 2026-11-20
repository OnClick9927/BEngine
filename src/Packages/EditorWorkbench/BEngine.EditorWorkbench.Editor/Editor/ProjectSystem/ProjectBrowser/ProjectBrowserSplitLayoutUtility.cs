namespace BEngine.Editor;

internal static class ProjectBrowserSplitLayoutUtility
{
    internal const int SeparatorHeight = 2;
    internal const int SeparatorHitHeight = 7;
    internal const int MinimumPaneHeight = 44;

    internal static ProjectBrowserSplitLayout Calculate(Rect bounds, Fix64 preferredPackagesHeight)
    {
        var paneSpace = Fix64.Max(0, bounds.height - SeparatorHeight);
        var (minimum, maximum) = PackagesHeightRange(bounds.height);
        var packagesHeight = Fix64.Clamp(preferredPackagesHeight, minimum, maximum);
        var assetsHeight = Fix64.Max(0, paneSpace - packagesHeight);
        var separator = new Rect(bounds.x, bounds.y + assetsHeight, bounds.width,
            Fix64.Min(SeparatorHeight, Fix64.Max(0, bounds.height - assetsHeight)));
        var packages = new Rect(bounds.x, separator.yMax, bounds.width,
            Fix64.Max(0, bounds.yMax - separator.yMax));
        var hitHeight = Fix64.Min(SeparatorHitHeight, bounds.height);
        var hitY = Fix64.Clamp(separator.y - (hitHeight - separator.height) / 2,
            bounds.y, Fix64.Max(bounds.y, bounds.yMax - hitHeight));
        var hitArea = new Rect(bounds.x, hitY, bounds.width, hitHeight);
        return new ProjectBrowserSplitLayout(
            new Rect(bounds.x, bounds.y, bounds.width, assetsHeight), separator, hitArea, packages);
    }

    internal static Fix64 FromAssetContentHeight(Rect bounds, Fix64 assetsContentHeight)
    {
        var paneSpace = Fix64.Max(0, bounds.height - SeparatorHeight);
        var (minimum, maximum) = PackagesHeightRange(bounds.height);
        return Fix64.Clamp(paneSpace - assetsContentHeight, minimum, maximum);
    }

    internal static Fix64 ResizePackagesHeight(
        Fix64 dragStartHeight,
        Fix64 verticalDragDelta,
        Fix64 availableHeight)
    {
        var (minimum, maximum) = PackagesHeightRange(availableHeight);
        return Fix64.Clamp(dragStartHeight - verticalDragDelta, minimum, maximum);
    }

    private static (Fix64 Minimum, Fix64 Maximum) PackagesHeightRange(Fix64 availableHeight)
    {
        var paneSpace = Fix64.Max(0, availableHeight - SeparatorHeight);
        var minimum = Fix64.Min(MinimumPaneHeight, paneSpace / 2);
        return (minimum, Fix64.Max(minimum, paneSpace - minimum));
    }
}
