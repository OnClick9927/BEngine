using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using BEngine;
using BEngine.Platform;

if (OperatingSystem.IsBrowser()) Probe.Run();

[SupportedOSPlatform("browser")]
static partial class Probe
{
    [JSImport("globalThis.logProbeResult")] private static partial void Report(string result);
    public static void Run()
    {
        try
        {
            WebPlatform.Initialize();
            var checks = LoggingProbe.Run("/logging-probe", RuntimeServices.GetRequiredService<FileUtility>(),
                RuntimeServices.GetRequiredService<PathUtility>(), RuntimeServices.GetRequiredService<PlatformUtility>(),
                RuntimeServices.GetRequiredService<IPlatformService>());
            checks.AddRange(ModuleManifestProbe.Run("/module-manifests"));
            Report(JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks }));
        }
        catch (Exception exception)
        {
            Report(JsonSerializer.Serialize(new { Passed = false, Error = exception.ToString() }));
        }
    }
}
