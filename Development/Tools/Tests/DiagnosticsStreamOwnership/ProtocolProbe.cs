using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.Diagnostics;

public static class DiagnosticsProtocolProbe
{
    public static List<string> Run(int minimumHeapObjects = 0)
    {
        var checks = new List<string>();
        void Check(bool value, string name)
        { if (!value) throw new Exception(name); checks.Add(name); }
        const string token = "diagnostics-probe-token";
        using var server = RemoteDiagnosticsServer.Start("127.0.0.1", 0, token);
        var port = server.EndPoint.Port;
        Check(port > 0 && server.EndPoint.Address == "127.0.0.1", "The host reports the bound ephemeral endpoint");
        var client = Wait(RemoteDiagnosticsClient.ConnectAsync("localhost", port, token));
        try
        {
            Check(client.Hello.Protocol == RemoteDiagnosticsProtocol.Version && client.Hello.ProcessId == Environment.ProcessId,
                "The real DNS stream preserves the hello protocol and process identity");
            var sample = Wait(client.CaptureSampleAsync());
            Check(sample.ProcessId == Environment.ProcessId && sample.ProcessName.Length > 0,
                "The real connection captures a sample on the owning engine thread");
            var heap = Wait(client.CaptureHeapAsync());
            Check(heap is not null && heap.Objects.Length >= minimumHeapObjects,
                "Heap capture round trips through the original protocol");
            var pending = Enumerable.Range(0, 4).Select(_ => client.CaptureSampleAsync()).ToArray();
            Wait(Task.WhenAll(pending));
            Check(pending.All(x => x.Result.ProcessId == Environment.ProcessId),
                "Concurrent client requests remain serialized without losing responses");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Check(Fails<OperationCanceledException>(() => Wait(client.CaptureSampleAsync(canceled.Token))),
                "Canceled queued requests preserve cancellation");
            Check(Wait(client.CaptureSampleAsync()).ProcessId == Environment.ProcessId,
                "Cancellation before a request does not corrupt the next response");
        }
        finally { Wait(client.DisposeAsync().AsTask()); }
        Wait(client.DisposeAsync().AsTask());
        Check(Fails<ObjectDisposedException>(() => Wait(client.CaptureSampleAsync())),
            "Disposed clients reject requests and tolerate repeated disposal");
        Check(Fails<InvalidOperationException>(() => Wait(RemoteDiagnosticsClient.ConnectAsync("localhost", port, "wrong"))),
            "Wrong authentication is rejected by the diagnostic protocol");
        using (var raw = new TcpClient())
        {
            Wait(raw.ConnectAsync(IPAddress.Loopback, port));
            using var reader = new StreamReader(raw.GetStream(), Encoding.UTF8, false, 4096, true);
            using var writer = new StreamWriter(raw.GetStream(), new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            JsonElement Exchange(string text)
            {
                Wait(writer.WriteLineAsync(text));
                var line = Wait(reader.ReadLineAsync()) ?? throw new Exception("Unexpected protocol EOF");
                using var document = JsonDocument.Parse(line); return document.RootElement.Clone();
            }
            var invalid = Exchange("{");
            Check(!invalid.GetProperty("ok").GetBoolean() && invalid.GetProperty("error").GetString()!.Length > 0,
                "Malformed JSON returns a protocol error without closing the connection");
            var version = Exchange(JsonSerializer.Serialize(new { protocol = 999, operation = "hello", token }));
            Check(!version.GetProperty("ok").GetBoolean() && version.GetProperty("error").GetString() == "Diagnostics protocol 1 is required.",
                "Incompatible protocol versions retain their exact rejection");
            var unknown = Exchange(JsonSerializer.Serialize(new { protocol = 1, operation = "unrecognized", token }));
            Check(!unknown.GetProperty("ok").GetBoolean() && unknown.GetProperty("error").GetString() == "Unknown operation 'unrecognized'.",
                "Unknown operations retain their exact rejection");
            var hello = Exchange(JsonSerializer.Serialize(new { protocol = 1, operation = "hello", token }));
            Check(hello.GetProperty("ok").GetBoolean() && hello.GetProperty("data").GetProperty("protocol").GetInt32() == 1,
                "A valid request still succeeds after recoverable protocol errors");
        }
        Check(Fails<ArgumentException>(() => RemoteDiagnosticsServer.Start("0.0.0.0", 0, "short")),
            "Non-loopback listeners retain the authentication policy");
        Check(Fails<ArgumentException>(() => RemoteDiagnosticsServer.Start("invalid", 0, token)),
            "Invalid bind addresses are rejected before host I/O");
        Check(Fails<ArgumentOutOfRangeException>(() => RemoteDiagnosticsServer.Start("127.0.0.1", -1, token)),
            "Invalid listener ports are rejected before host I/O");
        Check(Fails<ArgumentOutOfRangeException>(() => Wait(RemoteDiagnosticsClient.ConnectAsync("localhost", 0, token))),
            "Invalid destination ports are rejected before host I/O");
        server.Dispose(); server.Dispose();
        for (var i = 0; i < 8; i++) BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(2));
        using var replacement = RemoteDiagnosticsServer.Start("127.0.0.1", port, token);
        var reconnected = Wait(RemoteDiagnosticsClient.ConnectAsync("localhost", port, token));
        Check(reconnected.Hello.Protocol == 1, "Stop releases the listener so the same port can restart");
        Wait(reconnected.DisposeAsync().AsTask());
        return checks;
    }

    public static void Wait(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed.TotalSeconds < 12)
        { BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(2)); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("Diagnostic stream probe timed out.");
        task.GetAwaiter().GetResult();
    }
    public static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    public static bool Fails<T>(Action action) where T : Exception
    { try { action(); return false; } catch (T) { return true; } }
}
