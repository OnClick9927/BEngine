using System.Text.Json;
using System.Diagnostics;
using BEngine;
using BEngine.Diagnostics;
using BEngine.Platform.DotNet;

DotNetPlatformServices.Initialize();
try
{
    var checks = DiagnosticsProtocolProbe.Run();
    var idle = new List<object>();
    using (var server = RemoteDiagnosticsServer.Start("127.0.0.1", 0, ""))
    {
        for (var i = 0; i < 64; i++) BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(2));
        for (var pass = 0; pass < 5; pass++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < 1000; i++) BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(2));
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            idle.Add(new { Frames = 1000, AllocatedBytes = bytes, Milliseconds = elapsed });
        }
    }
#if !LEGACY
    checks.AddRange(FactoryProbe.Run());
#endif
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks, Idle = idle }));
    Console.WriteLine("DIAGNOSTIC_STREAM_OK|" + checks.Count);
    return 0;
}
catch (Exception error)
{
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() }));
    Console.Error.WriteLine(error); return 1;
}
