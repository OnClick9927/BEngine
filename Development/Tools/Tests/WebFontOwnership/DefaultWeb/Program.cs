using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.UIElements;

if (OperatingSystem.IsBrowser()) await Probe.Run();

[SupportedOSPlatform("browser")]
static partial class Probe
{
    [JSImport("globalThis.logProbeResult")] private static partial void Report(string result);
    [JSImport("globalThis.probeBaseUrl")] private static partial string BaseUrl();
    public static async Task Run()
    {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        try
        {
            WebPlatform.Initialize();
            Check(RuntimeServiceContributions.GetDefault<IPlatformFontRasterizer>() is null,
                "Default host does not activate the optional native font capability");
            using var http = new HttpClient { BaseAddress = new Uri(BaseUrl()) };
            var sources = new[] { new DynamicFontSource("Arial", await http.GetByteArrayAsync("fonts/arial.ttf")) };
            using var font = new AdvancedFontRuntime(sources);
            foreach (var text in new[] { "BEngine office fi 123", "العربية 123", "שלום עולם", "line one\nline two" })
            {
                var measure = font.Measure(text, 24, 320, true, TextDirectionMode.Auto);
                Check(measure.Width > 0 && measure.Height > 0 && measure.GlyphCount > 0, "Default Skia shapes " + text);
                var image = font.Render(text, 320, 128, 24, true, TextDirectionMode.Auto, new(230, 240, 250));
                Check(image.Pixels.Length == 320 * 128 * 4 && image.Pixels.Any(x => x != 0), "Default Skia renders " + text);
            }
            Report(JsonSerializer.Serialize(new ProbeResult { Passed = true, Count = checks.Count, Checks = checks }, ProbeJson.Default.ProbeResult));
        }
        catch (Exception error) { Report(JsonSerializer.Serialize(new ProbeResult { Passed = false, Count = checks.Count, Checks = checks, Error = error.ToString() }, ProbeJson.Default.ProbeResult)); }
    }
}
