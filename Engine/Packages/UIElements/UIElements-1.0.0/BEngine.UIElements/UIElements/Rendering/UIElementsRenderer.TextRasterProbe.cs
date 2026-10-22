using System.Diagnostics;

namespace BEngine.Rendering;

public sealed partial class UIElementsRenderer
{
    // Opt-in diagnostics retain a bounded ring of cache misses. Formatting,
    // sorting and log I/O happen only when the renderer is disposed.
    private sealed class TextRasterProbe
    {
        private readonly Miss[] _misses = new Miss[1024];
        private long _count;
        private readonly record struct Miss(long Started, long Rasterized, long Uploaded,
            string Element, BasicTextKey Key);

        internal static TextRasterProbe? Create() =>
            Environment.GetEnvironmentVariable("BENGINE_UI_TEXT_PROBE") == "1" ? new() : null;

        internal void Record(long started, long rasterized, long uploaded, string element, BasicTextKey key)
        {
            _misses[(int)(_count % _misses.Length)] = new(started, rasterized, uploaded, element, key);
            _count++;
        }

        internal void Report()
        {
            int count = (int)Math.Min(_count, _misses.Length);
            if (count == 0) return;
            var retained = _misses.AsSpan(0, count).ToArray();
            Array.Sort(retained, (a, b) => (b.Uploaded - b.Started).CompareTo(a.Uploaded - a.Started));
            Debug.Log($"BENGINE_UI_TEXT_PROBE|misses={_count}|retained={count}|stopwatchFrequency={Stopwatch.Frequency}");
            for (int i = 0; i < Math.Min(64, count); i++)
            {
                var miss = retained[i];
                Debug.Log(FormattableString.Invariant($"BENGINE_UI_TEXT_MISS|timestamp={miss.Started}|rasterMs={Stopwatch.GetElapsedTime(miss.Started, miss.Rasterized).TotalMilliseconds:F6}|uploadMs={Stopwatch.GetElapsedTime(miss.Rasterized, miss.Uploaded).TotalMilliseconds:F6}|element={Escape(miss.Element)}|width={miss.Key.Width}|height={miss.Key.Height}|fontSize={miss.Key.Size}|wrap={miss.Key.Wrap}|text={Escape(miss.Key.Text)}"));
            }
            _count = 0;
            Array.Clear(_misses);
        }

        private static string Escape(string value) => (value.Length > 256 ? value[..256] : value)
            .Replace("|", "¦", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
