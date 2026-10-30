using System.Buffers;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using BEngine.Networking;

namespace BEngine.Platform.DotNet;

/// <summary>Browser fetch owns bounded prefetch; only Poll calls engine handlers.</summary>
internal static class BrowserHttpTransport
{
    internal const int MaximumQueueBytes = 2 * 1024 * 1024;
    private const int MaximumPollBytes = 1024 * 1024;
    private const int MaximumLoadingPollBytes = 4 * MaximumPollBytes;
    private const int LoadingChunkBytes = 256 * 1024;
    private const int SmallBufferBytes = 64 * 1024;

    internal static bool TrySend(HttpTransportRequest request, IHttpDownloadHandler handler,
        out IHttpTransportOperation? operation)
    {
        operation = null;
        if (!OperatingSystem.IsBrowser()) return false;
        // Older exported hosts do not expose this bridge. Probe before starting
        // a request so fallback can never submit an upload twice.
        try
        {
            if (!NativeBridge.Shared.Available()) return false;
        }
        catch (JSException) { return false; }
        operation = TryCreate(request, handler, NativeBridge.Shared);
        return operation is not null;
    }

    internal static IHttpTransportOperation? TryCreate(HttpTransportRequest request,
        IHttpDownloadHandler handler, IBrowserHttpBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        var operation = new Operation(request, handler, bridge);
        return operation.HasHandle || operation.Status == NetworkOperationStatus.Failed ? operation : null;
    }

    private static string RequestJson(HttpTransportRequest request)
    {
        var bytes = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteString("url", request.Uri.AbsoluteUri);
            writer.WriteString("method", request.Method.ToString().ToUpperInvariant());
            writer.WriteStartArray("headers");
            foreach (var header in request.Headers)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(header.Key);
                writer.WriteStringValue(header.Value);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteBoolean("hasUpload", request.UploadData is not null);
            writer.WriteString("uploadContentType", request.UploadContentType);
            writer.WriteBoolean("bypassCache", request.BypassCache);
            writer.WriteNumber("maximumQueueBytes", FrameWorkBudget.IsActive && FrameWorkBudget.isLoading
                ? MaximumLoadingPollBytes : MaximumQueueBytes);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(bytes.WrittenSpan);
    }

    private sealed class Operation : IHttpTransportOperation
    {
        private readonly IBrowserHttpBridge _bridge;
        private readonly IHttpDownloadHandler _handler;
        private readonly bool _expectsBody;
        private int _handle;
        private byte[]? _buffer;
        private long _receivedBytes;
        private long _deliveredBytes;
        private long? _expectedBodyLength;
        private bool _disposed;
        private bool _polling;

        internal Operation(HttpTransportRequest request, IHttpDownloadHandler handler, IBrowserHttpBridge bridge)
        {
            _bridge = bridge;
            _handler = handler;
            _expectsBody = request.Method != HttpRequestMethod.Head;
            try
            {
                _handle = bridge.Begin(RequestJson(request), request.UploadData.AsSpan());
                if (_handle < 0) throw new InvalidDataException("The browser HTTP bridge returned an invalid request handle.");
            }
            catch (Exception exception)
            {
                // Once begin was entered, an unexpected bridge failure is a
                // failed operation, never permission to submit the request again.
                Exception = new IOException("The browser HTTP request could not be started.", exception);
                Status = NetworkOperationStatus.Failed;
            }
        }

        internal bool HasHandle => _handle > 0;
        public NetworkOperationStatus Status { get; private set; } = NetworkOperationStatus.Running;
        public Exception? Exception { get; private set; }
        public HttpTransportResponse? Response { get; private set; }
        public long DownloadedBytes => _receivedBytes;
        public long? ContentLength { get; private set; }

        public void Poll()
        {
            if (_disposed || _polling || Status != NetworkOperationStatus.Running) return;
            _polling = true;
            var loadingPoll = FrameWorkBudget.IsActive && FrameWorkBudget.isLoading;
            try
            {
                RefreshReceivedBytes();
                if (Response is null)
                {
                    var json = _bridge.Response(_handle);
                    if (json is null)
                    {
                        var pending = _bridge.Read(_handle, Span<byte>.Empty);
                        if (pending == -2) throw NetworkFailure();
                        if (pending != 0) throw new IOException("The browser HTTP response ended without headers.");
                        return;
                    }
                    Response = ParseResponse(json);
                    _handler.ReceiveResponse(Response);
                    if (_disposed || Status != NetworkOperationStatus.Running) return;
                    _buffer = new byte[ContentLength is > SmallBufferBytes
                        ? (int)Math.Min(ContentLength.Value, MaximumPollBytes) : SmallBufferBytes];
                }

                var budget = loadingPoll ? MaximumLoadingPollBytes : MaximumPollBytes;
                var deliveredThisPoll = 0;
                var extraStart = 0L;
                while (!_disposed && Status == NetworkOperationStatus.Running && budget > 0)
                {
                    // Preserve the original one-MiB forward-progress allowance. Only
                    // the additional loading allowance consumes unspent frame time.
                    if (loadingPoll && deliveredThisPoll >= MaximumPollBytes)
                    {
                        if (!FrameWorkBudget.isLoading || FrameWorkBudget.remainingMilliseconds <= 0) return;
                        if (extraStart == 0) extraStart = Stopwatch.GetTimestamp();
                        else if (FrameWorkBudget.enabled && Stopwatch.GetElapsedTime(extraStart).TotalMilliseconds >= FrameWorkBudget.CooperativeMaximumStep(2)) return;
                    }
                    var capacity = Math.Min(budget, _buffer!.Length);
                    if (loadingPoll)
                        capacity = Math.Min(capacity, deliveredThisPoll < MaximumPollBytes
                            ? MaximumPollBytes - deliveredThisPoll : LoadingChunkBytes);
                    var count = _bridge.Read(_handle, _buffer.AsSpan(0, capacity));
                    if (count == 0) return;
                    if (count == -2) throw NetworkFailure();
                    if (count == -1)
                    {
                        if (_expectedBodyLength is { } expected && _deliveredBytes != expected)
                            throw new EndOfStreamException("The HTTP body ended before its declared Content-Length.");
                        RefreshReceivedBytes();
                        Status = NetworkOperationStatus.Succeeded;
                        return;
                    }
                    if (count < 0 || count > capacity)
                        throw new InvalidDataException("The browser HTTP bridge returned an invalid byte count.");
                    if (_expectedBodyLength is { } maximum && count > maximum - _deliveredBytes)
                        throw new InvalidDataException("The HTTP body exceeds its declared Content-Length.");
                    _deliveredBytes += count;
                    _receivedBytes = Math.Max(_receivedBytes, _deliveredBytes);
                    budget -= count;
                    deliveredThisPoll += count;
                    _handler.ReceiveData(_buffer.AsMemory(0, count));
                }
            }
            catch (Exception exception)
            {
                Exception = exception;
                Status = NetworkOperationStatus.Failed;
                CancelBridge();
            }
            finally { _polling = false; }
        }

        private HttpTransportResponse ParseResponse(string json)
        {
            if (json.Length > 64 * 1024)
                throw new InvalidDataException("The browser HTTP response headers exceed the supported size.");
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var status = root.GetProperty("statusCode").GetInt32();
            if (status is < 100 or > 599) throw new IOException("The browser HTTP response has an invalid status code.");
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in root.GetProperty("headers").EnumerateObject())
                headers.Add(header.Name, header.Value.EnumerateArray().Select(static value =>
                    value.GetString() ?? throw new InvalidDataException("An HTTP response header cannot be null.")).ToArray());
            if (headers.TryGetValue("Content-Length", out var lengths) && lengths.Length != 0)
            {
                if (!long.TryParse(lengths[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length < 0)
                    throw new InvalidDataException("The HTTP response has an invalid Content-Length.");
                ContentLength = length;
            }
            // Fetch may transparently decode the body while retaining compressed
            // wire headers. Preserve those headers for callers without comparing
            // their wire size against the decoded body.
            if (_expectsBody && status is not (204 or 304) &&
                (!headers.TryGetValue("Content-Encoding", out var encodings) ||
                 encodings.SelectMany(static encoding => encoding.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                     .All(static encoding => encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))))
                _expectedBodyLength = ContentLength;
            return new HttpTransportResponse((HttpStatusCode)status,
                root.TryGetProperty("reasonPhrase", out var reason) ? reason.GetString() : null, headers);
        }

        private void RefreshReceivedBytes()
        {
            var received = _bridge.Downloaded(_handle);
            if (!double.IsFinite(received) || received < 0 || received > 9_007_199_254_740_991 ||
                Math.Truncate(received) != received)
                throw new InvalidDataException("The browser HTTP bridge returned an invalid download count.");
            _receivedBytes = Math.Max(_receivedBytes, (long)received);
        }

        private IOException NetworkFailure() => new(_bridge.Error(_handle) ?? "The browser HTTP request failed.");

        private void CancelBridge()
        {
            if (_handle <= 0) return;
            try { _bridge.Abort(_handle); } catch { }
        }

        public void Abort()
        {
            if (_disposed || Status != NetworkOperationStatus.Running) return;
            Exception = new IOException("The platform HTTP operation was aborted.");
            Status = NetworkOperationStatus.Failed;
            CancelBridge();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Abort();
            _disposed = true;
            var handle = _handle;
            _handle = 0;
            _buffer = null;
            if (handle > 0) try { _bridge.Release(handle); } catch { }
        }
    }

    [SupportedOSPlatform("browser")]
    private sealed class NativeBridge : IBrowserHttpBridge
    {
        internal static NativeBridge Shared { get; } = new();
        public bool Available() => BrowserHttpInterop.Available();
        public int Begin(string request, Span<byte> upload) => BrowserHttpInterop.Begin(request, upload);
        public string? Response(int handle) => BrowserHttpInterop.Response(handle);
        public int Read(int handle, Span<byte> destination) => BrowserHttpInterop.Read(handle, destination);
        public double Downloaded(int handle) => BrowserHttpInterop.Downloaded(handle);
        public string? Error(int handle) => BrowserHttpInterop.Error(handle);
        public void Abort(int handle) => BrowserHttpInterop.Abort(handle);
        public void Release(int handle) => BrowserHttpInterop.Release(handle);
    }
}

internal interface IBrowserHttpBridge
{
    bool Available();
    int Begin(string request, Span<byte> upload);
    string? Response(int handle);
    int Read(int handle, Span<byte> destination);
    double Downloaded(int handle);
    string? Error(int handle);
    void Abort(int handle);
    void Release(int handle);
}

[SupportedOSPlatform("browser")]
internal static partial class BrowserHttpInterop
{
    [JSImport("globalThis.BEngineWebHttp.available")]
    internal static partial bool Available();
    [JSImport("globalThis.BEngineWebHttp.begin")]
    internal static partial int Begin(string request, [JSMarshalAs<JSType.MemoryView>] Span<byte> upload);
    [JSImport("globalThis.BEngineWebHttp.response")]
    internal static partial string? Response(int handle);
    [JSImport("globalThis.BEngineWebHttp.read")]
    internal static partial int Read(int handle, [JSMarshalAs<JSType.MemoryView>] Span<byte> destination);
    [JSImport("globalThis.BEngineWebHttp.downloaded")]
    internal static partial double Downloaded(int handle);
    [JSImport("globalThis.BEngineWebHttp.error")]
    internal static partial string? Error(int handle);
    [JSImport("globalThis.BEngineWebHttp.abort")]
    internal static partial void Abort(int handle);
    [JSImport("globalThis.BEngineWebHttp.release")]
    internal static partial void Release(int handle);
}

