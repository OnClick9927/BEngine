using System.Net;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using BEngine.Diagnostics;
using BEngine.Platform;
using BEngine.Platform.DotNet;

if (OperatingSystem.IsBrowser()) await Probe.Run();

[SupportedOSPlatform("browser")]
static partial class Probe
{
    [JSImport("globalThis.logProbeResult")] private static partial void Report(string result);
    public static async Task Run()
    {
        try
        {
            WebPlatform.Initialize();
            var checks = new List<string>();
            void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
            var factory = DotNetTcpStreamFactory.Shared;
            Check(Fails<PlatformNotSupportedException>(() => factory.Listen(new(IPAddress.Loopback, 0))),
                "Browser native listener rejects before allocating a system socket");
            try { await factory.ConnectAsync("localhost", 1234); throw new Exception("Browser native connect was accepted"); }
            catch (PlatformNotSupportedException) { checks.Add("Browser native connect rejects before opening a system socket"); }
            Check(Fails<PlatformNotSupportedException>(() => RemoteDiagnosticsServer.Start("127.0.0.1", 0, "")),
                "Optional diagnostic server retains browser capability rejection");
            try { await RemoteDiagnosticsClient.ConnectAsync("localhost", 1234, ""); throw new Exception("Browser diagnostic connect was accepted"); }
            catch (PlatformNotSupportedException) { checks.Add("Optional diagnostic client retains browser capability rejection"); }
            Check(RemoteDiagnosticsProtocol.Version == 1 && RemoteDiagnosticsProtocol.MaximumMessageBytes == 16 * 1024 * 1024,
                "The browser package retains the same protocol constants");
            Check(!typeof(RemoteDiagnosticsProtocol).Assembly.GetReferencedAssemblies().Any(x => x.Name == "BEngine.Networking"),
                "Diagnostic stream composition does not force the domain networking package");
            Report(JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks }));
        }
        catch (Exception error) { Report(JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() })); }
    }
    private static bool Fails<T>(Action action) where T : Exception
    { try { action(); return false; } catch (T) { return true; } }
}
