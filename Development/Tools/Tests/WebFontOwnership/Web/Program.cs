using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.UIElements;
using Microsoft.Extensions.DependencyInjection;

if (OperatingSystem.IsBrowser()) await Probe.Run();

[SupportedOSPlatform("browser")]
static partial class Probe
{
    private static long _nativeAllocated, _legacyAllocated, _allocationProbeBytes;
    private static double _nativeMilliseconds, _legacyMilliseconds;
    [JSImport("globalThis.logProbeResult")] private static partial void Report(string result);
    [JSImport("globalThis.probeBaseUrl")] private static partial string BaseUrl();
    private static readonly List<string> Checks = [];
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Checks.Add(name); }
    private static bool Fails<T>(Action action) where T : Exception
    { try { action(); return false; } catch (T) { return true; } }

    public static async Task Run()
    {
        try
        {
            // The real shared host's module initializer selects the optional native capability.
            Check(ReferenceEquals(RuntimeServices.GetRequiredService<IPlatformFontRasterizer>(), WebFontRasterizer.Shared),
                "Shared FreeType host registers the font capability before platform startup");
            WebPlatform.Initialize();
            var services = new ServiceCollection();
            RuntimeServiceContributions.Configure(services);
            using (var provider = services.BuildServiceProvider())
                Check(ReferenceEquals(provider.GetRequiredService<IPlatformFontRasterizer>(), WebFontRasterizer.Shared),
                    "Composed service scopes resolve the same optional platform capability");
            using var http = new HttpClient { BaseAddress = new Uri(BaseUrl()) };
            var sources = new[] {
                new DynamicFontSource("Arial", await http.GetByteArrayAsync("fonts/arial.ttf")),
                new DynamicFontSource("SimHei", await http.GetByteArrayAsync("fonts/simhei.ttf")),
                new DynamicFontSource("Symbols", await http.GetByteArrayAsync("fonts/seguisym.ttf")) };
            using var legacy = new LegacyWebAdvancedFontBackend(sources);
            using var current = new WebAdvancedFontBackend(sources);
            Check(current.FontCount == legacy.FontCount && current.FontCount == 3, "Fallback chain retains every face");
            Check(Marshal.SizeOf<AdvancedGlyphPoint>() == 2 * sizeof(float), "UI point layout matches interleaved platform coordinates");
            Check(Marshal.SizeOf<PlatformFontMetrics>() == 3 * sizeof(float), "Platform metrics layout matches the native ABI");
            var texts = new[] { "BEngine office fi ffi 123", "AVATAR To WA", "العربية 123", "שלום עולם", "中文测试：你好世界", "a\u0301 e\u0308", "☀ ★ ♫ → ⌘", "😀 🚂", "\uD800", "\uDC00", "", "line one\nline two", "a\t b\r\nc" };
            for (var font = 0; font < sources.Length; font++)
            {
                foreach (var size in new[] { 1f, 14f, 23.5f, 64f, 512f })
                    Check(current.GetMetrics(font, size) == legacy.GetMetrics(font, size), $"Face {font}: metrics match at {size}");
                foreach (var text in texts)
                {
                    Check(current.ContainsGlyphs(font, text) == legacy.ContainsGlyphs(font, text), $"Face {font}: coverage matches {JsonSerializer.Serialize(text, ProbeJson.Default.String)}");
                    Check(current.GetGlyphs(font, text).SequenceEqual(legacy.GetGlyphs(font, text)), $"Face {font}: nominal glyphs match {JsonSerializer.Serialize(text, ProbeJson.Default.String)}");
                    using var oldBuffer = Buffer(text); using var newBuffer = Buffer(text);
                    var oldRun = legacy.Shape(font, oldBuffer, 23.5f); var newRun = current.Shape(font, newBuffer, 23.5f);
                    Check(newRun.Width == oldRun.Width && newRun.Codepoints.SequenceEqual(oldRun.Codepoints) &&
                        newRun.Clusters.SequenceEqual(oldRun.Clusters) && newRun.Points.SequenceEqual(oldRun.Points),
                        $"Face {font}: OpenType shaping matches {JsonSerializer.Serialize(text, ProbeJson.Default.String)}");
                    var oldImage = legacy.Render([new(font, -2, 50, oldRun)], 640, 128, 23.5f, new(31, 128, 220, 170));
                    var newImage = current.Render([new(font, -2, 50, newRun)], 640, 128, 23.5f, new(31, 128, 220, 170));
                    Check(newImage.PreserveColors == oldImage.PreserveColors && newImage.Pixels.SequenceEqual(oldImage.Pixels),
                        $"Face {font}: clipped premultiplied pixels match {JsonSerializer.Serialize(text, ProbeJson.Default.String)}");
                }
            }
            using (var oldLayout = new AdvancedFontRuntime(sources, new LegacyWebAdvancedFontBackend(sources)))
            using (var newLayout = new AdvancedFontRuntime(sources, new WebAdvancedFontBackend(sources)))
                foreach (var text in texts)
                    foreach (var direction in new[] { TextDirectionMode.Auto, TextDirectionMode.RightToLeft })
                        foreach (var width in new[] { 80, 320 })
                        {
                            Check(newLayout.Measure(text, 23.5f, width, true, direction) == oldLayout.Measure(text, 23.5f, width, true, direction),
                                $"Layout metrics, fallback, bidi and wrapping match ({width}, {direction}, {JsonSerializer.Serialize(text, ProbeJson.Default.String)})");
                            var a = oldLayout.Render(text, width, 128, 23.5f, true, direction, new(250, 230, 210));
                            var b = newLayout.Render(text, width, 128, 23.5f, true, direction, new(250, 230, 210));
                            Check(a.PreserveColors == b.PreserveColors && a.Pixels.SequenceEqual(b.Pixels),
                                $"Layout pixels match ({width}, {direction}, {JsonSerializer.Serialize(text, ProbeJson.Default.String)})");
                        }
            var rasterizer = WebFontRasterizer.Shared;
            Check(Fails<InvalidDataException>(() => rasterizer.OpenFace([])), "Empty font rejected before native access");
            Check(Fails<InvalidDataException>(() => rasterizer.OpenFace(new byte[12])), "Malformed OpenType data rejected");
            var counting = new CountingRasterizer();
            using (var injected = new ServiceCollection().AddSingleton<IPlatformFontRasterizer>(counting).BuildServiceProvider())
            {
                PlatformRuntime.Initialize(injected);
                try
                {
                    Check(Fails<InvalidDataException>(() => new WebAdvancedFontBackend([sources[0], new("invalid", new byte[12])])),
                        "Later invalid fallback aborts UI backend construction");
                    Check(counting.OpenCalls == 2 && counting.ActiveFaces == 0, "Partial UI construction releases all previously opened native faces");
                }
                finally { WebPlatform.Initialize(); }
            }
            var ownedBytes = sources[0].Data.ToArray();
            using (var face = rasterizer.OpenFace(ownedBytes))
            {
                Array.Clear(ownedBytes);
                Check(face.GetGlyph('B') != 0 && face.GetMetrics(24).Descent > 0, "Platform face owns an independent font copy");
                var glyphs = new uint[] { face.GetGlyph('B') }; var positions = new float[] { 0, 0 }; var pixels = new byte[64 * 64 * 4];
                face.DrawRun(24, glyphs, positions, 2, 30, 0xffffffff, pixels, 64, 64);
                Check(pixels.Any(x => x != 0), "Independent non-UI consumer renders using borrowed spans");
                Check(Fails<ArgumentException>(() => face.DrawRun(24, glyphs, [], 0, 20, 0xffffffff, pixels, 64, 64)), "Missing coordinate pairs rejected");
                Check(Fails<ArgumentException>(() => face.DrawRun(24, glyphs, positions, 0, 20, 0xffffffff, new byte[4], 64, 64)), "Undersized native output buffer rejected");
                Check(Fails<ArgumentException>(() => face.DrawRun(24, glyphs, positions, 0, 20, 0xffffffff, pixels, -1, 64)), "Invalid destination dimensions rejected");
                Check(Fails<InvalidDataException>(() => face.GetMetrics(float.NaN)), "Invalid native metrics size rejected");
                face.DrawRun(24, [], [], 0, 20, 0xffffffff, pixels, 64, 64);
                Checks.Add("Empty glyph run is a safe no-op");
                for (var i = 0; i < 5000; i++) { face.GetMetrics(24); legacy.GetMetrics(0, 24); }
                var probeBefore = GC.GetAllocatedBytesForCurrentThread();
                var knownAllocation = new byte[4096];
                _allocationProbeBytes = GC.GetAllocatedBytesForCurrentThread() - probeBefore;
                GC.KeepAlive(knownAllocation);
                var timer = Stopwatch.StartNew();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 3000; i++) face.GetMetrics(24);
                var nativeMilliseconds = timer.Elapsed.TotalMilliseconds;
                var nativeAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                _nativeAllocated = nativeAllocated; _nativeMilliseconds = nativeMilliseconds;
                allocated = GC.GetAllocatedBytesForCurrentThread(); timer.Restart();
                for (var i = 0; i < 3000; i++) legacy.GetMetrics(0, 24);
                var legacyMilliseconds = timer.Elapsed.TotalMilliseconds;
                var legacyAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                _legacyAllocated = legacyAllocated; _legacyMilliseconds = legacyMilliseconds;
                if (_allocationProbeBytes >= 4096)
                    Check(nativeAllocated < legacyAllocated, "Native metrics allocates less than the legacy array-marshaling path");
                face.Dispose(); face.Dispose();
                Check(Fails<ObjectDisposedException>(() => face.GetGlyph('B')), "Face disposal is idempotent and invalidates native access");
                current.Dispose(); current.Dispose();
                Check(Fails<ObjectDisposedException>(() => current.GetMetrics(0, 24)), "UI backend disposal remains idempotent");
                Report(JsonSerializer.Serialize(new ProbeResult { Passed = true, Count = Checks.Count, Checks = Checks,
                    MetricsCalls = 3000, NativeAllocated = nativeAllocated, LegacyAllocated = legacyAllocated, NativeMilliseconds = nativeMilliseconds, LegacyMilliseconds = legacyMilliseconds,
                    AllocationCounterSupported = _allocationProbeBytes >= 4096, AllocationProbeBytes = _allocationProbeBytes }, ProbeJson.Default.ProbeResult));
            }
        }
        catch (Exception error) { Report(JsonSerializer.Serialize(new ProbeResult { Passed = false, Count = Checks.Count, Checks = Checks, Error = error.ToString(),
            NativeAllocated = _nativeAllocated, LegacyAllocated = _legacyAllocated, NativeMilliseconds = _nativeMilliseconds, LegacyMilliseconds = _legacyMilliseconds,
            AllocationCounterSupported = _allocationProbeBytes >= 4096, AllocationProbeBytes = _allocationProbeBytes }, ProbeJson.Default.ProbeResult)); }
    }
    private static HarfBuzzSharp.Buffer Buffer(string text)
    { var buffer = new HarfBuzzSharp.Buffer(); buffer.AddUtf16(text); buffer.GuessSegmentProperties(); return buffer; }

    private sealed class CountingRasterizer : IPlatformFontRasterizer
    {
        public int OpenCalls, ActiveFaces;
        public IPlatformFontFace OpenFace(ReadOnlySpan<byte> data)
        {
            OpenCalls++;
            var face = WebFontRasterizer.Shared.OpenFace(data);
            ActiveFaces++;
            return new TrackedFace(this, face);
        }
        private sealed class TrackedFace(CountingRasterizer owner, IPlatformFontFace face) : IPlatformFontFace
        {
            private bool _disposed;
            public PlatformFontMetrics GetMetrics(float size) => face.GetMetrics(size);
            public uint GetGlyph(uint codepoint) => face.GetGlyph(codepoint);
            public void DrawRun(float size, ReadOnlySpan<uint> glyphs, ReadOnlySpan<float> positions, float x,
                float baseline, uint rgba, Span<byte> pixels, int width, int height) =>
                face.DrawRun(size, glyphs, positions, x, baseline, rgba, pixels, width, height);
            public void Dispose() { if (_disposed) return; _disposed = true; face.Dispose(); owner.ActiveFaces--; }
        }
    }
}
