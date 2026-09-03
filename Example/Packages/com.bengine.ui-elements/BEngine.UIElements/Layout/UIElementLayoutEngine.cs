using System.Globalization;
using BEngine.Serialization;

namespace BEngine.UIElements;

public static class UIElementLayoutEngine
{
    public static IReadOnlyList<UIElementLayout> Calculate(
        VisualElement root,
        int viewportWidth,
        int viewportHeight,
        Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var result = new List<UIElementLayout>();
        Layout(root, new UIElementRect(0, 0, Math.Max(1, viewportWidth), Math.Max(1, viewportHeight)),
            scale ?? Fix64.One, result);
        return result;
    }

    private static void Layout(
        VisualElement element,
        UIElementRect rect,
        Fix64 scale,
        ICollection<UIElementLayout> output)
    {
        if (!element.visible || element.style.display == DisplayStyle.None) return;
        output.Add(new UIElementLayout(element, rect));
        var visibleChildren = element.Children.Where(child => child.visible && child.style.display != DisplayStyle.None).ToArray();
        var children = visibleChildren.Where(child => child.style.position != Position.Absolute).ToArray();
        var absoluteChildren = visibleChildren.Where(child => child.style.position == Position.Absolute).ToArray();
        if (visibleChildren.Length == 0) return;

        var left = Scale(element.style.paddingLeft, scale);
        var top = Scale(element.style.paddingTop, scale);
        var right = Scale(element.style.paddingRight, scale);
        var bottom = Scale(element.style.paddingBottom, scale);
        var content = new UIElementRect(rect.X + left, rect.Y + top,
            Fix64.Max(0, rect.Width - left - right), Fix64.Max(0, rect.Height - top - bottom));
        var horizontal = element.style.flexDirection == FlexDirection.Row;
        var availableMain = horizontal ? content.Width : content.Height;
        var fixedMain = Fix64.Zero;
        var totalGrow = Fix64.Zero;
        foreach (var child in children)
        {
            fixedMain += MainMargins(child, horizontal, scale);
            var explicitSize = MainSize(child, horizontal, scale);
            if (explicitSize > 0) fixedMain += explicitSize;
            else if (child.style.flexGrow <= 0) fixedMain += DefaultMainSize(child, horizontal, scale);
            else totalGrow += (Fix64)child.style.flexGrow;
        }

        var remaining = Fix64.Max(0, availableMain - fixedMain);
        var allocatedGrow = totalGrow > 0 ? remaining : Fix64.Zero;
        var unusedMain = Fix64.Max(0, remaining - allocatedGrow);
        var mainOffset = element.style.justifyContent switch
        {
            Justify.Center => unusedMain / 2,
            Justify.FlexEnd => unusedMain,
            _ => Fix64.Zero
        };
        var gap = element.style.justifyContent == Justify.SpaceBetween && children.Length > 1
            ? unusedMain / (children.Length - 1)
            : Fix64.Zero;
        var cursor = (horizontal ? content.X : content.Y) + mainOffset;
        foreach (var child in children)
        {
            var leadingMargin = Scale(horizontal ? child.style.marginLeft : child.style.marginTop, scale);
            var trailingMargin = Scale(horizontal ? child.style.marginRight : child.style.marginBottom, scale);
            cursor += leadingMargin;
            var main = MainSize(child, horizontal, scale);
            if (main <= 0)
            {
                main = child.style.flexGrow > 0 && totalGrow > 0
                    ? remaining * (Fix64)child.style.flexGrow / totalGrow
                    : DefaultMainSize(child, horizontal, scale);
            }

            var crossLeading = Scale(horizontal ? child.style.marginTop : child.style.marginLeft, scale);
            var crossTrailing = Scale(horizontal ? child.style.marginBottom : child.style.marginRight, scale);
            var crossAvailable = (horizontal ? content.Height : content.Width) - crossLeading - crossTrailing;
            var cross = CrossSize(child, horizontal, scale);
            if (cross <= 0)
            {
                cross = element.style.alignItems == Align.Stretch
                    ? Fix64.Max(0, crossAvailable)
                    : DefaultCrossSize(child, horizontal, scale, crossAvailable);
            }
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
                childRect = childRect with { Y = childRect.Y - (Fix64)scroll.scrollOffset };
            Layout(child, childRect, scale, output);
            cursor += main + trailingMargin + gap;
        }

        foreach (var child in absoluteChildren)
        {
            var absoluteLeft = float.IsNaN(child.style.left) ? (Fix64?)null : Scale(child.style.left, scale);
            var absoluteTop = float.IsNaN(child.style.top) ? (Fix64?)null : Scale(child.style.top, scale);
            var absoluteRight = float.IsNaN(child.style.right) ? (Fix64?)null : Scale(child.style.right, scale);
            var absoluteBottom = float.IsNaN(child.style.bottom) ? (Fix64?)null : Scale(child.style.bottom, scale);
            var width = MainSize(child, horizontal: true, scale);
            var height = MainSize(child, horizontal: false, scale);
            if (width <= 0 && absoluteLeft is { } leftValue && absoluteRight is { } rightValue)
                width = Fix64.Max(0, content.Width - leftValue - rightValue);
            if (height <= 0 && absoluteTop is { } topValue && absoluteBottom is { } bottomValue)
                height = Fix64.Max(0, content.Height - topValue - bottomValue);
            if (width <= 0) width = DefaultMainSize(child, horizontal: true, scale);
            if (height <= 0) height = DefaultMainSize(child, horizontal: false, scale);
            var x = absoluteLeft is { } leftOffset
                ? content.X + leftOffset
                : content.X + content.Width - (absoluteRight ?? Fix64.Zero) - width;
            var y = absoluteTop is { } topOffset
                ? content.Y + topOffset
                : content.Y + content.Height - (absoluteBottom ?? Fix64.Zero) - height;
            Layout(child, new UIElementRect(x, y, width, height), scale, output);
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

    private static Fix64 DefaultMainSize(VisualElement element, bool horizontal, Fix64 scale)
    {
        var visibleChildren = element.Children
            .Where(child => child.visible && child.style.display != DisplayStyle.None)
            .ToArray();
        if (visibleChildren.Length > 0)
        {
            var padding = Scale(horizontal
                ? element.style.paddingLeft + element.style.paddingRight
                : element.style.paddingTop + element.style.paddingBottom, scale);
            var childrenFollowRequestedAxis = horizontal ==
                                              (element.style.flexDirection == FlexDirection.Row);
            var contentSize = childrenFollowRequestedAxis ? visibleChildren.Aggregate(Fix64.Zero, (current, child) =>
            {
                var childSize = MainSize(child, horizontal, scale);
                if (childSize <= 0) childSize = DefaultMainSize(child, horizontal, scale);
                return current + MainMargins(child, horizontal, scale) + childSize;
            }) : visibleChildren.Aggregate(Fix64.Zero, (current, child) =>
            {
                var childSize = MainSize(child, horizontal, scale);
                if (childSize <= 0) childSize = DefaultMainSize(child, horizontal, scale);
                return Fix64.Max(current, MainMargins(child, horizontal, scale) + childSize);
            });
            return padding + contentSize;
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
        Fix64 available) => Fix64.Min(available, horizontal
        ? DefaultMainSize(element, horizontal: false, scale)
        : DefaultMainSize(element, horizontal: true, scale));

    private static Fix64 Scale(float value, Fix64 scale) => value <= 0 ? Fix64.Zero : (Fix64)value * scale;

    private static int ApproximateTextWidth(string text) =>
        text.Sum(character => character > 0xFF ? 14 : 7);
}
