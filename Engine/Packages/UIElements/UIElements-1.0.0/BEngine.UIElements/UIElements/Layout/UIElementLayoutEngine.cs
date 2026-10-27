using System.Globalization;
using BEngine.Serialization;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public static partial class UIElementLayoutEngine
{
    private static readonly ConditionalWeakTable<VisualElement, LayoutCache> Caches = new();
    public static long cacheHits { get; private set; }
    public static int lastRebuiltNodeCount { get; private set; }
    public static int lastReusedNodeCount { get; private set; }
    static UIElementLayoutEngine() => Diagnostics.RuntimeDiagnosticRegistry.Register("budget.ui", "UI layout",
        new("rebuilt", "Last rebuilt nodes", Diagnostics.RuntimeDiagnosticUnit.Number, () => lastRebuiltNodeCount),
        new("cacheHits", "Layout cache hits", Diagnostics.RuntimeDiagnosticUnit.Number, () => cacheHits));
    public static IReadOnlyList<UIElementLayout> Calculate(
        VisualElement root,
        int viewportWidth,
        int viewportHeight,
        Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var resolvedScale = scale ?? Fix64.One;
        if (resolvedScale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var styleRoot = root;
        while (styleRoot.parent is not null) styleRoot = styleRoot.parent;
        var cache = Caches.GetValue(root, static _ => new LayoutCache());
        if (cache.Version == root.layoutVersion && cache.TreeVersion == styleRoot.layoutVersion && ReferenceEquals(cache.StyleRoot, styleRoot) &&
            cache.Width == viewportWidth && cache.Height == viewportHeight && cache.Scale == resolvedScale && cache.Result is not null)
        {
            cacheHits++; lastRebuiltNodeCount = lastReusedNodeCount = 0;
            cache.Result = cache.Result.WithCurrentScrollOffsets();
            return cache.Result;
        }
        UIElementLayoutSnapshot result;
        try { result = CalculateFull(root, viewportWidth, viewportHeight, resolvedScale, cache.Measurements, cache.Scratch, cache.Result, cache.Fragments); }
        finally { cache.Measurements.Clear(); }
        cache.Version = root.layoutVersion; cache.Width = viewportWidth; cache.Height = viewportHeight; cache.Scale = resolvedScale;
        cache.TreeVersion = styleRoot.layoutVersion; cache.StyleRoot = styleRoot;
        cache.Result = result; return result;
    }

    /// <summary>Reference full-tree layout for diagnostics and explicit cache invalidation checks.</summary>
    public static IReadOnlyList<UIElementLayout> CalculateUncached(VisualElement root, int viewportWidth,
        int viewportHeight, Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var resolvedScale = scale ?? Fix64.One;
        if (resolvedScale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        return CalculateFull(root, viewportWidth, viewportHeight, resolvedScale);
    }

    private static UIElementLayoutSnapshot CalculateFull(VisualElement root, int viewportWidth,
        int viewportHeight, Fix64 resolvedScale, MeasurementContext? measurements = null, LayoutScratch? scratch = null,
        UIElementLayoutSnapshot? previous = null, FragmentCache? fragments = null)
    {
        // A provider/style callback can recursively request layout. Its complete
        // reference pass must not overwrite the outer pass's fragment metadata.
        if (fragments?.InUse == true) fragments = null;
        if (fragments is not null) fragments.InUse = true;
        try
        {
        root.SetStyleViewport((Fix64)viewportWidth / resolvedScale, (Fix64)viewportHeight / resolvedScale);
        root.EnsureStyleSheets();
        root.style.ResolveLengths(root.styleViewportWidth, root.styleViewportHeight,
            root.styleViewportWidth, root.styleViewportHeight, 16);
        fragments?.Begin(root, viewportWidth, viewportHeight, resolvedScale, previous);
        if (scratch is null || scratch.InUse) scratch = new LayoutScratch();
        scratch.InUse = true;
        var result = scratch.Layouts;
        var scrollers = scratch.Scrollers;
        var scrollAncestors = scratch.ScrollAncestors;
        try
        {
        Layout(root, new UIElementRect(0, 0, Math.Max(1, viewportWidth), Math.Max(1, viewportHeight)),
            resolvedScale, result, scrollers, scrollAncestors, -1, measurements, fragments);
        lastReusedNodeCount = fragments?.ReusedNodes ?? 0;
        lastRebuiltNodeCount = result.Count - lastReusedNodeCount;
        var snapshot = UIElementLayoutSnapshot.Create(result, scrollers, scrollAncestors, resolvedScale, previous);
        fragments?.Complete(snapshot);
        return snapshot;
        }
        finally { result.Clear(); scrollers.Clear(); scrollAncestors.Clear(); scratch.InUse = false; }
        }
        finally { fragments?.End(); }
    }

    private sealed class LayoutScratch
    {
        internal readonly List<UIElementLayout> Layouts = [];
        internal readonly List<UIElementLayoutSnapshot.ScrollBinding> Scrollers = [];
        internal readonly List<int> ScrollAncestors = [];
        internal bool InUse;
    }

    private sealed class LayoutCache
    {
        internal long Version = -1;
        internal long TreeVersion;
        internal VisualElement? StyleRoot;
        internal int Width, Height;
        internal Fix64 Scale;
        internal UIElementLayoutSnapshot? Result;
        internal readonly MeasurementContext Measurements = new();
        internal readonly LayoutScratch Scratch = new();
        internal readonly FragmentCache Fragments = new();
    }

    private static void Layout(
        VisualElement element,
        UIElementRect rect,
        Fix64 scale,
        List<UIElementLayout> output,
        List<UIElementLayoutSnapshot.ScrollBinding> scrollers,
        List<int> scrollAncestors,
        int scrollAncestor, MeasurementContext? measurements, FragmentCache? fragments)
    {
        if (!element.visible || element.style.display == DisplayStyle.None) return;
        rect = rect with {
            X = rect.X + element.style.ResolveTranslation(true, rect.Width / scale, element.styleViewportWidth, element.styleViewportHeight, EffectiveFontSize(element)) * scale,
            Y = rect.Y + element.style.ResolveTranslation(false, rect.Height / scale, element.styleViewportWidth, element.styleViewportHeight, EffectiveFontSize(element)) * scale };
        output.Add(new UIElementLayout(element, rect));
        scrollAncestors.Add(scrollAncestor);
        if (element is ScrollView scrollElement)
        {
            var index = scrollers.Count;
            scrollers.Add(new(scrollElement, scrollAncestor, ScaleScroll(scrollElement.scrollOffset, scale)));
            scrollAncestor = index;
        }
        var visibleChildCount = 0;
        var flowChildCount = 0;
        var children = element.Children;
        for (var childIndex = 0; childIndex < children.Count; childIndex++)
        {
            var child = children[childIndex];
            if (!child.visible || child.style.display == DisplayStyle.None) continue;
            visibleChildCount++;
            if (child.style.position != Position.Absolute) flowChildCount++;
        }
        if (visibleChildCount == 0) return;

        var left = Scale(element.style.paddingLeft, scale);
        var top = Scale(element.style.paddingTop, scale);
        var right = Scale(element.style.paddingRight, scale);
        var bottom = Scale(element.style.paddingBottom, scale);
        var content = new UIElementRect(rect.X + left, rect.Y + top,
            Fix64.Max(0, rect.Width - left - right), Fix64.Max(0, rect.Height - top - bottom));
        var horizontal = element.style.flexDirection == FlexDirection.Row;
        var availableMain = horizontal ? content.Width : content.Height;
        var contentWidth = content.Width / scale; var contentHeight = content.Height / scale;
        for (var childIndex = 0; childIndex < children.Count; childIndex++)
            ResolveChildLengths(children[childIndex], contentWidth, contentHeight);
        var spacing = Scale(horizontal ? element.style.columnGap : element.style.rowGap, scale);
        var fixedMain = spacing * Math.Max(0, flowChildCount - 1);
        var totalGrow = Fix64.Zero;
        for (var childIndex = 0; childIndex < children.Count; childIndex++)
        {
            var child = children[childIndex];
            if (!child.visible || child.style.display == DisplayStyle.None ||
                child.style.position == Position.Absolute) continue;
            fixedMain += MainMargins(child, horizontal, scale);
            var explicitSize = MainSize(child, horizontal, scale);
            if (explicitSize > 0) fixedMain += explicitSize;
            else if (child.style.flexGrow <= 0) fixedMain += DefaultMainSize(child, horizontal, scale, measurements);
            else totalGrow += (Fix64)child.style.flexGrow;
        }

        var remaining = Fix64.Max(0, availableMain - fixedMain);
        var allocatedGrow = totalGrow > 0 ? remaining : Fix64.Zero;
        var unusedMain = Fix64.Max(0, remaining - allocatedGrow);
        var justify = element.style.justifyContent;
        if (element.ClassListContains("html-inline") && justify == Justify.FlexStart)
        {
            var alignment = element.style.textAlign;
            for (var current = element.parent; alignment.Length == 0 && current is not null; current = current.parent)
                alignment = current.style.textAlign;
            if (alignment == "center") justify = Justify.Center;
            else if (alignment == "right") justify = Justify.FlexEnd;
        }
        var mainOffset = justify switch
        {
            Justify.Center => unusedMain / 2,
            Justify.FlexEnd => unusedMain,
            _ => Fix64.Zero
        };
        var gap = spacing + (element.style.justifyContent == Justify.SpaceBetween && flowChildCount > 1
            ? unusedMain / (flowChildCount - 1) : Fix64.Zero);
        var cursor = (horizontal ? content.X : content.Y) + mainOffset;
        for (var childIndex = 0; childIndex < children.Count; childIndex++)
        {
            var child = children[childIndex];
            if (!child.visible || child.style.display == DisplayStyle.None ||
                child.style.position == Position.Absolute) continue;
            var leadingMargin = Scale(horizontal ? child.style.marginLeft : child.style.marginTop, scale);
            var trailingMargin = Scale(horizontal ? child.style.marginRight : child.style.marginBottom, scale);
            cursor += leadingMargin;
            var main = MainSize(child, horizontal, scale);
            if (main <= 0)
            {
                main = child.style.flexGrow > 0 && totalGrow > 0
                    ? remaining * (Fix64)child.style.flexGrow / totalGrow
                    : DefaultMainSize(child, horizontal, scale, measurements);
            }

            main = Constrain(child, main, horizontal, scale);
            var crossLeading = Scale(horizontal ? child.style.marginTop : child.style.marginLeft, scale);
            var crossTrailing = Scale(horizontal ? child.style.marginBottom : child.style.marginRight, scale);
            var crossAvailable = (horizontal ? content.Height : content.Width) - crossLeading - crossTrailing;
            var cross = CrossSize(child, horizontal, scale);
            if (cross <= 0)
            {
                cross = element.style.alignItems == Align.Stretch
                    ? Fix64.Max(0, crossAvailable)
                    : DefaultCrossSize(child, horizontal, scale, crossAvailable, measurements);
            }
            cross = Constrain(child, cross, !horizontal, scale);
            var crossOffset = element.style.alignItems switch
            {
                Align.Center => Fix64.Max(0, crossAvailable - cross) / 2,
                Align.FlexEnd => Fix64.Max(0, crossAvailable - cross),
                _ => Fix64.Zero
            };
            var childRect = horizontal
                ? new UIElementRect(cursor, content.Y + crossLeading + crossOffset, main, cross)
                : new UIElementRect(content.X + crossLeading + crossOffset, cursor, cross, main);
            if (element is ScrollView scroll && scroll.scrollOffset > 0)
                childRect = childRect with { Y = childRect.Y - ScaleScroll(scroll.scrollOffset, scale) };
            Layout(child, childRect, scale, output, scrollers, scrollAncestors, scrollAncestor, measurements, fragments);
            cursor += main + trailingMargin + gap;
        }

        for (var childIndex = 0; childIndex < children.Count; childIndex++)
        {
            var child = children[childIndex];
            if (!child.visible || child.style.display == DisplayStyle.None ||
                child.style.position != Position.Absolute) continue;
            var absoluteLeft = child.style.left * scale;
            var absoluteTop = child.style.top * scale;
            var absoluteRight = child.style.right * scale;
            var absoluteBottom = child.style.bottom * scale;
            var width = MainSize(child, horizontal: true, scale);
            var height = MainSize(child, horizontal: false, scale);
            if (width <= 0 && absoluteLeft is { } leftValue && absoluteRight is { } rightValue)
                width = Fix64.Max(0, content.Width - leftValue - rightValue);
            if (height <= 0 && absoluteTop is { } topValue && absoluteBottom is { } bottomValue)
                height = Fix64.Max(0, content.Height - topValue - bottomValue);
            if (width <= 0) width = DefaultMainSize(child, horizontal: true, scale, measurements);
            if (height <= 0) height = DefaultMainSize(child, horizontal: false, scale, measurements);
            width = Constrain(child, width, true, scale);
            height = Constrain(child, height, false, scale);
            var x = absoluteLeft is { } leftOffset
                ? content.X + leftOffset
                : absoluteRight is { } rightOffset ? content.X + content.Width - rightOffset - width : content.X;
            var y = absoluteTop is { } topOffset
                ? content.Y + topOffset
                : absoluteBottom is { } bottomOffset ? content.Y + content.Height - bottomOffset - height : content.Y;
            if (element is ScrollView scroll) y -= ScaleScroll(scroll.scrollOffset, scale);
            var childRect = new UIElementRect(x, y, width, height);
            var independent = fragments is not null && ReferenceEquals(element, fragments.Root) && scrollAncestor < 0;
            if (independent && fragments!.TryAppend(child, childRect, output, scrollAncestors)) continue;
            var start = output.Count;
            Layout(child, childRect, scale, output, scrollers, scrollAncestors, scrollAncestor, measurements, fragments);
            if (independent) fragments!.Record(child, childRect, start, output);
        }
    }

    private static Fix64 MainSize(VisualElement element, bool horizontal, Fix64 scale) =>
        Scale(horizontal ? element.style.width : element.style.height, scale);

    private static Fix64 CrossSize(VisualElement element, bool horizontal, Fix64 scale) =>
        Scale(horizontal ? element.style.height : element.style.width, scale);

    private static Fix64 MainMargins(VisualElement element, bool horizontal, Fix64 scale) =>
        Scale(horizontal
            ? element.style.marginLeft + element.style.marginRight
            : element.style.marginTop + element.style.marginBottom, scale);

    private static Fix64 DefaultMainSize(VisualElement element, bool horizontal, Fix64 scale, MeasurementContext? measurements)
    {
        if (measurements is null || !measurements.IsStable(element))
            return MeasureDefaultMainSize(element, horizontal, scale, measurements);
        var font = EffectiveFontSize(element);
        if (measurements.TryGet(element, horizontal, font, out var cached)) return cached;
        var size = MeasureDefaultMainSize(element, horizontal, scale, measurements);
        measurements.Store(element, horizontal, font, size); return size;
    }

    private static Fix64 MeasureDefaultMainSize(VisualElement element, bool horizontal, Fix64 scale, MeasurementContext? measurements)
    {
        var hasVisibleChildren = false;
        var children = element.Children;
        for (var childIndex = 0; childIndex < children.Count; childIndex++)
        {
            var child = children[childIndex];
            if (!child.visible || child.style.display == DisplayStyle.None || child.style.position == Position.Absolute) continue;
            hasVisibleChildren = true;
            break;
        }
        if (hasVisibleChildren)
        {
            var padding = Scale(horizontal
                ? element.style.paddingLeft + element.style.paddingRight
                : element.style.paddingTop + element.style.paddingBottom, scale);
            var childrenFollowRequestedAxis = horizontal ==
                                              (element.style.flexDirection == FlexDirection.Row);
            var contentSize = Fix64.Zero;
            var count = 0;
            for (var childIndex = 0; childIndex < children.Count; childIndex++)
            {
                var child = children[childIndex];
                if (!child.visible || child.style.display == DisplayStyle.None || child.style.position == Position.Absolute) continue;
                ResolveChildLengths(child, element.style.width > 0 ? element.style.width : element.styleViewportWidth,
                    element.style.height > 0 ? element.style.height : element.styleViewportHeight);
                var childSize = MainSize(child, horizontal, scale);
                if (childSize <= 0) childSize = DefaultMainSize(child, horizontal, scale, measurements);
                childSize = Constrain(child, childSize, horizontal, scale);
                count++;
                var desired = MainMargins(child, horizontal, scale) + childSize;
                contentSize = childrenFollowRequestedAxis
                    ? contentSize + desired
                    : Fix64.Max(contentSize, desired);
            }
            if (childrenFollowRequestedAxis)
                contentSize += Scale(horizontal ? element.style.columnGap : element.style.rowGap, scale) * Math.Max(0, count - 1);
            return padding + contentSize;
        }

        if (element.sourceTag.Length > 0 && element is TextElement htmlText)
        {
            var font = EffectiveFontSize(element);
            return horizontal
                ? UIRenderListBuilder.MeasureTextWidth(htmlText.text, font, element) * scale + Scale(element.style.paddingLeft + element.style.paddingRight, scale)
                : Scale(font * 14 / 10 + element.style.paddingTop + element.style.paddingBottom, scale);
        }
        if (horizontal)
        {
            var width = element switch
            {
                Image => 100,
                SearchField => 180,
                DropdownField => 110,
                TextField or FloatField or IntegerField or ColorField or Slider => 140,
                Toggle toggle => Math.Clamp(18 + ApproximateTextWidth(toggle.label), 46, 160),
                TextElement text => Math.Clamp(18 + ApproximateTextWidth(text.text), 24, 180),
                _ => 32
            };
            return Scale(width, scale);
        }
        return Scale(element switch
        {
            Label => 24,
            Button => 32,
            TextField => 28,
            Toggle => 24,
            Slider => 28,
            Image => 100,
            _ => 32
        }, scale);
    }

    private static Fix64 DefaultCrossSize(
        VisualElement element,
        bool horizontal,
        Fix64 scale,
        Fix64 available, MeasurementContext? measurements) => Fix64.Min(available, horizontal
        ? DefaultMainSize(element, horizontal: false, scale, measurements)
        : DefaultMainSize(element, horizontal: true, scale, measurements));

    private static Fix64 Scale(Fix64 value, Fix64 scale) => value * scale;

    // Scrolling controls retain float input compatibility at this boundary.
    private static Fix64 ScaleScroll(float value, Fix64 scale) => float.IsFinite(value) ? (Fix64)value * scale : Fix64.Zero;

    private static Fix64 Constrain(VisualElement element, Fix64 size, bool horizontal, Fix64 scale)
    {
        var minimum = horizontal ? element.style.minWidth : element.style.minHeight;
        var maximum = horizontal ? element.style.maxWidth : element.style.maxHeight;
        size = Fix64.Max(size, Scale(minimum, scale));
        if (maximum is { } limit) size = Fix64.Max(Scale(minimum, scale), Fix64.Min(size, Scale(limit, scale)));
        return Fix64.Max(0, size);
    }

    private static void ResolveChildLengths(VisualElement child, Fix64 width, Fix64 height) =>
        child.style.ResolveLengths(width, height, child.styleViewportWidth, child.styleViewportHeight,
            EffectiveFontSize(child.parent));

    internal static Fix64 EffectiveFontSize(VisualElement? element)
    {
        for (var current = element; current is not null; current = current.parent)
            if (current.style.fontSize > 0) return current.style.fontSize;
        return 14;
    }

    private static int ApproximateTextWidth(string text)
    {
        var width = 0;
        foreach (var character in text) width += character > 0xFF ? 14 : 7;
        return width;
    }
}
