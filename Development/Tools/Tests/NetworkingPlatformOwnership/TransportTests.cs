using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BEngine.Networking;
#if !LEGACY
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.DependencyInjection;
#endif

internal static class TransportTests
{
    private static readonly List<string> Checks = [];
    private static readonly SortedDictionary<string, string> Payloads = [];
    private static readonly List<long[]> IdleAllocationSamples = [];
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Checks.Add(name); }
    private static void Pump(INetworkOperation operation, Sink? sink = null)
    {
        var timer = Stopwatch.StartNew();
        while (operation.Status == NetworkOperationStatus.Running && timer.Elapsed.TotalSeconds < 12)
        { sink?.BeginPoll(); operation.Poll(); Thread.Sleep(1); }
        Check(operation.Status != NetworkOperationStatus.Running, "Operation reaches a terminal state");
    }
    private static void Success(INetworkOperation operation)
    { Pump(operation); Check(operation.Status == NetworkOperationStatus.Succeeded, "Operation succeeds: " + operation.Exception); }
    private static void Remember(string name, byte[] actual, byte[] expected)
    { Check(actual.AsSpan().SequenceEqual(expected), name + " payload"); Payloads[name] = Convert.ToHexString(SHA256.HashData(actual)); }

    public static int Main(string[] args)
    {
        try
        {
#if !LEGACY
            DotNetPlatformServices.Initialize();
            var client = (Lazy<System.Net.Http.HttpClient>)typeof(DotNetHttpTransport)
                .GetField("SharedClient", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            Check(!client.IsValueCreated, "Host registration does not allocate an unused HttpClient");
#endif
            using var server = new LoopbackServer();
#if LEGACY
            IHttpTransport http = BEngine.Platform.DotNet.DotNetHttpTransport.Shared;
#else
            IHttpTransport http = PlatformHttpTransport.Shared;
#endif
            ExerciseHttp(http, server, "http");
            ExerciseHttp(SocketHttpTransport.Shared, server, "parser");
            ExerciseTcp(); ExerciseUdp(); ExerciseWebSocket(server);
#if !LEGACY
            using (var request = HttpNetworkRequest.Get(server.Url + "fixed"))
            {
                var task = request.SendAsync(); var timer = Stopwatch.StartNew();
                while (!task.IsCompleted && timer.Elapsed.TotalSeconds < 12)
                { BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(2)); Thread.Sleep(1); }
                task.GetAwaiter().GetResult();
                Check(request.IsSuccess && request.DownloadProgress == 1, "Default BTask request reaches complete progress");
                Remember("domain", request.DownloadData!, LoopbackServer.Data);
            }
            Check(NetworkMemorySettings.activeRequests == 0 && NetworkMemorySettings.bufferedBytes == 0, "Completed domain request releases its request slot and response budget");
            AdapterTests.Run(Check);
            RuntimeServiceContributions.UnregisterAssembly(typeof(HttpNetworkRequest).Assembly);
            Check(RuntimeServiceContributions.GetDefault<IHttpTransport>() is null,
                "Domain registration retires with the network assembly");
            Check(PlatformRuntime.GetService<IPlatformHttpTransport>() is not null,
                "Domain retirement preserves the host transport");
#endif
            File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = true, Count = Checks.Count,
                Details = Checks, Payloads, IdleAllocationSamples }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("TRANSPORT_TESTS_OK|" + Checks.Count); return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = false, Error = error.ToString(), Details = Checks }));
            Console.Error.WriteLine(error); return 1;
        }
    }

    private static void ExerciseHttp(IHttpTransport transport, LoopbackServer server, string name)
    {
        foreach (var path in new[] { "fixed", "chunked", "empty", "head", "bad-length" })
        {
            var sink = new Sink();
            using var operation = transport.Send(new(new Uri(server.Url + path),
                path == "head" ? HttpRequestMethod.Head : HttpRequestMethod.Get,
                new Dictionary<string, string>(), null, null), sink);
            Check(sink.Bytes.Length == 0, name + " callbacks wait for owner Poll");
            Pump(operation, sink);
            Check(sink.OwnerThreadOnly && sink.HeaderCalls == 1, name + " callbacks stay on the owner thread");
            // The existing unknown-length stream can finish one pending 64 KiB
            // read after crossing the one-MiB allowance. Preserve and measure it.
            var allowance = 1024 * 1024 + (path == "chunked" ? 64 * 1024 : 0);
            Check(sink.MaximumBytesPerPoll <= allowance, name + " bounded per-Poll delivery");
            if (path == "bad-length")
            { Check(operation.Status == NetworkOperationStatus.Failed && operation.Exception is not null, name + " rejects truncated declared length"); continue; }
            Check(operation.Status == NetworkOperationStatus.Succeeded, name + " " + path + " succeeds: " + operation.Exception);
            var expected = path is "empty" or "head" ? Array.Empty<byte>() : LoopbackServer.Data;
            Remember(name + "/" + path, sink.Bytes.ToArray(), expected);
            Check(operation.DownloadedBytes == expected.Length, name + " byte progress is exact");
            Check(operation.Response is not null && sink.Response == operation.Response, name + " response metadata preserved");
            if (path == "head") Check(operation.ContentLength == 777, name + " HEAD preserves declared metadata without downloading a body");
            var samples = new long[6];
            for (var i = 0; i < samples.Length; i++) samples[i] = MeasureIdle(operation);
            IdleAllocationSamples.Add(samples);
            Check(samples[^1] == 0, name + " warm completed Poll is allocation free");
        }
        var upload = Encoding.UTF8.GetBytes("hello native boundary"); var uploaded = new Sink();
        using (var operation = transport.Send(new(new Uri(server.Url + "echo"), HttpRequestMethod.Post,
            new Dictionary<string, string> { ["X-Probe"] = "owner" }, upload, "application/probe") { BypassCache = true }, uploaded))
        { Success(operation); Remember(name + "/upload", uploaded.Bytes.ToArray(), upload); }
        var canceled = new Sink();
        using (var operation = transport.Send(new(new Uri(server.Url + "stall"), HttpRequestMethod.Get,
            new Dictionary<string, string>(), null, null), canceled))
        {
            var timer = Stopwatch.StartNew();
            while (operation.Response is null && timer.Elapsed.TotalSeconds < 4) { operation.Poll(); Thread.Sleep(1); }
            Check(operation.Response is not null, name + " streaming headers arrive before body completion");
            operation.Abort(); Check(operation.Status == NetworkOperationStatus.Failed, name + " abort is terminal");
            operation.Dispose(); var count = canceled.HeaderCalls + canceled.DataCalls;
            for (var i = 0; i < 100; i++) operation.Poll();
            Check(canceled.HeaderCalls + canceled.DataCalls == count, name + " disposed operation emits no callbacks");
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static long MeasureIdle(INetworkOperation operation)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) operation.Poll();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void ExerciseTcp()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var echo = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(); var stream = accepted.GetStream();
            var data = new byte[65536]; await stream.ReadExactlyAsync(data); await stream.WriteAsync(data);
        });
        using var client = SocketTransportFactory.Shared.CreateTcpTransport(); client.NoDelay = true;
        using (var connect = client.Connect("127.0.0.1", endpoint.Port)) Success(connect);
        Check(client.IsConnected && client.NoDelay && client.RemoteEndPoint is IPEndPoint, "TCP connected endpoint and NoDelay preserved");
        var payload = LoopbackServer.Data[..65536];
        using (var send = client.Send(payload)) Success(send);
        var received = new byte[payload.Length]; var offset = 0;
        while (offset < received.Length)
        { using var receive = client.Receive(received.AsMemory(offset)); Success(receive); Check(receive.Result > 0, "TCP receives a nonempty block"); offset += receive.Result; }
        Remember("tcp", received, payload); echo.GetAwaiter().GetResult(); client.Close();
        Check(!client.IsConnected, "TCP close releases native handle");
    }

    private static void ExerciseUdp()
    {
        using var first = SocketTransportFactory.Shared.CreateUdpTransport(new(IPAddress.Loopback, 0));
        using var second = SocketTransportFactory.Shared.CreateUdpTransport(new(IPAddress.Loopback, 0));
        foreach (var data in new[] { Encoding.UTF8.GetBytes("datagram"), Array.Empty<byte>() })
        {
            using var receive = second.Receive();
            for (var i = 0; i < 10; i++) receive.Poll();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) receive.Poll();
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Pending UDP Poll is allocation free");
            using var send = first.Send(data, second.LocalEndPoint); Success(send); Success(receive);
            Check(send.Result == data.Length && receive.Result.RemoteEndPoint.Port == first.LocalEndPoint!.Port, "UDP length and sender endpoint preserved");
            Check(ReferenceEquals(receive.Result, receive.Result), "Repeated UDP result does not allocate a second packet");
            Remember("udp/" + data.Length, receive.Result.Data, data);
        }
        second.Connect("127.0.0.1", first.LocalEndPoint!.Port);
        using var returning = first.Receive(); using var response = second.Send(new byte[] { 8, 3, 1 });
        Success(response); Success(returning); Remember("udp/connected", returning.Result.Data, [8, 3, 1]);
    }

    private static void ExerciseWebSocket(LoopbackServer server)
    {
#if LEGACY
        using IWebSocketTransport socket = new BEngine.Platform.DotNet.DotNetWebSocketTransport();
#else
        using IWebSocketTransport socket = new PlatformWebSocketTransport();
#endif
        using (var connect = socket.Connect(new Uri(server.Url.Replace("http:", "ws:") + "ws"))) Success(connect);
        var data = Encoding.UTF8.GetBytes("WebSocket owner");
        using (var send = socket.Send(data, WebSocketMessageType.Text, true)) Success(send);
        var buffer = new byte[256]; using (var receive = socket.Receive(buffer))
        {
            Success(receive); Check(receive.Result.MessageType == WebSocketMessageType.Text && receive.Result.EndOfMessage, "WebSocket frame metadata preserved");
            Remember("websocket", buffer[..receive.Result.Count], data);
        }
        using (var close = socket.Close(WebSocketCloseStatus.NormalClosure, "done")) Success(close);
        Check(socket.State == WebSocketState.Closed, "WebSocket close handshake completes");
    }

    private sealed class Sink : IHttpDownloadHandler
    {
        private readonly int _owner = Environment.CurrentManagedThreadId;
        public MemoryStream Bytes { get; } = new();
        public int HeaderCalls, DataCalls, MaximumBytesPerPoll;
        private int _pollBytes;
        public bool OwnerThreadOnly = true;
        public HttpTransportResponse? Response;
        public void BeginPoll() => _pollBytes = 0;
        public void ReceiveResponse(HttpTransportResponse response)
        { OwnerThreadOnly &= Environment.CurrentManagedThreadId == _owner; HeaderCalls++; Response = response; }
        public void ReceiveData(ReadOnlyMemory<byte> data)
        { OwnerThreadOnly &= Environment.CurrentManagedThreadId == _owner; DataCalls++; _pollBytes += data.Length; MaximumBytesPerPoll = Math.Max(MaximumBytesPerPoll, _pollBytes); Bytes.Write(data.Span); }
    }

    private sealed class LoopbackServer : IDisposable
    {
        internal static readonly byte[] Data = Enumerable.Range(0, 1536 * 1024).Select(i => (byte)(i * 31 + 7)).ToArray();
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        public string Url { get; }
        public LoopbackServer()
        {
            _listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/";
            _loop = Task.Run(Accept);
        }
        private async Task Accept()
        {
            try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = Handle(client); } }
            catch (OperationCanceledException) { } catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task Handle(TcpClient client)
        {
            using (client)
            try
            {
                var stream = client.GetStream(); var header = new MemoryStream(); var one = new byte[1];
                while (header.Length < 65536)
                { if (await stream.ReadAsync(one, _stop.Token) == 0) return; header.WriteByte(one[0]); var b = header.GetBuffer(); var n = (int)header.Length; if (n >= 4 && b[n-4] == 13 && b[n-3] == 10 && b[n-2] == 13 && b[n-1] == 10) break; }
                var text = Encoding.ASCII.GetString(header.ToArray()); var lines = text.Split("\r\n"); var path = lines[0].Split(' ')[1];
                var fields = lines.Skip(1).Where(l => l.Contains(':')).Select(l => l.Split(':', 2)).ToDictionary(p => p[0], p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
                async Task Write(string value) => await stream.WriteAsync(Encoding.ASCII.GetBytes(value), _stop.Token);
                if (path == "/ws")
                {
                    var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(fields["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    await Write("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                    using var socket = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
                    var buffer = new byte[4096]; var message = await socket.ReceiveAsync(buffer.AsMemory(), _stop.Token);
                    await socket.SendAsync(buffer.AsMemory(0, message.Count), message.MessageType, message.EndOfMessage, _stop.Token);
                    await socket.ReceiveAsync(buffer.AsMemory(), _stop.Token);
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", _stop.Token); return;
                }
                if (path == "/head") { await Write("HTTP/1.1 200 OK\r\nContent-Length: 777\r\nConnection: close\r\n\r\n"); return; }
                if (path == "/empty") { await Write("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); return; }
                if (path == "/bad-length") { await Write("HTTP/1.1 200 OK\r\nContent-Length: 9\r\nConnection: close\r\n\r\nabc"); return; }
                if (path == "/stall") { await Write("HTTP/1.1 200 OK\r\nContent-Length: 1048576\r\nConnection: close\r\n\r\n"); await Task.Delay(3000, _stop.Token); return; }
                if (path == "/echo")
                {
                    var data = new byte[int.Parse(fields["Content-Length"])]; await stream.ReadExactlyAsync(data, _stop.Token);
                    if (fields["X-Probe"] != "owner" || fields["Content-Type"] != "application/probe") throw new Exception("Upload metadata changed");
                    await Write("HTTP/1.1 200 OK\r\nContent-Length: " + data.Length + "\r\nConnection: close\r\n\r\n"); await stream.WriteAsync(data, _stop.Token); return;
                }
                if (path == "/chunked")
                {
                    await Write("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
                    for (var i = 0; i < Data.Length; i += 4096) { var count = Math.Min(4096, Data.Length-i); await Write(count.ToString("X")+"\r\n"); await stream.WriteAsync(Data.AsMemory(i,count),_stop.Token); await Write("\r\n"); }
                    await Write("0\r\n\r\n"); return;
                }
                await Write("HTTP/1.1 200 OK\r\nContent-Length: " + Data.Length + "\r\nX-Owner: native\r\nConnection: close\r\n\r\n");
                for (var i = 0; i < Data.Length; i += 4096) await stream.WriteAsync(Data.AsMemory(i, Math.Min(4096, Data.Length-i)), _stop.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or SocketException or WebSocketException or ObjectDisposedException) { }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); _loop.GetAwaiter().GetResult(); _stop.Dispose(); }
    }
}
