using System.Collections;
using System.Buffers;

namespace BEngine.UIElements;

/// <summary>Immutable layout geometry with per-scroll-container translations and shared lookup indices.</summary>
internal sealed class UIElementLayoutSnapshot : IReadOnlyList<UIElementLayout>
{
    internal readonly record struct ScrollBinding(ScrollView Element, int Parent, Fix64 InitialOffset);
    private readonly record struct ScrollState(Fix64 Offset, Fix64 Translation);
    private readonly Geometry _geometry;
    private readonly ScrollState[] _scrollState;
    // Published layouts may be retained by render lists, input and tools. Keep
    // their scroll state immutable too; share only the much larger geometry.
    internal long ScrollVersion { get; }
    internal bool HasCollections => _geometry.HasCollections;
    public int Count => _geometry.Layouts.Length;

    internal static UIElementLayoutSnapshot Create(List<UIElementLayout> layouts, List<ScrollBinding> scrollers,
        List<int> scrollAncestors, Fix64 scale, UIElementLayoutSnapshot? previous)
    {
        // Reuse only completed immutable arrays after full value comparison.
        // Geometry is still rebuilt: live overflow and margins can change its
        // child ranges/content extents even when every rectangle is identical.
        return new UIElementLayoutSnapshot(SnapshotLayouts(layouts, previous?._geometry.Layouts),
            SnapshotScrollers(scrollers, previous?._geometry.Scrollers),
            Snapshot(scrollAncestors, previous?._geometry.ScrollAncestors), scale, previous);
    }

    private static UIElementLayout[] SnapshotLayouts(List<UIElementLayout> values, UIElementLayout[]? previous)
    {
        if (previous is not null && previous.Length == values.Count)
        {
            var equal = true;
            for (var i = 0; i < values.Count; i++)
                if (!ReferenceEquals(values[i].Element, previous[i].Element) || values[i].Rect != previous[i].Rect)
                { equal = false; break; }
            if (equal) return previous;
        }
        return values.ToArray();
    }

    private static ScrollBinding[] SnapshotScrollers(List<ScrollBinding> values, ScrollBinding[]? previous)
    {
        if (previous is not null && previous.Length == values.Count)
        {
            var equal = true;
            for (var i = 0; i < values.Count; i++)
                if (!ReferenceEquals(values[i].Element, previous[i].Element) || values[i].Parent != previous[i].Parent ||
                    values[i].InitialOffset != previous[i].InitialOffset) { equal = false; break; }
            if (equal) return previous;
        }
        return values.ToArray();
    }

    private static T[] Snapshot<T>(List<T> values, T[]? previous)
    {
        if (previous is not null && previous.Length == values.Count)
        {
            var equal = true;
            for (var i = 0; i < values.Count; i++)
                if (!EqualityComparer<T>.Default.Equals(values[i], previous[i])) { equal = false; break; }
            if (equal) return previous;
        }
        return values.ToArray();
    }

    internal UIElementLayoutSnapshot(UIElementLayout[] layouts, ScrollBinding[] scrollers,
        int[] scrollAncestors, Fix64 scale, UIElementLayoutSnapshot? previous = null)
    {
        _geometry = new Geometry(layouts, scrollers, scrollAncestors, scale, previous?._geometry);
        _scrollState = new ScrollState[scrollers.Length];
        for (var i = 0; i < scrollers.Length; i++)
            _scrollState[i] = new(scrollers[i].InitialOffset, Fix64.Zero);
    }

    private UIElementLayoutSnapshot(Geometry geometry, ScrollState[] scrollState, long scrollVersion)
    {
        _geometry = geometry;
        _scrollState = scrollState;
        ScrollVersion = scrollVersion;
    }

    public UIElementLayout this[int index]
    {
        get
        {
            var layout = _geometry.Layouts[index];
            var ancestor = _geometry.ScrollAncestors[index];
            return ancestor < 0 ? layout : layout with { Rect = layout.Rect with
                { Y = layout.Rect.Y + _scrollState[ancestor].Translation } };
        }
    }

    internal UIElementLayoutSnapshot WithCurrentScrollOffsets()
    {
        var scrollers = _geometry.Scrollers;
        var changed = false;
        for (var i = 0; i < scrollers.Length; i++)
            if (_scrollState[i].Offset != (Fix64)scrollers[i].Element.scrollOffset * _geometry.Scale)
            { changed = true; break; }
        if (!changed) return this;

        var scrollState = new ScrollState[scrollers.Length];
        for (var i = 0; i < scrollers.Length; i++)
        {
            var binding = scrollers[i];
            var offset = (Fix64)binding.Element.scrollOffset * _geometry.Scale;
            var translation = binding.InitialOffset - offset +
                (binding.Parent < 0 ? Fix64.Zero : scrollState[binding.Parent].Translation);
            scrollState[i] = new(offset, translation);
        }
        return new UIElementLayoutSnapshot(_geometry, scrollState, ScrollVersion + 1);
    }

    internal bool TryGetRect(VisualElement element, out UIElementRect rect)
    {
        if (_geometry.Indices.TryGetValue(element, out var index)) { rect = this[index].Rect; return true; }
        rect = default; return false;
    }

    internal bool TryGetContentExtent(ScrollView element, out Fix64 extent)
    {
        if (_geometry.ContentExtents is { } extents) return extents.TryGetValue(element, out extent);
        extent = default; return false;
    }

    internal bool TryGetVisibleChildRange(VisualElement parent, UIElementRect clip,
        out IReadOnlyList<VisualElement> children, out int first, out int end)
    {
        if (_geometry.ChildRanges is not { } ranges || !ranges.TryGetValue(parent, out var range))
        { children = Array.Empty<VisualElement>(); first = end = 0; return false; }
        children = range.Children;
        var translation = range.ScrollAncestor < 0 ? Fix64.Zero : _scrollState[range.ScrollAncestor].Translation;
        var top = clip.Y - translation; var bottom = top + clip.Height;
        first = LowerBound(range.Bottoms, top); end = UpperBound(range.Tops, bottom);
        return true;
    }

    private static int LowerBound(Fix64[] values, Fix64 value)
    {
        var lo = 0; var hi = values.Length;
        while (lo < hi) { var middle = lo + (hi - lo) / 2; if (values[middle] < value) lo = middle + 1; else hi = middle; }
        return lo;
    }
    private static int UpperBound(Fix64[] values, Fix64 value)
    {
        var lo = 0; var hi = values.Length;
        while (lo < hi) { var middle = lo + (hi - lo) / 2; if (values[middle] <= value) lo = middle + 1; else hi = middle; }
        return lo;
    }

    private sealed record ChildRange(VisualElement[] Children, Fix64[] Tops, Fix64[] Bottoms, int ScrollAncestor);
    private readonly record struct VerticalBounds(Fix64 Top, Fix64 Bottom);

    public IEnumerator<UIElementLayout> GetEnumerator()
    { for (var i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Geometry
    {
        internal readonly UIElementLayout[] Layouts;
        internal readonly ScrollBinding[] Scrollers;
        internal readonly int[] ScrollAncestors;
        internal readonly Fix64 Scale;
        internal readonly Dictionary<VisualElement, int> Indices;
        // Most HUDs have neither scroll containers nor wide child groups.
        // Absence needs no mutable empty dictionary in each immutable geometry.
        internal readonly Dictionary<ScrollView, Fix64>? ContentExtents;
        internal readonly Dictionary<VisualElement, ChildRange>? ChildRanges;
        internal readonly bool HasCollections;
        internal Geometry(UIElementLayout[] layouts, ScrollBinding[] scrollers, int[] scrollAncestors, Fix64 scale,
            Geometry? previous = null)
        {
            Layouts = layouts; Scrollers = scrollers; ScrollAncestors = scrollAncestors; Scale = scale;
            // Bounds can animate while every element retains its preorder index.
            // The completed index is never mutated; share it only when the full
            // element sequence matches, leaving all bounds/scroll data independent.
            var shareIndices = previous is not null && layouts.Length == previous.Layouts.Length;
            var hasWideChildren = false;
            for (var i = 0; i < layouts.Length; i++)
            {
                if (shareIndices && !ReferenceEquals(layouts[i].Element, previous!.Layouts[i].Element))
                    shareIndices = false;
                HasCollections |= layouts[i].Element is ListView or TreeView;
                hasWideChildren |= layouts[i].Element.Children.Count >= 64;
            }
            Indices = shareIndices ? previous!.Indices : new(layouts.Length);
            if (!shareIndices)
                for (var i = 0; i < layouts.Length; i++) Indices.Add(layouts[i].Element, i);
            if (scrollers.Length > 0) ContentExtents = new();
            foreach (var binding in scrollers)
            {
                var viewport = layouts[Indices[binding.Element]].Rect;
                var bottom = viewport.Y;
                foreach (var child in binding.Element.Children)
                    if (Indices.TryGetValue(child, out var index))
                    {
                        var rect = layouts[index].Rect;
                        bottom = Fix64.Max(bottom, rect.Y + rect.Height +
                            (Fix64)child.style.marginBottom * scale + binding.InitialOffset);
                    }
                ContentExtents!.Add(binding.Element, bottom - viewport.Y);
            }
            if (hasWideChildren)
            {
                ChildRanges = new();
                BuildChildRanges();
            }
        }

        private void BuildChildRanges()
        {
            // Layouts are in preorder. Fold descendant bounds into overflowing parents
            // in reverse order, stopping at each clipping/scrolling boundary. This
            // preserves descendants outside a row's own box without scanning every row.
            var bounds = ArrayPool<VerticalBounds>.Shared.Rent(Layouts.Length);
            try
            {
                for (var i = 0; i < Layouts.Length; i++)
                {
                    var rect = Layouts[i].Rect; bounds[i] = new(rect.Y, rect.Y + rect.Height);
                }
                for (var i = Layouts.Length - 1; i >= 0; i--)
                {
                    var parent = Layouts[i].Element.parent;
                    if (parent is null or ScrollView || parent.style.overflow != Overflow.Visible ||
                        !Indices.TryGetValue(parent, out var parentIndex)) continue;
                    var current = bounds[parentIndex]; var child = bounds[i];
                    bounds[parentIndex] = new(Fix64.Min(current.Top, child.Top), Fix64.Max(current.Bottom, child.Bottom));
                }
                foreach (var layout in Layouts)
                {
                    var parent = layout.Element; var source = parent.Children;
                    if (source.Count < 64) continue;
                    var ancestor = -1; var eligible = true; var count = 0;
                    var previous = default(VerticalBounds);
                    for (var i = 0; i < source.Count; i++)
                    {
                        var child = source[i];
                        if (!Indices.TryGetValue(child, out var index)) continue;
                        var extent = bounds[index];
                        // Unordered or overlapping extents that are not monotonic retain
                        // the complete traversal and its original rendering/picking order.
                        if (count > 0 && (extent.Top < previous.Top || extent.Bottom < previous.Bottom ||
                            ancestor != ScrollAncestors[index])) { eligible = false; break; }
                        ancestor = ScrollAncestors[index];
                        previous = extent; count++;
                    }
                    if (!eligible) continue;
                    // Most absolute-positioned groups cannot use binary-search ranges.
                    // Validate first so they allocate no discarded arrays; hidden children
                    // are omitted from the exact size of an eligible group's arrays.
                    var children = new VisualElement[count]; var tops = new Fix64[count]; var bottoms = new Fix64[count];
                    count = 0;
                    for (var i = 0; i < source.Count; i++)
                    {
                        var child = source[i];
                        if (!Indices.TryGetValue(child, out var index)) continue;
                        var extent = bounds[index];
                        children[count] = child; tops[count] = extent.Top; bottoms[count] = extent.Bottom; count++;
                    }
                    ChildRanges!.Add(parent, new(children, tops, bottoms, ancestor));
                }
            }
            finally { ArrayPool<VerticalBounds>.Shared.Return(bounds); }
        }
    }

}
