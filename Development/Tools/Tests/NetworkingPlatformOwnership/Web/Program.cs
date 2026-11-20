using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.Networking;
using BEngine.Platform;

if (OperatingSystem.IsBrowser()) await Probe.Run();

[SupportedOSPlatform("browser")]
internal static partial class Probe
{
    private static readonly List<string> Checks = [];
    private static readonly List<long[]> IdleAllocationSamples = [];
    [JSImport("globalThis.networkProbeResult")] private static partial void Report(string json);
    [JSImport("globalThis.networkProbeBase")] private static partial string Base();
    [JSImport("globalThis.networkProbeDiagnostics")] private static partial string Diagnostics();
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Checks.Add(name); }
    private static byte[] Data() { var data = new byte[1536 * 1024]; for (var i = 0; i < data.Length; i++) data[i] = (byte)(i * 31 + 17); return data; }
    private static async Task Pump(IHttpTransportOperation operation, Sink sink)
    {
        var timer = Stopwatch.StartNew();
        while (operation.Status == NetworkOperationStatus.Running && timer.Elapsed.TotalSeconds < 12)
        { sink.PollBytes = 0; operation.Poll(); await Task.Delay(1); }
        Check(operation.Status != NetworkOperationStatus.Running, "Browser request terminates");
    }
    internal static async Task Run()
    {
        try
        {
            WebPlatform.Initialize();
            Check(PlatformRuntime.GetRequiredService<IPlatformHttpTransport>() is WebHttpTransport,
                "Browser host registers its native bridge through a neutral contract");
            var expected = Data();
            foreach (var path in new[] { "fixed", "chunked", "head", "empty", "bad-length" })
            {
                var sink = new Sink();
                using var operation = PlatformHttpTransport.Shared.Send(new(new Uri(Base() + path),
                    path == "head" ? HttpRequestMethod.Head : HttpRequestMethod.Get, new Dictionary<string,string>(), null, null), sink);
                Check(sink.HeaderCalls == 0 && sink.Bytes.Length == 0, "Callbacks are deferred until Poll");
                await Pump(operation, sink);
                Check(sink.OwnerThreadOnly && sink.HeaderCalls == 1, "Header and body callbacks stay on the owner thread");
                Check(sink.MaximumBytes <= 1024 * 1024, "Browser normal Poll delivers at most one MiB");
                if (path == "bad-length")
                { Check(operation.Status == NetworkOperationStatus.Failed && operation.Exception is not null, "Truncated response fails without resubmission"); continue; }
                Check(operation.Status == NetworkOperationStatus.Succeeded, "Browser " + path + " succeeds: " + operation.Exception);
                Check(sink.Bytes.ToArray().AsSpan().SequenceEqual(path is "head" or "empty" ? [] : expected), "Browser " + path + " payload is exact");
                Check(operation.DownloadedBytes == sink.Bytes.Length && operation.Response == sink.Response, "Byte progress and response identity survive the adapter");
                if (path == "head") Check(operation.ContentLength == 777, "HEAD keeps declared metadata without fetching a body");
                // Warm the WebAssembly interpreter's loop/dispatch helpers too.
                var samples = new long[6];
                for (var i = 0; i < samples.Length; i++) samples[i] = MeasureIdle(operation);
                IdleAllocationSamples.Add(samples);
                Check(samples[^1] == 0, "Warm completed browser Poll allocates zero bytes");
            }
            var upload = Encoding.UTF8.GetBytes("browser native boundary"); var uploaded = new Sink();
            using (var operation = PlatformHttpTransport.Shared.Send(new(new Uri(Base() + "echo"), HttpRequestMethod.Post,
                new Dictionary<string,string> { ["X-Probe"] = "owner" }, upload, "application/probe") { BypassCache = true }, uploaded))
            { await Pump(operation, uploaded); Check(operation.Status == NetworkOperationStatus.Succeeded && uploaded.Bytes.ToArray().AsSpan().SequenceEqual(upload), "Browser upload, content type and custom headers survive"); }
            var canceled = new Sink();
            using (var operation = PlatformHttpTransport.Shared.Send(new(new Uri(Base() + "stall"), HttpRequestMethod.Get, new Dictionary<string,string>(), null, null), canceled))
            {
                var timer = Stopwatch.StartNew();
                while (operation.Response is null && timer.Elapsed.TotalSeconds < 4) { operation.Poll(); await Task.Delay(1); }
                Check(operation.Response is not null, "Streaming response publishes headers before completion");
                operation.Abort(); Check(operation.Status == NetworkOperationStatus.Failed, "Browser abort is terminal");
                operation.Dispose(); var calls = canceled.HeaderCalls + canceled.DataCalls;
                for (var i = 0; i < 100; i++) operation.Poll();
                Check(canceled.HeaderCalls + canceled.DataCalls == calls, "Disposed request emits no further callbacks");
            }
            using (var request = HttpNetworkRequest.Get(Base() + "fixed"))
            {
                var task = request.SendAsync(); var timer = Stopwatch.StartNew();
                while (!task.IsCompleted && timer.Elapsed.TotalSeconds < 12)
                { BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(2)); await Task.Delay(1); }
                task.GetAwaiter().GetResult();
                Check(request.IsSuccess && request.DownloadProgress == 1 && request.DownloadData!.AsSpan().SequenceEqual(expected), "Public BTask request uses the Web native transport");
            }
            Check(NetworkMemorySettings.activeRequests == 0 && NetworkMemorySettings.bufferedBytes == 0, "Domain request releases admission and response memory");
            await Task.Delay(100);
            using var diagnostics = JsonDocument.Parse(Diagnostics());
            var d = diagnostics.RootElement;
            Check(d.GetProperty("ActiveRequests").GetInt32() == 0, "Disposal releases all browser request handles");
            Check(d.GetProperty("Started").GetInt32() == 8, "Eight accepted requests start exactly once");
            Report(JsonSerializer.Serialize(new { Passed = true, Count = Checks.Count, Checks,
                PayloadSHA256 = Convert.ToHexString(SHA256.HashData(expected)), IdleAllocationSamples, Diagnostics = d.Clone() }));
        }
        catch (Exception error) { Report(JsonSerializer.Serialize(new { Passed = false, Error = error.ToString(), Checks, IdleAllocationSamples, Diagnostics = Diagnostics() })); }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static long MeasureIdle(IHttpTransportOperation operation)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) operation.Poll();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    private sealed class Sink : IHttpDownloadHandler
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public readonly MemoryStream Bytes = new();
        public bool OwnerThreadOnly = true;
        public int HeaderCalls, DataCalls, PollBytes, MaximumBytes;
        public HttpTransportResponse? Response;
        public void ReceiveResponse(HttpTransportResponse response) { OwnerThreadOnly &= _thread == Environment.CurrentManagedThreadId; HeaderCalls++; Response = response; }
        public void ReceiveData(ReadOnlyMemory<byte> data) { OwnerThreadOnly &= _thread == Environment.CurrentManagedThreadId; DataCalls++; PollBytes += data.Length; MaximumBytes = Math.Max(MaximumBytes, PollBytes); Bytes.Write(data.Span); }
    }
}
