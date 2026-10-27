namespace BEngine.UIElements;

public static partial class UIElementLayoutEngine
{
    // Only fixed-size, absolute children of the requested layout root can own
    // independent geometry here. Flow/intrinsic/relative-size trees still take
    // the full reference path. Published flat arrays are never edited or leased.
    private sealed class FragmentCache
    {
        private readonly record struct Fragment(long Version, UIElementRect Input, int Start, int Count);
        private Dictionary<VisualElement, Fragment> _entries = new(ReferenceEqualityComparer.Instance);
        private Dictionary<VisualElement, Fragment> _next = new(ReferenceEqualityComparer.Instance);
        private UIElementLayoutSnapshot? _snapshot, _source;
        private VisualElement? _styleRoot, _nextStyleRoot;
        private long _styleVersion, _nextStyleVersion, _generation, _nextGeneration;
        private int _width, _height, _nextWidth, _nextHeight;
        private Fix64 _scale, _nextScale;
        internal VisualElement? Root { get; private set; }
        internal int ReusedNodes { get; private set; }
        internal bool InUse;

        internal void Begin(VisualElement root, int width, int height, Fix64 scale, UIElementLayoutSnapshot? previous)
        {
            Root = root; ReusedNodes = 0; _next.Clear();
            var styleRoot = root;
            while (styleRoot.parent is not null) styleRoot = styleRoot.parent;
            _nextStyleRoot = styleRoot; _nextStyleVersion = styleRoot.styleApplicationVersion;
            _nextGeneration = BAsset.cacheGeneration; _nextWidth = width; _nextHeight = height; _nextScale = scale;
            _source = ReferenceEquals(previous, _snapshot) && ReferenceEquals(styleRoot, _styleRoot) &&
                _styleVersion == _nextStyleVersion && _generation == _nextGeneration &&
                _width == width && _height == height && _scale == scale ? previous : null;
        }

        internal bool TryAppend(VisualElement element, UIElementRect input, List<UIElementLayout> output,
            List<int> scrollAncestors)
        {
            if (_source is null || !_entries.TryGetValue(element, out var entry) ||
                entry.Version != element.layoutVersion || entry.Input != input) return false;
            var start = output.Count;
            for (var index = entry.Start; index < entry.Start + entry.Count; index++)
            {
                output.Add(_source[index]);
                scrollAncestors.Add(-1);
            }
            _next.Add(element, entry with { Start = start });
            ReusedNodes += entry.Count;
            return true;
        }

        internal void Record(VisualElement element, UIElementRect input, int start, List<UIElementLayout> output)
        {
            var count = output.Count - start;
            // A leaf's normal Layout is already cheaper than dictionary lookup
            // plus metadata. Amortize the additional cache work over a subtree.
            if (count < 8) return;
            for (var index = start; index < output.Count; index++)
            {
                var node = output[index].Element;
                // Explicit boxes avoid font/intrinsic dependencies on ancestors.
                // Relative expressions can be resolved by other subtree layouts;
                // inherited inline text alignment can alter html-inline packing.
                if (node is ScrollView || node.style.hasContextDependentLengths ||
                    node.style.width <= 0 ||
                    node.style.height <= 0 ||
                    node.ClassListContains("html-inline")) return;
                // Reused fragments publish scrollAncestors as -1 because the
                // current pass may have different scroller indices. Reject a
                // fragment that contains a scroll descendant/ancestor rather
                // than applying a stale translation to its children.
                for (var ancestor = node.parent; ancestor is not null && !ReferenceEquals(ancestor, element); ancestor = ancestor.parent)
                    if (ancestor is ScrollView) return;
            }
            _next.Add(element, new(element.layoutVersion, input, start, count));
        }

        internal void Complete(UIElementLayoutSnapshot snapshot)
        {
            (_entries, _next) = (_next, _entries);
            _snapshot = snapshot; _styleRoot = _nextStyleRoot; _styleVersion = _nextStyleVersion;
            _generation = _nextGeneration; _width = _nextWidth; _height = _nextHeight; _scale = _nextScale;
        }

        internal void End()
        {
            _next.Clear(); _source = null; Root = null; _nextStyleRoot = null; InUse = false;
        }
    }
}
