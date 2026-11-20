using BEngine.Platform;
using System.Net.Http;

namespace BEngine.Platform.DotNet;

/// <summary>OS/browser asynchronous I/O. Download callbacks run only inside Poll on the owning thread.</summary>
public sealed class DotNetHttpTransport : IPlatformHttpTransport
{
    public static DotNetHttpTransport Shared { get; } = new();
    private static readonly Lazy<HttpClient> SharedClient = new(static () =>
        new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
    private static HttpClient Client => SharedClient.Value;

    public IPlatformHttpOperation Send(PlatformHttpRequest request, IPlatformHttpDownloadHandler downloadHandler)
    {
        return new Operation(request, downloadHandler);
    }

    private sealed class Operation : IPlatformHttpOperation
    {
        private const int SmallReceiveBufferBytes = 64 * 1024;
        private const int MaximumReceiveBufferBytes = 1024 * 1024;
        private readonly HttpRequestMessage _request;
        private readonly IPlatformHttpDownloadHandler _handler;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task<HttpResponseMessage> _responseTask;
        private readonly bool _expectsBody;
        private byte[]? _buffer;
        private HttpResponseMessage? _message;
        private Task<Stream>? _streamTask;
        private Stream? _stream;
        private Task<int>? _readTask;
        private long? _expectedBodyLength;
        private bool _coalesceReads;
        private long _receivedBytes;
        private bool _disposed;

        internal Operation(PlatformHttpRequest request, IPlatformHttpDownloadHandler handler, HttpClient? client = null)
        {
            ArgumentNullException.ThrowIfNull(request);
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _expectsBody = !string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
            _request = new HttpRequestMessage(new HttpMethod(request.Method), request.Uri);
            if (request.BypassCache)
            {
                _request.Headers.CacheControl = new() { NoStore = true, NoCache = true };
                if (OperatingSystem.IsBrowser())
                    _request.Options.Set(
                        new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"),
                        new Dictionary<string, object> { ["cache"] = "no-store" });
            }
            if (request.UploadData is not null)
            {
                _request.Content = new ByteArrayContent(request.UploadData);
                if (!string.IsNullOrWhiteSpace(request.UploadContentType))
                    _request.Content.Headers.TryAddWithoutValidation("Content-Type", request.UploadContentType);
            }
            foreach (var (name, value) in request.Headers)
                if (!_request.Headers.TryAddWithoutValidation(name, value))
                    _request.Content?.Headers.TryAddWithoutValidation(name, value);
            _responseTask = (client ?? Client).SendAsync(_request, HttpCompletionOption.ResponseHeadersRead, _cancellation.Token);
        }

        public PlatformNetworkOperationStatus Status { get; private set; } = PlatformNetworkOperationStatus.Running;
        public Exception? Exception { get; private set; }
        public PlatformHttpResponse? Response { get; private set; }
        public long DownloadedBytes => Interlocked.Read(ref _receivedBytes);
        public long? ContentLength => _message?.Content.Headers.ContentLength;

        public void Poll()
        {
            if (_disposed || Status != PlatformNetworkOperationStatus.Running) return;
            try
            {
                if (_message is null)
                {
                    if (!_responseTask.IsCompleted) return;
                    _message = _responseTask.GetAwaiter().GetResult();
                    var headers = _message.Headers.Concat(_message.Content.Headers)
                        .GroupBy(static header => header.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(static group => group.Key,
                            static group => group.SelectMany(static header => header.Value).ToArray(), StringComparer.OrdinalIgnoreCase);
                    Response = new PlatformHttpResponse(_message.StatusCode, _message.ReasonPhrase, headers);
                    _handler.ReceiveResponse(Response);
                    if (_disposed || Status != PlatformNetworkOperationStatus.Running) return;
                    // Browser streams complete reads through promises. Coalescing only
                    // 64 KiB therefore throttles a large download to 64 KiB per game
                    // frame. Fill one bounded larger block with asynchronous I/O;
                    // callbacks and their per-Poll budget remain on the owning thread.
                    // Encoded response headers describe wire bytes, not the decoded
                    // bytes returned by browser fetch, so their length is not trusted.
                    if (_expectsBody && _message.StatusCode is not (System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotModified) && !Query5598(_message.Content.Headers.ContentEncoding))
                        _expectedBodyLength = _message.Content.Headers.ContentLength;
                    [global::BEngine.SkipLoopGuard]
                    bool Query5598(global::System.Collections.Generic.IEnumerable<string> querySource5598)
                    {
                        ArgumentNullException.ThrowIfNull(querySource5598, "source");
                        bool Matches5598(string encoding) => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase);
                        global::System.ReadOnlySpan<string> queryValues5598;
                        if (querySource5598.GetType() == typeof(string[]))
                            queryValues5598 = (string[])querySource5598;
                        else if (querySource5598.GetType() == typeof(global::System.Collections.Generic.List<string>))
                            queryValues5598 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource5598);
                        else
                        {
                            foreach (var encoding in querySource5598)
                                if (Matches5598(encoding))
                                    return true;
                            return false;
                        }
                    
                        foreach (var encoding in queryValues5598)
                            if (Matches5598(encoding))
                                return true;
                        return false;
                    }
                    _coalesceReads = _message.Content.Headers.ContentLength.HasValue;
                    var capacity = _message.Content.Headers.ContentLength is > SmallReceiveBufferBytes
                        ? (int)Math.Min(MaximumReceiveBufferBytes, _message.Content.Headers.ContentLength.Value)
                        : SmallReceiveBufferBytes;
                    _buffer = new byte[capacity];
                    _streamTask = _message.Content.ReadAsStreamAsync(_cancellation.Token);
                }
                if (_stream is null)
                {
                    if (!_streamTask!.IsCompleted) return;
                    _stream = _streamTask.GetAwaiter().GetResult();
                }
                var budget = MaximumReceiveBufferBytes;
                while (!_disposed && Status == PlatformNetworkOperationStatus.Running && budget > 0)
                {
                    _readTask ??= ReadBufferAsync();
                    if (!_readTask.IsCompleted) return;
                    var count = _readTask.GetAwaiter().GetResult();
                    _readTask = null;
                    if (count == 0) { Status = PlatformNetworkOperationStatus.Succeeded; return; }
                    budget -= count;
                    _handler.ReceiveData(_buffer!.AsMemory(0, count));
                    if (budget <= 0 && !_disposed && Status == PlatformNetworkOperationStatus.Running)
                        _readTask = ReadBufferAsync();
                }
            }
            catch (Exception exception)
            {
                Exception = exception;
                Status = PlatformNetworkOperationStatus.Failed;
            }
        }

        private async Task<int> ReadBufferAsync()
        {
            var token = _cancellation.Token;
            var length = _expectedBodyLength;
            var target = length is { } remainingLength
                ? (int)Math.Clamp(remainingLength - DownloadedBytes, 1, _buffer!.Length)
                : _buffer!.Length;
            var buffered = 0;
            do
            {
                token.ThrowIfCancellationRequested();
                var count = await _stream!.ReadAsync(_buffer.AsMemory(buffered, target - buffered), token).ConfigureAwait(false);
                if (count == 0)
                {
                    if (length is { } declared && DownloadedBytes != declared)
                        throw new EndOfStreamException("The HTTP body ended before its declared Content-Length.");
                    break;
                }
                if (length is { } maximum && count > maximum - DownloadedBytes)
                    throw new InvalidDataException("The HTTP body exceeds its declared Content-Length.");
                buffered += count;
                Interlocked.Add(ref _receivedBytes, count);
                // Coalesce small server chunks through asynchronous I/O instead of
                // spending a game frame on every chunk. Retain a single bounded
                // buffer; unknown-length streams keep their low-latency delivery.
            } while (buffered < target && _coalesceReads && (length is not { } expected || DownloadedBytes < expected));
            return buffered;
        }

        public void Abort()
        {
            if (_disposed || Status != PlatformNetworkOperationStatus.Running) return;
            Status = PlatformNetworkOperationStatus.Failed;
            Exception = new IOException("The platform HTTP operation was aborted.");
            _cancellation.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Abort();
            _disposed = true;
            _stream?.Dispose();
            _message?.Dispose();
            if (_message is null) _ = DisposeResponseAsync(_responseTask);
            if (_readTask is not null) _ = ObserveAsync(_readTask);
            if (_streamTask is not null && _stream is null) _ = DisposeStreamAsync(_streamTask);
            _request.Dispose();
            _cancellation.Dispose();
        }

        private static async Task DisposeResponseAsync(Task<HttpResponseMessage> pending)
        {
            try { (await pending.ConfigureAwait(false)).Dispose(); } catch { }
        }
        private static async Task DisposeStreamAsync(Task<Stream> pending)
        {
            try { (await pending.ConfigureAwait(false)).Dispose(); } catch { }
        }
        private static async Task ObserveAsync(Task pending)
        {
            try { await pending.ConfigureAwait(false); } catch { }
        }
    }
}
