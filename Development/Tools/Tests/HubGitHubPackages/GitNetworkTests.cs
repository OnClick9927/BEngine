using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BEngine.Launcher;

internal static class GitNetworkTests
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        var events = new ConcurrentQueue<string>();
        var progress = new GitHubProgressSink<string>(events.Enqueue);
        GitRepositorySnapshot Snapshot(GitCommandPolicy? policy = null)
        {
            var snapshot = new GitRepositorySnapshot(Path.Combine(root, "Temp", "HubGit", Guid.NewGuid().ToString("N")), progress, policy);
            Directory.CreateDirectory(snapshot.Root); return snapshot;
        }
        await using (var server = new Server(firstFailure: true))
        using (var snapshot = Snapshot(new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), TimeSpan.Zero)))
        {
            var output = await snapshot.RunAsync(["ls-remote", server.Url], default);
            check(output.Contains(new string('a', 40), StringComparison.Ordinal) && server.InfoRequests == 2,
                "A transient Git HTTP error retries and returns real Git references");
            check(events.Contains("Retrying Git transfer"), "The Git retry is visible in operation progress");
            var log = File.ReadAllText(Path.Combine(root, "Temp", "Logs", "HubGit.log"));
            check(log.Contains("Reading Git references", StringComparison.Ordinal) && log.Contains("503", StringComparison.Ordinal),
                "Git diagnostics retain the failed phase and original network error");
        }
        events.Clear();
        await using (var server = new Server(stall: true))
        using (var snapshot = Snapshot(new(TimeSpan.FromMilliseconds(1200), TimeSpan.FromMilliseconds(1200), TimeSpan.Zero)))
        {
            var elapsed = Stopwatch.StartNew();
            try { await snapshot.RunAsync(["ls-remote", server.Url], default); throw new Exception("Stalled Git request accepted"); }
            catch (GitCommandException error)
            {
                check(error.TimedOut && error.Phase == "Reading Git references" && elapsed.Elapsed.TotalSeconds < 8 && server.InfoRequests <= 2,
                    "A stalled Git request stops after bounded retries and identifies the timed-out phase");
            }
            check(events.Any(value => value.StartsWith("Git · Reading Git references · ", StringComparison.Ordinal)),
                "A waiting heartbeat remains visible without extending the network idle deadline");
        }
        events.Clear();
        await using (var server = new Server(stall: true))
        using (var snapshot = Snapshot())
        using (var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
        {
            try { await snapshot.RunAsync(["ls-remote", server.Url], cancelled.Token); throw new Exception("Cancelled Git request accepted"); }
            catch (OperationCanceledException) { check(!events.Contains("Retrying Git transfer"), "User cancellation terminates Git without starting a retry"); }
        }
        await using (var server = new Server(notFound: true))
        using (var snapshot = Snapshot())
        {
            try { await snapshot.RunAsync(["ls-remote", server.Url], default); throw new Exception("Missing Git repository accepted"); }
            catch (GitCommandException error)
            { check(!error.TimedOut && server.InfoRequests == 1 && error.Detail.Contains("not found", StringComparison.OrdinalIgnoreCase), "A missing repository preserves the Git error and is not retried"); }
        }
        check(GitRepositoryDiagnostics.Redact("https://name:secret@example.invalid/repo.git").Contains("<redacted>@", StringComparison.Ordinal),
            "Git error logging masks credentials embedded in a URL");
        using (var resolver = new Resolver())
        using (var http = new HttpClient(resolver))
        {
            var result = await GitHostResolution.TryResolveAsync(default, http);
            check(result == "github.com:443:203.0.113.1" && resolver.Requests.Count == 2,
                "DNS fallback recovers through a second HTTPS resolver using only a GitHub A record");
            check(resolver.Requests.All(uri => uri.Query == "?name=github.com&type=A" && uri.UserInfo.Length == 0),
                "DNS fallback transmits only the public hostname without repository paths or credentials");
        }
        using (var resolver = new Resolver { Invalid = true })
        using (var http = new HttpClient(resolver))
            check(await GitHostResolution.TryResolveAsync(default, http) is null, "DNS fallback rejects loopback and invalid address responses");
        using (var resolver = new Resolver())
        using (var http = new HttpClient(resolver))
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await GitHostResolution.TryResolveAsync(cancelled.Token, http); throw new Exception("Cancelled DNS lookup accepted"); }
            catch (OperationCanceledException) { check(true, "User cancellation interrupts DNS fallback"); }
        }
        Probe.Clean(root);
    }

    private sealed class Resolver : HttpMessageHandler
    {
        internal readonly List<Uri> Requests = [];
        internal bool Invalid;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!);
            if (!Invalid && Requests.Count == 1) throw new HttpRequestException("Fixture resolver unavailable");
            var answer = new { Status = 0, Answer = new[] { new { name = "github.com.", type = 1, data = Invalid ? "127.0.0.1" : "203.0.113.1" },
                new { name = "other.invalid", type = 1, data = "203.0.113.2" } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(answer)) });
        }
    }

    private sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ConcurrentBag<Task> _clients = [];
        private readonly Task _accepting;
        private readonly bool _firstFailure, _stall, _notFound;
        internal int InfoRequests;
        internal string Url { get; }
        internal Server(bool firstFailure = false, bool stall = false, bool notFound = false)
        {
            _firstFailure = firstFailure; _stall = stall; _notFound = notFound;
            _listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/repo.git";
            _accepting = Accept();
        }
        private async Task Accept()
        {
            try
            {
                while (true) { var client = await _listener.AcceptTcpClientAsync(_lifetime.Token); _clients.Add(Reply(client)); }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        }
        private async Task Reply(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var first = await reader.ReadLineAsync(_lifetime.Token) ?? "";
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_lifetime.Token))) { }
                    var info = first.Contains("/info/refs", StringComparison.Ordinal);
                    var number = info ? Interlocked.Increment(ref InfoRequests) : 0;
                    if (_stall) { await Task.Delay(Timeout.Infinite, _lifetime.Token); return; }
                    var status = _notFound ? "404 Not Found" : _firstFailure && info && number == 1 ? "503 Service Unavailable" : "200 OK";
                    var content = status.StartsWith("200", StringComparison.Ordinal) ? info ? new string('a', 40) + "\trefs/heads/main\n" : "ref: refs/heads/main\n" : "unavailable";
                    var bytes = Encoding.ASCII.GetBytes(content);
                    var header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: text/plain\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _lifetime.Token); await stream.WriteAsync(bytes, _lifetime.Token);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException) { }
            }
        }
        public async ValueTask DisposeAsync()
        {
            await _lifetime.CancelAsync(); _listener.Stop();
            await _accepting; await Task.WhenAll(_clients); _lifetime.Dispose();
        }
    }
}
