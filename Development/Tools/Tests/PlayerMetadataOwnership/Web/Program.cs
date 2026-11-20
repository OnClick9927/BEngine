using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using BEngine.Platform;
if(OperatingSystem.IsBrowser()) Probe.Run();
[SupportedOSPlatform("browser")]
static partial class Probe
{
    [JSImport("globalThis.logProbeResult")] private static partial void Report(string result);
    public static void Run()
    {
        try { WebPlatform.Initialize();var checks=MetadataCoreProbe.Run("/metadata-probe");Report(JsonSerializer.Serialize(new{Passed=true,Count=checks.Count,Checks=checks})); }
        catch(Exception error){Report(JsonSerializer.Serialize(new{Passed=false,Error=error.ToString()}));}
    }
}
