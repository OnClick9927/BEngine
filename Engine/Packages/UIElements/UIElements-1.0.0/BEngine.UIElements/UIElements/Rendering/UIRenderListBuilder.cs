using System.Runtime.CompilerServices;
using System.Buffers;

namespace BEngine.UIElements;

public static class UIRenderListBuilder
{
    private static readonly ConditionalWeakTable<VisualElement, RenderListCache> Caches = new();
    public static long cacheHits { get; private set; }
    public static int lastRebuiltCommandCount { get; private set; }
    public static UIRenderCommandList Build(
        VisualElement root,
        int viewportWidth,
        int viewportHeight,
        Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        viewportWidth = Math.Max(1, viewportWidth);
        viewportHeight = Math.Max(1, viewportHeight);
        var layouts = UIElementLayoutEngine.Calculate(root, viewportWidth, viewportHeight, scale);
        var styleRoot = root;
        while (styleRoot.parent is not null) styleRoot = styleRoot.parent;
        var cache = Caches.GetValue(root, static _ => new RenderListCache());
        var layoutScrollVersion = layouts is UIElementLayoutSnapshot scrollSnapshot
            ? scrollSnapshot.ScrollVersion : 0;
        // Mutable collection sources can change without calling RefreshItems. Preserve
        // their existing live rendering behavior instead of caching stale rows.
        if (cache.Result is not null && !cache.HasCollections && ReferenceEquals(cache.Layouts, layouts) &&
            ReferenceEquals(cache.StyleRoot, styleRoot) && cache.RootVersion == root.renderVersion &&
            cache.StyleVersion == styleRoot.renderVersion && cache.AssetGeneration == BAsset.cacheGeneration &&
            cache.LayoutScrollVersion == layoutScrollVersion)
        { cacheHits++; lastRebuiltCommandCount = 0; return cache.Result; }
        var result = BuildCore(root, layouts, viewportWidth, viewportHeight, scale,
            scratch: cache.Scratch, previous: cache.Result);
        cache.Result = result; cache.Layouts = layouts; cache.StyleRoot = styleRoot;
        cache.RootVersion = root.renderVersion; cache.StyleVersion = styleRoot.renderVersion;
        cache.AssetGeneration = BAsset.cacheGeneration; cache.LayoutScrollVersion = layoutScrollVersion;
        cache.HasCollections = layouts is UIElementLayoutSnapshot snapshot ? snapshot.HasCollections :
            layouts.Any(layout => layout.Element is ListView or TreeView);
        lastRebuiltCommandCount = result.Commands.Count;
        return result;
    }

    /// <summary>Rebuilds commands explicitly; useful for custom controls and cache validation.</summary>
    public static UIRenderCommandList BuildUncached(VisualElement root, int viewportWidth,
        int viewportHeight, Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        viewportWidth = Math.Max(1, viewportWidth); viewportHeight = Math.Max(1, viewportHeight);
        return BuildCore(root, UIElementLayoutEngine.CalculateUncached(root, viewportWidth, viewportHeight, scale),
            viewportWidth, viewportHeight, scale, useChildRanges: false);
    }

    // The built-in collector copies command values into frame payloads. Input
    // retains this lease across callbacks so a nested input/render request must
    // use an independent public snapshot. Never publish these reusable lists
    // through Build, whose snapshots can be retained by external callers.
    internal static RendererCommandLease BuildForRenderer(VisualElement root,
        int viewportWidth, int viewportHeight, Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        viewportWidth = Math.Max(1, viewportWidth); viewportHeight = Math.Max(1, viewportHeight);
        var layouts = UIElementLayoutEngine.Calculate(root, viewportWidth, viewportHeight, scale);
        var styleRoot = root;
        while (styleRoot.parent is not null) styleRoot = styleRoot.parent;
        var cache = Caches.GetValue(root, static _ => new RenderListCache());
        var scrollVersion = layouts is UIElementLayoutSnapshot snapshot ? snapshot.ScrollVersion : 0;
        // Input may already have published this exact state. Reuse it rather
        // than rebuilding the same commands in a separate renderer cache.
        if (cache.Result is not null && !cache.HasCollections && ReferenceEquals(cache.Layouts, layouts) &&
            ReferenceEquals(cache.StyleRoot, styleRoot) && cache.RootVersion == root.renderVersion &&
            cache.StyleVersion == styleRoot.renderVersion && cache.AssetGeneration == BAsset.cacheGeneration &&
            cache.LayoutScrollVersion == scrollVersion)
        {
            cacheHits++; lastRebuiltCommandCount = 0;
            return new RendererCommandLease(cache.Result.Layouts, cache.Result.Commands,
                cache.Result.HitRegions, cache.Result, 0, null);
        }

        var frame = cache.Renderer ??= new RendererCommandCache();
        // A collection factory or style callback may synchronously request
        // another render. Keep the outer lease's command values untouched.
        if (frame.InUse)
        {
            var published = Build(root, viewportWidth, viewportHeight, scale);
            return new RendererCommandLease(published.Layouts, published.Commands,
                published.HitRegions, published, 0, null);
        }
        frame.InUse = true;
        try
        {
            if (frame.Valid && !frame.HasCollections && ReferenceEquals(frame.Layouts, layouts) &&
                ReferenceEquals(frame.StyleRoot, styleRoot) && frame.RootVersion == root.renderVersion &&
                frame.StyleVersion == styleRoot.renderVersion && frame.AssetGeneration == BAsset.cacheGeneration &&
                frame.ScrollVersion == scrollVersion)
            {
                cacheHits++; lastRebuiltCommandCount = 0;
                return new RendererCommandLease(layouts, frame.Commands, frame.HitRegions,
                    frame, frame.Revision, frame);
            }

            frame.Valid = false;
            frame.Revision++;
            BuildRendererCommands(root, layouts, viewportWidth, viewportHeight, scale, frame);
            frame.Layouts = layouts; frame.StyleRoot = styleRoot;
            frame.RootVersion = root.renderVersion; frame.StyleVersion = styleRoot.renderVersion;
            frame.AssetGeneration = BAsset.cacheGeneration; frame.ScrollVersion = scrollVersion;
            frame.HasCollections = layouts is UIElementLayoutSnapshot current ? current.HasCollections :
                layouts.Any(layout => layout.Element is ListView or TreeView);
            frame.Valid = true;
            lastRebuiltCommandCount = frame.Commands.Count;
            return new RendererCommandLease(layouts, frame.Commands, frame.HitRegions,
                frame, frame.Revision, frame);
        }
        catch
        {
            frame.Commands.Clear(); frame.HitRegions.Clear(); frame.InUse = false;
            throw;
        }
    }

    private static void BuildRendererCommands(VisualElement root, IReadOnlyList<UIElementLayout> layouts,
        int viewportWidth, int viewportHeight, Fix64? scale, RendererCommandCache frame)
    {
        var snapshot = layouts as UIElementLayoutSnapshot;
        IReadOnlyDictionary<VisualElement, UIElementRect>? index = snapshot is null
            ? layouts.ToDictionary(layout => layout.Element, layout => layout.Rect) : null;
        var scratch = frame.Scratch;
        try
        {
            frame.HitRegions.Clear();
            BuildElement(root, new UIElementRect(0, 0, viewportWidth, viewportHeight), index,
                scratch.Commands, frame.HitRegions, scale ?? Fix64.One, snapshot, useChildRanges: true);
            if (scale is { } textScale && textScale != Fix64.One)
                for (var i = 0; i < scratch.Commands.Count; i++)
                    if (scratch.Commands[i].Type == UIRenderCommandType.Text)
                        scratch.Commands[i] = scratch.Commands[i] with { FontSize = scratch.Commands[i].FontSize * (float)textScale };
            var ordered = frame.HitRegions.Any(static layout => layout.Element.orderInLayer != 0);
            if (ordered) OrderRendererHitRegions(frame.HitRegions, scratch.HitRegions);
            var monotonic = true;
            if (ordered)
            {
                var previous = long.MinValue;
                for (var i = 0; i < scratch.Commands.Count; i++)
                {
                    var order = ResolveOrder(scratch.Commands[i].Element);
                    if (order < previous) { monotonic = false; break; }
                    previous = order;
                }
            }
            frame.Commands.Clear();
            if (monotonic) { frame.Commands.AddRange(scratch.Commands); return; }
            var indices = ArrayPool<OrderIndex>.Shared.Rent(scratch.Commands.Count);
            try
            {
                for (var i = 0; i < scratch.Commands.Count; i++) indices[i] = new(ResolveOrder(scratch.Commands[i].Element), i);
                Array.Sort(indices, 0, scratch.Commands.Count);
                for (var i = 0; i < scratch.Commands.Count; i++) frame.Commands.Add(scratch.Commands[indices[i].Index]);
            }
            finally { ArrayPool<OrderIndex>.Shared.Return(indices); }
        }
        finally { scratch.Commands.Clear(); scratch.HitRegions.Clear(); }
    }

    private static void OrderRendererHitRegions(List<UIElementLayout> regions, List<UIElementLayout> scratch)
    {
        var previous = long.MinValue;
        var monotonic = true;
        for (var i = 0; i < regions.Count; i++)
        {
            var order = ResolveOrder(regions[i].Element);
            if (order < previous) { monotonic = false; break; }
            previous = order;
        }
        if (monotonic) return;
        var indices = ArrayPool<OrderIndex>.Shared.Rent(regions.Count);
        try
        {
            for (var i = 0; i < regions.Count; i++) indices[i] = new(ResolveOrder(regions[i].Element), i);
            Array.Sort(indices, 0, regions.Count);
            scratch.AddRange(regions);
            for (var i = 0; i < regions.Count; i++) regions[i] = scratch[indices[i].Index];
        }
        finally { ArrayPool<OrderIndex>.Shared.Return(indices); }
    }

    internal readonly ref struct RendererCommandLease
    {
        private readonly IReadOnlyList<UIElementLayout> _layouts;
        private readonly IReadOnlyList<UIElementLayout> _hitRegions;
        internal readonly IReadOnlyList<UIRenderCommand> Commands;
        internal readonly object Identity;
        internal readonly long Revision;
        private readonly RendererCommandCache? _owner;
        internal RendererCommandLease(IReadOnlyList<UIElementLayout> layouts,
            IReadOnlyList<UIRenderCommand> commands, IReadOnlyList<UIElementLayout> hitRegions,
            object identity, long revision, RendererCommandCache? owner)
        { _layouts = layouts; _hitRegions = hitRegions; Commands = commands; Identity = identity; Revision = revision; _owner = owner; }

        internal VisualElement? Pick(Vector2 position)
        {
            for (var i = _hitRegions.Count - 1; i >= 0; i--)
            {
                var layout = _hitRegions[i];
                if (layout.Element.enabledInHierarchy && layout.Rect.Contains(position)) return layout.Element;
            }
            return null;
        }

        internal bool TryGetRect(VisualElement element, out UIElementRect rect)
        {
            ArgumentNullException.ThrowIfNull(element);
            if (_layouts is UIElementLayoutSnapshot snapshot) return snapshot.TryGetRect(element, out rect);
            for (var i = 0; i < _layouts.Count; i++)
            {
                var layout = _layouts[i];
                if (!ReferenceEquals(layout.Element, element)) continue;
                rect = layout.Rect; return true;
            }
            rect = default; return false;
        }

        // Keep the captured-layout semantics of UIRenderCommandList: pointer
        // callbacks may move/resize the live tree before slider/scroll updates.
        internal float GetMaximumScrollOffset(ScrollView scroll, Fix64? scale = null)
        {
            ArgumentNullException.ThrowIfNull(scroll);
            var resolvedScale = scale ?? Fix64.One;
            if (resolvedScale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            if (!TryGetRect(scroll, out var viewport)) return 0;
            if (_layouts is UIElementLayoutSnapshot snapshot && snapshot.TryGetContentExtent(scroll, out var extent))
                return Math.Max(0, (float)((extent - viewport.Height) / resolvedScale));
            var bottom = viewport.Y;
            foreach (var child in scroll.Children)
                if (TryGetRect(child, out var rect))
                    bottom = Fix64.Max(bottom, rect.Y + rect.Height + (Fix64)child.style.marginBottom * resolvedScale +
                        (Fix64)scroll.scrollOffset * resolvedScale);
            return Math.Max(0, (float)((bottom - viewport.Y - viewport.Height) / resolvedScale));
        }
        public void Dispose() { if (_owner is not null) _owner.InUse = false; }
    }

    internal sealed class RendererCommandCache
    {
        internal readonly List<UIRenderCommand> Commands = [];
        internal readonly List<UIElementLayout> HitRegions = [];
        internal readonly RenderListScratch Scratch = new();
        internal IReadOnlyList<UIElementLayout>? Layouts;
        internal VisualElement? StyleRoot;
        internal long RootVersion, StyleVersion, AssetGeneration, ScrollVersion, Revision;
        internal bool Valid, HasCollections, InUse;
    }

    private static UIRenderCommandList BuildCore(VisualElement root, IReadOnlyList<UIElementLayout> layouts,
        int viewportWidth, int viewportHeight, Fix64? scale, bool useChildRanges = true,
        RenderListScratch? scratch = null, UIRenderCommandList? previous = null)
    {
        var snapshot = layouts as UIElementLayoutSnapshot;
        // A snapshot already owns its immutable element index. Access it
        // directly instead of allocating a dictionary-view wrapper per scroll.
        IReadOnlyDictionary<VisualElement, UIElementRect>? layoutByElement = snapshot is not null
            ? null : layouts.ToDictionary(layout => layout.Element, layout => layout.Rect);
        // Public lists are immutable snapshots. Only construction buffers are reused;
        // input routing and tools may retain an earlier result across UI mutations.
        if (scratch is null || scratch.InUse) scratch = new RenderListScratch();
        scratch.InUse = true;
        var commands = scratch.Commands;
        var hitRegions = scratch.HitRegions;
        try
        {
        var viewport = new UIElementRect(0, 0, viewportWidth, viewportHeight);
        BuildElement(root, viewport, layoutByElement, commands, hitRegions, scale ?? Fix64.One,
            snapshot, useChildRanges);
        if (scale is { } textScale && textScale != Fix64.One)
            for (var index = 0; index < commands.Count; index++)
                if (commands[index].Type == UIRenderCommandType.Text)
                    commands[index] = commands[index] with { FontSize = commands[index].FontSize * (float)textScale };
        if (hitRegions.Any(static layout => layout.Element.orderInLayer != 0))
            return new UIRenderCommandList(layouts,
                SnapshotCommands(commands, previous?.Commands, ordered: true),
                SnapshotHitRegions(hitRegions, previous?.HitRegions, ordered: true));
        return new UIRenderCommandList(layouts, SnapshotCommands(commands, previous?.Commands, ordered: false),
            SnapshotHitRegions(hitRegions, previous?.HitRegions, ordered: false));
        }
        finally { commands.Clear(); hitRegions.Clear(); scratch.InUse = false; }
    }

    private readonly record struct OrderIndex(long Order, int Index) : IComparable<OrderIndex>
    {
        public int CompareTo(OrderIndex other)
        { var order = Order.CompareTo(other.Order); return order != 0 ? order : Index.CompareTo(other.Index); }
    }

    private static UIRenderCommand[] SnapshotCommands(List<UIRenderCommand> source,
        IReadOnlyList<UIRenderCommand>? previous, bool ordered)
    {
        // A dirty offscreen descendant or a change that emits no command can
        // leave the visible result unchanged. Reuse only a fully equal, already
        // immutable array; retained lists never refer to the scratch buffer.
        var monotonic = true;
        if (ordered)
        {
            var previousOrder = long.MinValue;
            for (var i = 0; i < source.Count; i++)
            {
                var currentOrder = ResolveOrder(source[i].Element);
                if (currentOrder < previousOrder) { monotonic = false; break; }
                previousOrder = currentOrder;
            }
        }
        if (monotonic)
        {
            if (previous is UIRenderCommand[] retained && retained.Length == source.Count)
            {
                var matches = true;
                for (var i = 0; i < source.Count; i++)
                    if (!SameCommand(source[i], retained[i])) { matches = false; break; }
                if (matches) return retained;
            }
            return source.ToArray();
        }

        var order = ArrayPool<OrderIndex>.Shared.Rent(source.Count);
        try
        {
            for (var i = 0; i < source.Count; i++) order[i] = new(ResolveOrder(source[i].Element), i);
            Array.Sort(order, 0, source.Count);
            if (previous is UIRenderCommand[] retained && retained.Length == source.Count)
            {
                var matches = true;
                for (var i = 0; i < source.Count; i++)
                    if (!SameCommand(source[order[i].Index], retained[i])) { matches = false; break; }
                if (matches) return retained;
            }
            var result = new UIRenderCommand[source.Count];
            for (var i = 0; i < source.Count; i++) result[i] = source[order[i].Index];
            return result;
        }
        finally { ArrayPool<OrderIndex>.Shared.Return(order); }
    }

    private static bool SameCommand(in UIRenderCommand left, in UIRenderCommand right) =>
        left.Type == right.Type && ReferenceEquals(left.Element, right.Element) &&
        left.Rect == right.Rect && left.ClipRect == right.ClipRect && left.Color == right.Color &&
        string.Equals(left.Content, right.Content, StringComparison.Ordinal) && left.FontSize.Equals(right.FontSize);

    private static UIElementLayout[] SnapshotHitRegions(List<UIElementLayout> source,
        IReadOnlyList<UIElementLayout>? previous, bool ordered)
    {
        // Repaints often change text/color without moving any hit boxes. Share
        // only a completed immutable array after comparing every value and its
        // final order; scratch lists and rented arrays are never published.
        if (previous is UIElementLayout[] retained && retained.Length == source.Count)
        {
            var matches = true; var previousOrder = long.MinValue;
            for (var i = 0; i < source.Count; i++)
            {
                if (!SameHitRegion(source[i], retained[i])) { matches = false; break; }
                if (!ordered) continue;
                var currentOrder = ResolveOrder(source[i].Element);
                if (currentOrder < previousOrder) { matches = false; break; }
                previousOrder = currentOrder;
            }
            if (matches) return retained;
        }
        if (!ordered) return source.ToArray();

        var order = ArrayPool<OrderIndex>.Shared.Rent(source.Count);
        try
        {
            for (var i = 0; i < source.Count; i++) order[i] = new(ResolveOrder(source[i].Element), i);
            Array.Sort(order, 0, source.Count);
            if (previous is UIElementLayout[] sortedRetained && sortedRetained.Length == source.Count)
            {
                var matches = true;
                for (var i = 0; i < source.Count; i++)
                    if (!SameHitRegion(source[order[i].Index], sortedRetained[i])) { matches = false; break; }
                if (matches) return sortedRetained;
            }
            var result = new UIElementLayout[source.Count];
            for (var i = 0; i < source.Count; i++) result[i] = source[order[i].Index];
            return result;
        }
        finally { ArrayPool<OrderIndex>.Shared.Return(order); }
    }

    private static bool SameHitRegion(in UIElementLayout left, in UIElementLayout right) =>
        ReferenceEquals(left.Element, right.Element) && left.Rect == right.Rect;

    internal sealed class RenderListScratch
    {
        internal readonly List<UIRenderCommand> Commands = [];
        internal readonly List<UIElementLayout> HitRegions = [];
        internal bool InUse;
    }

    private sealed class RenderListCache
    {
        internal readonly RenderListScratch Scratch = new();
        internal RendererCommandCache? Renderer;
        internal UIRenderCommandList? Result;
        internal IReadOnlyList<UIElementLayout>? Layouts;
        internal VisualElement? StyleRoot;
        internal long RootVersion, StyleVersion;
        internal long AssetGeneration;
        internal long LayoutScrollVersion;
        internal bool HasCollections;
    }

    private static long ResolveOrder(VisualElement element)
    {
        long order = 0;
        for (var current = element; current is not null; current = current.parent) order += current.orderInLayer;
        return order;
    }

    private static void BuildElement(
        VisualElement element,
        UIElementRect inheritedClip,
        IReadOnlyDictionary<VisualElement, UIElementRect>? layouts,
        ICollection<UIRenderCommand> commands,
        ICollection<UIElementLayout> hitRegions,
        Fix64 scale,
        UIElementLayoutSnapshot? snapshot, bool useChildRanges)
    {
        if (!HasArea(inheritedClip)) return;
        UIElementRect rect;
        if (!(snapshot is not null ? snapshot.TryGetRect(element, out rect) : layouts!.TryGetValue(element, out rect))) return;
        var elementClip = Intersect(inheritedClip, rect);
        // A leaf cannot contribute outside its box; clipped containers cannot expose their descendants.
        if (!HasArea(elementClip) && (element.Children.Count == 0 || element is ScrollView ||
            element.style.overflow != Overflow.Visible)) return;
        if (HasArea(elementClip)) hitRegions.Add(new UIElementLayout(element, elementClip));
        if (TryBuildField(element, rect, elementClip, commands, scale) ||
            TryBuildCollection(element, rect, elementClip, commands))
            return;
        var background = ResolveBackground(element);
        if (background.A > 0 && HasArea(elementClip))
            commands.Add(new UIRenderCommand(
                UIRenderCommandType.SolidRect, element, rect, inheritedClip, background));
        AddBorder(commands, element, rect, elementClip);
        if (element.focused)
            AddRectBorder(commands, element, rect, elementClip, Fix64.One,
                element.style.borderColor ?? new UIColor(238, 208, 120));

        if (element is Image image && !string.IsNullOrWhiteSpace(image.sourcePath) && HasArea(elementClip))
            commands.Add(new UIRenderCommand(
                UIRenderCommandType.Image, element, FitImage(image, rect), elementClip, UIColor.FromRgb(255, 255, 255),
                image.sourcePath));

        var text = ResolveText(element);
        if (!string.IsNullOrWhiteSpace(text) && HasArea(elementClip))
            commands.Add(new UIRenderCommand(
                UIRenderCommandType.Text, element, AlignText(element, rect, text, scale), elementClip, ResolveTextColor(element), text,
                (float)UIElementLayoutEngine.EffectiveFontSize(element)));

        var childClip = element is not ScrollView && element.style.overflow == Overflow.Visible ? inheritedClip : elementClip;
        if (useChildRanges && snapshot is not null && snapshot.TryGetVisibleChildRange(element, childClip, out var children, out var first, out var end))
        {
            for (var index = first; index < end; index++)
                BuildElement(children[index], childClip, layouts, commands, hitRegions, scale, snapshot, useChildRanges);
        }
        else
        {
            var allChildren = element.Children;
            for (var index = 0; index < allChildren.Count; index++)
                BuildElement(allChildren[index], childClip, layouts, commands, hitRegions, scale, snapshot, useChildRanges);
        }
    }

    private static bool TryBuildField(
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        ICollection<UIRenderCommand> commands,
        Fix64 scale)
    {
        if (element is ProgressBar progress)
        {
            var range = progress.highValue - progress.lowValue;
            var normalized = Math.Abs(range) <= float.Epsilon
                ? 0 : Math.Clamp((progress.value - progress.lowValue) / range, 0, 1);
            AddOutlinedRect(commands, element, rect, clip,
                element.style.backgroundColor ?? new UIColor(42, 42, 42),
                element.style.borderColor ?? new UIColor(29, 29, 29));
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
                new UIElementRect(rect.X + 1, rect.Y + 1,
                    Fix64.Max(0, (rect.Width - 2) * (Fix64)normalized), Fix64.Max(0, rect.Height - 2)),
                  clip, progress.fillColor));
            if (progress.showText) AddText(commands, element, rect, clip, string.IsNullOrWhiteSpace(progress.title)
                ? $"{progress.value:0.#}" : progress.title);
            return true;
        }

        if (element is Toggle toggle)
        {
            var boxSize = Fix64.Min((Fix64)(element.style.fontSize + 6) * scale, Fix64.Max(0, rect.Height - 6 * scale));
            var box = new UIElementRect(rect.X + 2 * scale, rect.Y + (rect.Height - boxSize) / 2, boxSize, boxSize);
            if (toggle is RadioButton)
                AddText(commands, element, box, clip, toggle.value ? "\u25cf" : "\u25cb");
            else AddOutlinedRect(commands, element, box, clip, ResolveBackground(element),
                element.style.borderColor ?? new UIColor(140, 155, 163));
            if (toggle.value && toggle is not RadioButton)
                AddText(commands, element, new UIElementRect(box.X - scale, box.Y, box.Width + 2 * scale, box.Height), clip, "\u2713");
            AddText(commands, element, new UIElementRect(rect.X + boxSize + 12 * scale, rect.Y,
                Fix64.Max(0, rect.Width - boxSize - 12 * scale), rect.Height), clip, toggle.label);
            return true;
        }

        var editingValue = element.textEditingValue;
        var field = element switch
        {
            TextField value => (value.label, value.isPasswordField
                ? new string(value.maskCharacter, (editingValue ?? value.value).Length)
                : editingValue ?? value.value),
            FloatField value => (value.label, editingValue ??
                value.value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            IntegerField value => (value.label, editingValue ??
                value.value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ColorField value => (value.label, FormatColor(value.value, value.showAlpha)),
            Slider value => (value.label, value.value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
            DropdownField value => (value.label, value.value),
            _ => ((string Label, string Value)?)null
        };
        if (field is null) return false;

        var labelWidth = string.IsNullOrWhiteSpace(field.Value.Label) ? Fix64.Zero : rect.Width * (Fix64)0.4;
        var labelRect = new UIElementRect(rect.X, rect.Y, labelWidth, rect.Height);
        var valueRect = new UIElementRect(rect.X + labelWidth, rect.Y,
            Fix64.Max(0, rect.Width - labelWidth), rect.Height);
        if (labelWidth > 0) AddText(commands, element, labelRect, clip, field.Value.Label);
        var fieldBackground = element.style.backgroundColor ??
                              (element.isReadOnlyField() ? new UIColor(47, 47, 47) : new UIColor(42, 42, 42));
        AddOutlinedRect(commands, element, valueRect, clip, fieldBackground,
            element.style.borderColor ?? new UIColor(29, 29, 29));
        if (element.sourceTag.Length > 0)
        {
            var inset = Fix64.Max(0, element.style.paddingLeft - 4) * scale;
            valueRect = valueRect with { X = valueRect.X + inset,
                Width = Fix64.Max(0, valueRect.Width - inset - (Fix64)element.style.paddingRight * scale) };
        }
        if (element is ColorField color)
        {
            var swatch = new UIElementRect(valueRect.X + 3, valueRect.Y + 3,
                Fix64.Min(36, Fix64.Max(0, valueRect.Width - 6)), Fix64.Max(0, valueRect.Height - 6));
            AddCheckerboard(commands, element, swatch, clip);
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, swatch, clip,
                ToUiColor(color.value)));
            AddText(commands, element,
                new UIElementRect(swatch.X + swatch.Width + 5, valueRect.Y,
                    Fix64.Max(0, valueRect.Width - swatch.Width - 8), valueRect.Height),
                clip, field.Value.Value);
        }
        else if (element is Slider slider)
        {
            var range = slider.highValue - slider.lowValue;
            var t = range <= float.Epsilon ? 0 : Math.Clamp((slider.value - slider.lowValue) / range, 0, 1);
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
                new UIElementRect(valueRect.X, valueRect.Y, valueRect.Width * (Fix64)t, valueRect.Height), clip,
                new UIColor(45, 93, 135)));
            AddText(commands, element, valueRect, clip, field.Value.Value);
        }
        else if (element is SearchField search)
        {
            var iconWidth = Fix64.Min(20, valueRect.Width);
            AddText(commands, element,
                new UIElementRect(valueRect.X + 2, valueRect.Y, iconWidth, valueRect.Height), clip, "\u2315");
            var displayValue = field.Value.Value;
            var clearWidth = search.showClearButton && !string.IsNullOrEmpty(displayValue)
                ? Fix64.Min(20, valueRect.Width) : Fix64.Zero;
            var contentRect = new UIElementRect(valueRect.X + iconWidth, valueRect.Y,
                Fix64.Max(0, valueRect.Width - iconWidth - clearWidth), valueRect.Height);
            if (string.IsNullOrEmpty(displayValue) && !search.isTextEditing && !search.focused)
                commands.Add(new UIRenderCommand(UIRenderCommandType.Text, element, contentRect, clip,
                    new UIColor(125, 125, 125), search.placeholderText,
                    (float)UIElementLayoutEngine.EffectiveFontSize(element)));
            else
            {
                AddTextSelection(commands, element, contentRect, clip, displayValue, scale);
                AddText(commands, element, contentRect, clip, displayValue);
            }
            AddTextCaret(commands, element, contentRect, clip, displayValue, scale);
            if (clearWidth > 0)
                AddText(commands, element, new UIElementRect(valueRect.X + valueRect.Width - clearWidth,
                    valueRect.Y, clearWidth, valueRect.Height), clip, "x");
        }
        else
        {
            if (element is TextField placeholder && string.IsNullOrEmpty(field.Value.Value) &&
                !placeholder.isTextEditing && !placeholder.focused && placeholder.placeholderText.Length > 0)
                commands.Add(new UIRenderCommand(UIRenderCommandType.Text, element, valueRect, clip,
                    new UIColor(125, 125, 125), placeholder.placeholderText, (float)UIElementLayoutEngine.EffectiveFontSize(element)));
            if (element is TextField or FloatField or IntegerField)
                AddTextSelection(commands, element, valueRect, clip, field.Value.Value, scale);
            AddText(commands, element, valueRect, clip, field.Value.Value);
            if (element is TextField or FloatField or IntegerField)
                AddTextCaret(commands, element, valueRect, clip, field.Value.Value, scale);
            if (element is DropdownField)
                AddText(commands, element, new UIElementRect(valueRect.X + valueRect.Width - 18, valueRect.Y,
                    18, valueRect.Height), clip, "v");
        }
        return true;
    }

    private static void AddTextSelection(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect contentRect,
        UIElementRect clip,
        string displayText, Fix64 scale)
    {
        if (!element.isTextEditing || element.isReadOnlyField()) return;
        var start = Math.Clamp(Math.Min(element.textEditingSelectionStart,
            element.textEditingSelectionEnd), 0, displayText.Length);
        var end = Math.Clamp(Math.Max(element.textEditingSelectionStart,
            element.textEditingSelectionEnd), 0, displayText.Length);
        if (end <= start) return;

        var fontSize = UIElementLayoutEngine.EffectiveFontSize(element) * scale;
        var inset = TextInset(element, fontSize);
        var lineHeight = TextLineHeight(element, fontSize);
        var selectionClip = Intersect(clip, contentRect);
        var lineStart = 0; var row = 0;
        while (lineStart <= displayText.Length)
        {
            var lineEnd = displayText.IndexOf('\n', lineStart); if (lineEnd < 0) lineEnd = displayText.Length;
            var a = Math.Max(start, lineStart); var b = Math.Min(end, lineEnd);
            if (b > a || end > lineEnd && start <= lineEnd)
            {
                var left = contentRect.X + inset + MeasureTextAdvance(displayText[lineStart..a], fontSize, element);
                var right = contentRect.X + inset + MeasureTextAdvance(displayText[lineStart..Math.Max(a, b)], fontSize, element);
                if (end > lineEnd) right += (Fix64)(fontSize / 3);
                var multiline = element is TextField { multiline: true };
                var y = multiline ? contentRect.Y + 2 + row * lineHeight : contentRect.Y + 2;
                var selectionRect = new UIElementRect(left, y, Fix64.Max(0, right - left),
                    multiline ? lineHeight : Fix64.Max(0, contentRect.Height - 4));
                if (HasArea(selectionRect) && HasArea(selectionClip)) commands.Add(new(UIRenderCommandType.SolidRect,
                    element, selectionRect, selectionClip, new UIColor(45, 93, 135)));
            }
            if (lineEnd == displayText.Length) break;
            lineStart = lineEnd + 1; row++;
        }
    }

    private static void AddTextCaret(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect contentRect,
        UIElementRect clip,
        string displayText, Fix64 scale)
    {
        if (!element.isTextEditing || element.isReadOnlyField()) return;

        var index = Math.Clamp(element.textEditingCaretIndex, 0, displayText.Length);
        var prefix = displayText.AsSpan(0, index);
        var lastLineBreak = prefix.LastIndexOf('\n');
        if (lastLineBreak >= 0) prefix = prefix[(lastLineBreak + 1)..];
        var fontSize = (UIElementLayoutEngine.EffectiveFontSize(element)) * scale;
        var advance = EstimateTextWidth(prefix, fontSize);
        var lineHeight = TextLineHeight(element, fontSize);
        if (Style.ResolveFont(element) is { } font)
        {
            advance = (Fix64)(double)font.MeasureAdvance(prefix.ToString(), (float)fontSize);
            lineHeight = (Fix64)(double)font.lineHeight * fontSize / (Fix64)(double)font.samplingSize;
        }
        else if (UIElementsRuntimeServices.TextResourceResolver?.TryMeasureText(prefix.ToString(), (float)fontSize, out var measured, out var measuredHeight) == true)
        {
            advance = (Fix64)(double)measured;
            lineHeight = (Fix64)(double)measuredHeight;
        }
        var inset = TextInset(element, fontSize);
        var x = contentRect.X + inset + advance;
        var maxX = contentRect.X + Fix64.Max(1, contentRect.Width - 2);
        x = Fix64.Min(x, maxX);
        var caretRect = new UIElementRect(
            x,
            element is TextField { multiline: true }
                ? contentRect.Y + 2 + displayText.AsSpan(0, index).Count('\n') * lineHeight
                : contentRect.Y + Fix64.Max(2, (contentRect.Height - lineHeight) / 2),
            Fix64.Max(Fix64.One, scale),
            Fix64.Min(lineHeight, Fix64.Max(0, contentRect.Height - 4)));
        var caretClip = Intersect(clip, contentRect);
        var visibleCaret = Intersect(caretRect, caretClip);
        Input.textInputRect = HasArea(visibleCaret)
            ? new Rect(visibleCaret.X, visibleCaret.Y, visibleCaret.Width, visibleCaret.Height) : null;
        if (!element.textEditingCaretVisible) return;
        if (HasArea(caretRect) && HasArea(caretClip))
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, caretRect,
                caretClip, ResolveTextColor(element)));
    }

    private static Fix64 EstimateTextWidth(ReadOnlySpan<char> text, Fix64 fontSize)
    {
        // Hundredths keep this fallback independent of hardware floating-point math.
        long units = 0;
        foreach (var character in text)
        {
            units += character switch
            {
                '\t' => 220,
                ' ' => 35,
                'i' or 'l' or 'I' or '.' or ',' or ':' or ';' or '!' or '|' or '\'' => 30,
                'm' or 'w' or 'M' or 'W' or '@' => 85,
                _ when character >= 0x2E80 => 100,
                _ when char.IsUpper(character) => 62,
                _ => 55
            };
        }
        return (Fix64)units * fontSize * 62 / 10000;
    }

    internal static Fix64 TextInset(VisualElement element, Fix64 fontSize) =>
        Style.ResolveFont(element) is not null ? Fix64.Zero : Fix64.Min(6, Fix64.Max(0, RoundToEven(fontSize / 3)));

    private static Fix64 RoundToEven(Fix64 value)
    {
        var whole = value.RawValue >> Fix64.FractionalBits;
        var fraction = value.RawValue & (Fix64.OneRaw - 1);
        if (fraction > Fix64.OneRaw / 2 || fraction == Fix64.OneRaw / 2 && (whole & 1) != 0) whole++;
        return (Fix64)whole;
    }

    internal static Fix64 TextLineHeight(VisualElement element, Fix64 fontSize) => Style.ResolveFont(element) is { } font
        ? (Fix64)(double)font.lineHeight * fontSize / (Fix64)(double)font.samplingSize : fontSize * 14 / 10;

    internal static Fix64 MeasureTextAdvance(string text, Fix64 fontSize, VisualElement? element = null) =>
        // The platform font provider remains a floating-point boundary. Preserve
        // all returned float bits before entering deterministic layout arithmetic.
        Style.ResolveFont(element) is { } font ? (Fix64)(double)font.MeasureAdvance(text, (float)fontSize) :
        UIElementsRuntimeServices.TextResourceResolver?.TryMeasureText(text, (float)fontSize, out var width, out _) == true
            ? (Fix64)(double)width : EstimateTextWidth(text, fontSize);

    private static bool TryBuildCollection(
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        ICollection<UIRenderCommand> commands)
    {
        if (element is ListView list)
        {
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, rect, clip,
                element.style.backgroundColor ?? new UIColor(56, 56, 56)));
            if (list.showBorder) AddRectBorder(commands, element, rect, clip, Fix64.One,
                element.style.borderColor ?? new UIColor(29, 29, 29));
            var rowHeight = (Fix64)list.fixedItemHeight;
            var first = Math.Max(0, (int)(list.scrollOffset / list.fixedItemHeight));
            var visible = Math.Max(1, (int)Math.Ceiling((double)rect.Height / list.fixedItemHeight) + 1);
            for (var index = first; index < Math.Min(list.itemsSource.Count, first + visible); index++)
            {
                var y = rect.Y + index * rowHeight - (Fix64)list.scrollOffset;
                var row = new UIElementRect(rect.X, y, rect.Width, rowHeight);
                var rowClass = list.makeItemClass(list.itemsSource[index]);
                if (list.IsSelected(index))
                    commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                        new UIColor(45, 93, 135)));
                else if (rowClass.Contains("error", StringComparison.OrdinalIgnoreCase))
                    commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                        new UIColor(83, 40, 40)));
                else if (rowClass.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                        new UIColor(78, 67, 39)));
                else if (list.showAlternatingRowBackgrounds && index % 2 == 1)
                    commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                        new UIColor(52, 52, 52)));
                var icon = list.makeItemIcon(list.itemsSource[index]);
                var textRect = row;
                if (!string.IsNullOrWhiteSpace(icon))
                {
                    var iconRect = new UIElementRect(row.X + 3, row.Y + 2,
                        Fix64.Max(0, rowHeight - 4), Fix64.Max(0, rowHeight - 4));
                    commands.Add(new UIRenderCommand(UIRenderCommandType.Image, element, iconRect, clip,
                        UIColor.FromRgb(255, 255, 255), icon));
                    textRect = new UIElementRect(row.X + rowHeight + 3, row.Y,
                        Fix64.Max(0, row.Width - rowHeight - 3), row.Height);
                }
                AddText(commands, element, textRect, clip, list.makeItemText(list.itemsSource[index]));
            }
            return true;
        }
        if (element is not TreeView tree) return false;
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, rect, clip,
            element.style.backgroundColor ?? new UIColor(56, 56, 56)));
        if (tree.showBorder) AddRectBorder(commands, element, rect, clip, Fix64.One,
            element.style.borderColor ?? new UIColor(29, 29, 29));
        var treeRowHeight = (Fix64)tree.fixedItemHeight;
        var rows = tree.GetVisibleRows();
        var firstRow = Math.Max(0, (int)(tree.scrollOffset / tree.fixedItemHeight));
        var visibleRows = Math.Max(1, (int)Math.Ceiling((double)rect.Height / tree.fixedItemHeight) + 1);
        for (var index = firstRow; index < Math.Min(rows.Count, firstRow + visibleRows); index++)
        {
            var (item, depth) = rows[index];
            var y = rect.Y + index * treeRowHeight - (Fix64)tree.scrollOffset;
            var row = new UIElementRect(rect.X, y, rect.Width, treeRowHeight);
            if (tree.IsSelected(item.Id))
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                    tree.focused ? new UIColor(45, 93, 135) : new UIColor(69, 69, 69)));
            else if (tree.pressedId == item.Id)
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                    new UIColor(62, 86, 108)));
            else if (tree.hoveredId == item.Id)
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                    new UIColor(67, 67, 67)));
            else if (tree.showAlternatingRowBackgrounds && index % 2 == 1)
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                    new UIColor(52, 52, 52)));
            if (tree.dropTargetId == item.Id)
            {
                var marker = tree.dropPosition switch
                {
                    TreeViewDropPosition.Before => new UIElementRect(row.X, row.Y, row.Width, 2),
                    TreeViewDropPosition.After => new UIElementRect(row.X, row.Y + row.Height - 2, row.Width, 2),
                    _ => row
                };
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, marker, clip,
                    tree.dropPosition == TreeViewDropPosition.Inside
                        ? new UIColor(38, 94, 132, 170)
                        : new UIColor(72, 160, 220)));
            }
            var indent = depth * 14;
            if (item.Children is { Count: > 0 })
                AddText(commands, element, new UIElementRect(row.X + indent, row.Y, 14, row.Height), clip,
                    tree.IsExpanded(item.Id) ? "v" : ">");
            var contentX = row.X + indent + 14;
            var itemIconPath = tree.IsExpanded(item.Id) &&
                               !string.IsNullOrWhiteSpace(item.ExpandedIconPath)
                ? item.ExpandedIconPath
                : item.IconPath;
            if (!string.IsNullOrWhiteSpace(itemIconPath))
            {
                var iconSize = Fix64.Max(0, treeRowHeight - 4);
                commands.Add(new UIRenderCommand(UIRenderCommandType.Image, element,
                    new UIElementRect(contentX, row.Y + 2, iconSize, iconSize), clip,
                    UIColor.FromRgb(255, 255, 255), itemIconPath));
                contentX += treeRowHeight;
            }
            var textRect = new UIElementRect(contentX, row.Y,
                Fix64.Max(0, row.X + row.Width - contentX), row.Height);
            var text = tree.renamingId == item.Id ? tree.renameValue : item.Text;
            AddText(commands, element, textRect, clip, text);
            if (tree.renamingId == item.Id)
                AddRectBorder(commands, element, textRect, clip, Fix64.One, new UIColor(58, 121, 178));
        }
        if (tree.focused)
            AddRectBorder(commands, element, rect, clip, Fix64.One, new UIColor(58, 121, 178));
        return true;
    }

    private static void AddText(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        commands.Add(new UIRenderCommand(UIRenderCommandType.Text, element, rect, clip,
            ResolveTextColor(element), text, (float)UIElementLayoutEngine.EffectiveFontSize(element)));
    }

    private static string FormatColor(BEngine.Color color, bool showAlpha)
    {
        static byte ToByte(Fix64 value) => (byte)Math.Clamp((int)Math.Round(
            Math.Clamp((double)value, 0, 1) * 255), 0, 255);
        var rgb = $"#{ToByte(color.r):X2}{ToByte(color.g):X2}{ToByte(color.b):X2}";
        return showAlpha ? $"{rgb}{ToByte(color.a):X2}" : rgb;
    }

    private static void AddCheckerboard(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip)
    {
        var tileWidth = rect.Width / 2;
        var tileHeight = rect.Height / 2;
        for (var row = 0; row < 2; row++)
        for (var column = 0; column < 2; column++)
        {
            var x = rect.X + tileWidth * column;
            var y = rect.Y + tileHeight * row;
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
                new UIElementRect(x, y, column == 0 ? tileWidth : rect.Width - tileWidth,
                    row == 0 ? tileHeight : rect.Height - tileHeight), clip,
                (row + column) % 2 == 0 ? new UIColor(190, 190, 190) : new UIColor(100, 100, 100)));
        }
    }

    private static UIColor ToUiColor(BEngine.Color color)
    {
        static byte ToByte(Fix64 value) => (byte)Math.Clamp((int)Math.Round(
            Math.Clamp((double)value, 0, 1) * 255), 0, 255);
        return new UIColor(ToByte(color.r), ToByte(color.g), ToByte(color.b), ToByte(color.a));
    }

    private static UIColor ResolveBackground(VisualElement element) =>
        element.style.backgroundColor ?? element switch
        {
            Button => new UIColor(88, 88, 88),
            TextField or FloatField or IntegerField or DropdownField => new UIColor(38, 41, 45),
            _ => UIColor.Clear
        };

    internal static Fix64 MeasureTextWidth(string text, Fix64 fontSize, VisualElement? element = null) => MeasureTextAdvance(text, fontSize, element) +
        (Style.ResolveFont(element) is not null ? Fix64.Zero : (Fix64)(UIElementsRuntimeServices.TextResourceResolver?.GetHorizontalPadding((float)fontSize) ?? 0));

    private static UIElementRect FitImage(Image image, UIElementRect rect)
    {
        if (image.scaleMode == "StretchToFill") return rect;
        var texture = image.ResolveTexture();
        if (texture is null || texture.width <= 0 || texture.height <= 0) return rect;
        var x = rect.Width / texture.width; var y = rect.Height / texture.height;
        var fit = image.scaleMode == "ScaleAndCrop" ? Fix64.Max(x, y) : Fix64.Min(x, y);
        var width = texture.width * fit; var height = texture.height * fit;
        return new UIElementRect(rect.X + (rect.Width-width)/2, rect.Y + (rect.Height-height)/2, width, height);
    }

    private static UIElementRect AlignText(VisualElement element, UIElementRect rect, string text, Fix64 scale)
    {
        var align = element.style.textAlign;
        if (element is Button)
        {
            // Buttons have an intrinsic centered caption on every platform.
            // Keep explicit left/right alignment for authored compound controls.
            if (align.Length == 0) align = "center";
            var lineHeight = TextLineHeight(element, UIElementLayoutEngine.EffectiveFontSize(element) * scale);
            var height = Fix64.Min(rect.Height, lineHeight * (text.AsSpan().Count('\n') + 1));
            rect = rect with { Y = rect.Y + (rect.Height - height) / 2, Height = height };
        }
        for (var current = element.parent; align.Length == 0 && current is not null; current = current.parent)
            align = current.style.textAlign;
        if (align is not ("center" or "right")) return rect;
        var width = Fix64.Min(rect.Width, MeasureTextWidth(text, UIElementLayoutEngine.EffectiveFontSize(element) * scale, element));
        return rect with { X = rect.X + (rect.Width - width) / (align == "center" ? 2 : 1), Width = width };
    }

    private static UIColor ResolveTextColor(VisualElement element)
    {
        for (var current = element; current is not null; current = current.parent)
            if (current.style.color is { } color) return color;
        return new UIColor(210, 210, 210);
    }

    private static string ResolveText(VisualElement element) => element switch
    {
        ToolbarMenu menu => $"{menu.text}  v",
        TextField field => field.value,
        FloatField field => field.value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IntegerField field => field.value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Toggle toggle => $"{(toggle.value ? "[x]" : "[ ]")} {toggle.label}",
        DropdownField dropdown => dropdown.value,
        TextElement text => text.text,
        _ => string.Empty
    };

    private static UIElementRect Intersect(UIElementRect first, UIElementRect second)
    {
        var left = Fix64.Max(first.X, second.X);
        var top = Fix64.Max(first.Y, second.Y);
        var right = Fix64.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Fix64.Min(first.Y + first.Height, second.Y + second.Height);
        return new UIElementRect(left, top, Fix64.Max(0, right - left), Fix64.Max(0, bottom - top));
    }

    private static bool HasArea(UIElementRect rect) => rect.Width > 0 && rect.Height > 0;

    private static void AddBorder(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip)
    {
        var width = element.style.borderWidth;
        if (width <= 0 || element.style.borderColor is not { } color) return;
        AddRectBorder(commands, element, rect, clip, (Fix64)width, color);
    }

    private static void AddOutlinedRect(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        UIColor fill,
        UIColor border)
    {
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, rect, clip, fill));
        AddRectBorder(commands, element, rect, clip, Fix64.One, border);
    }

    private static void AddRectBorder(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        Fix64 width,
        UIColor color)
    {
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
            new UIElementRect(rect.X, rect.Y, rect.Width, width), clip, color));
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
            new UIElementRect(rect.X, rect.Y + rect.Height - width, rect.Width, width), clip, color));
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
            new UIElementRect(rect.X, rect.Y, width, rect.Height), clip, color));
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
            new UIElementRect(rect.X + rect.Width - width, rect.Y, width, rect.Height), clip, color));
    }

    private static bool isReadOnlyField(this VisualElement element) => element switch
    {
        TextField field => field.isReadOnly,
        FloatField field => field.isReadOnly,
        IntegerField field => field.isReadOnly,
        ColorField field => field.isReadOnly,
        Slider field => field.isReadOnly,
        DropdownField field => field.isReadOnly,
        _ => false
    };
}
