namespace BEngine.UIElements;

public static partial class UIElementLayoutEngine
{
    // Reused only within a full layout pass. Relative CSS lengths are resolved against
    // different intrinsic/allocated constraints, so their ancestors cannot be memoized.
    private sealed class MeasurementContext
    {
        private struct Entry
        {
            internal bool Stable;
            internal int Axes;
            internal float Font;
            internal Fix64 Width, Height;
        }
        private readonly Dictionary<VisualElement, Entry> _entries = new(ReferenceEqualityComparer.Instance);
        internal void Clear() => _entries.Clear();
        internal bool IsStable(VisualElement element)
        {
            if (_entries.TryGetValue(element, out var entry)) return entry.Stable;
            var stable = !element.style.hasContextDependentLengths;
            if (stable)
                for (var i = 0; i < element.Children.Count; i++)
                {
                    var child = element.Children[i];
                    if (child.visible && child.style.display != DisplayStyle.None && child.style.position != Position.Absolute && !IsStable(child))
                    { stable = false; break; }
                }
            _entries[element] = new() { Stable = stable }; return stable;
        }
        internal bool TryGet(VisualElement element, bool horizontal, float font, out Fix64 size)
        {
            var entry = _entries[element]; size = horizontal ? entry.Width : entry.Height;
            return entry.Font == font && (entry.Axes & (horizontal ? 1 : 2)) != 0;
        }
        internal void Store(VisualElement element, bool horizontal, float font, Fix64 size)
        {
            var entry = _entries[element];
            if (entry.Font != font) entry.Axes = 0;
            entry.Font = font; entry.Axes |= horizontal ? 1 : 2;
            if (horizontal) entry.Width = size; else entry.Height = size;
            _entries[element] = entry;
        }
    }
}
