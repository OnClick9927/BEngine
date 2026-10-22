using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BEngine.Networking;

public sealed class HttpNetworkRequest : IDisposable
{
    public static readonly TimeSpan InfiniteTimeout = TimeSpan.FromMilliseconds(-1);

    private readonly IHttpTransport? _transport;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private IHttpTransportOperation? _activeOperation;
    private bool _sent;
    private bool _aborted;
    private bool _disposed;
    private long _bufferReservation;

    public HttpNetworkRequest(
        Uri uri,
        HttpRequestMethod method,
        IHttpTransport? transport = null)
    {
        Uri = ValidateUri(uri);
        Method = method;
        _transport = transport;
    }

    public Uri Uri { get; }
    public HttpRequestMethod Method { get; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Applies to buffered and streaming downloads; raise explicitly for large files.</summary>
    public long MaximumDownloadBytes { get; set; } = NetworkMemorySettings.maximumResponseBytes;
    public byte[]? UploadData { get; set; }
    public string? UploadContentType { get; set; }
    public byte[]? DownloadData { get; private set; }
    public string? DownloadText => DownloadData is null
        ? null
        : Encoding.UTF8.GetString(DownloadData);
    public long DownloadedBytes { get; private set; }
    public long? ContentLength { get; private set; }
    public float DownloadProgress => ContentLength switch
    {
        0 => 1f,
        > 0 => Math.Clamp((float)DownloadedBytes / ContentLength.Value, 0f, 1f),
        _ => 0f
    };
    public HttpStatusCode? ResponseCode { get; private set; }
    public IReadOnlyDictionary<string, string[]> ResponseHeaders { get; private set; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    public NetworkRequestResult Result { get; private set; } = NetworkRequestResult.NotStarted;
    public string? Error { get; private set; }
    public Exception? Exception { get; private set; }
    public bool IsDone => Result is not (NetworkRequestResult.NotStarted or NetworkRequestResult.InProgress);
    public bool IsSuccess => Result == NetworkRequestResult.Success;

    public static HttpNetworkRequest Get(string uri, IHttpTransport? transport = null) =>
        new(CreateUri(uri), HttpRequestMethod.Get, transport);

    public static HttpNetworkRequest Delete(string uri, IHttpTransport? transport = null) =>
        new(CreateUri(uri), HttpRequestMethod.Delete, transport);

    public static HttpNetworkRequest Post(
        string uri,
        ReadOnlySpan<byte> data,
        string contentType = "application/octet-stream",
        IHttpTransport? transport = null) =>
        CreateWithBody(uri, HttpRequestMethod.Post, data, contentType, transport);

    public static HttpNetworkRequest Put(
        string uri,
        ReadOnlySpan<byte> data,
        string contentType = "application/octet-stream",
        IHttpTransport? transport = null) =>
        CreateWithBody(uri, HttpRequestMethod.Put, data, contentType, transport);

    public void SetRequestHeader(string name, string value)
    {
        ThrowIfDisposed();
        if (_sent)
            throw new InvalidOperationException(
                "Request headers cannot be changed after sending starts.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        if (name.IndexOfAny(['\r', '\n', ':']) >= 0 || value.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("The HTTP header contains invalid characters.", nameof(name));
        _headers[name] = value;
    }

    public string? GetResponseHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ResponseHeaders.TryGetValue(name, out var values)
            ? string.Join(", ", values)
            : null;
    }

    public BTask<HttpNetworkRequest> SendAsync(BCancellationToken cancellationToken = default)
    {
        var handler = new BufferedDownloadHandler();
        return SendBufferedAsync(handler, cancellationToken);
    }

    public BTask<HttpNetworkRequest> SendAsync(
        IHttpDownloadHandler downloadHandler,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(downloadHandler);
        return SendCoreAsync(downloadHandler, cancellationToken);
    }

    public void Abort()
    {
        ThrowIfDisposed();
        if (IsDone) return;
        _aborted = true;
        _activeOperation?.Abort();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _aborted = true;
        _activeOperation?.Abort();
        _activeOperation?.Dispose();
        _activeOperation = null;
        DownloadData = null; NetworkMemorySettings.Release(_bufferReservation); _bufferReservation = 0;
    }

    private async BTask<HttpNetworkRequest> SendBufferedAsync(
        BufferedDownloadHandler handler,
        BCancellationToken cancellationToken)
    {
        using (handler)
        {
            await SendCoreAsync(handler, cancellationToken);
            if (!_disposed)
            {
                try
                {
                    DownloadData = handler.ToArray();
                    var transient = _bufferReservation - DownloadData.LongLength;
                    NetworkMemorySettings.Release(transient); _bufferReservation -= transient;
                }
                catch (Exception error)
                {
                    DownloadData = null; NetworkMemorySettings.Release(_bufferReservation); _bufferReservation = 0;
                    SetFailure(NetworkRequestResult.DataProcessingError, error.Message, error);
                }
            }
            return this;
        }
    }

    private async BTask<HttpNetworkRequest> SendCoreAsync(
        IHttpDownloadHandler downloadHandler,
        BCancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_sent)
            throw new InvalidOperationException("An HTTP network request can only be sent once.");
        ValidateTimeout();
        _sent = true;
        Result = NetworkRequestResult.InProgress;
        Error = null;
        Exception = null;
        var startedAt = BTaskScheduler.Main.Elapsed;
        var observer = new ObservingDownloadHandler(this, downloadHandler);
        var entered = false;

        try
        {
            if (MaximumDownloadBytes < 0) throw new InvalidDataException("HTTP response byte limit cannot be negative.");
            NetworkMemorySettings.Enter(); entered = true;
            var transport = _transport ?? NetworkTransportRegistry.HttpTransport ??
                RuntimeServiceAccess.GetRequiredService<IHttpTransport>();
            _activeOperation = transport.Send(
                new HttpTransportRequest(
                    Uri,
                    Method,
                    new Dictionary<string, string>(_headers, StringComparer.OrdinalIgnoreCase),
                    UploadData,
                    UploadContentType),
                observer);
            await BTask.Yield();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_aborted) throw new BOperationCanceledException(BCancellationToken.None);
                if (_activeOperation!.Status != NetworkOperationStatus.Running) break;
                if (Timeout != InfiniteTimeout &&
                    BTaskScheduler.Main.Elapsed - startedAt >= Timeout)
                {
                    _activeOperation.Abort();
                    throw new TimeoutException($"The HTTP request timed out after {Timeout}.");
                }
                _activeOperation.Poll();
                cancellationToken.ThrowIfCancellationRequested();
                if (_aborted) throw new BOperationCanceledException(BCancellationToken.None);
                DownloadedBytes = _activeOperation.DownloadedBytes;
                ContentLength = _activeOperation.ContentLength;
                if (_activeOperation.Status == NetworkOperationStatus.Running)
                    await BTask.Yield();
            }

            if (_activeOperation.Status == NetworkOperationStatus.Failed)
                throw _activeOperation.Exception ??
                      new IOException("The HTTP transport failed without an error.");
            CaptureResponse(_activeOperation.Response ??
                throw new InvalidDataException("The HTTP transport completed without a response."));
            DownloadedBytes = _activeOperation.DownloadedBytes;
            ContentLength = _activeOperation.ContentLength;
            if (ResponseCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices)
                Result = NetworkRequestResult.Success;
            else
            {
                Result = NetworkRequestResult.ProtocolError;
                Error = $"HTTP {(int)ResponseCode!.Value} ({_activeOperation.Response?.ReasonPhrase}).";
            }
        }
        catch (TimeoutException exception)
        {
            SetFailure(NetworkRequestResult.TimedOut, exception.Message, exception);
        }
        catch (BOperationCanceledException exception)
        {
            SetFailure(NetworkRequestResult.Canceled,
                "The HTTP request was canceled.", exception);
        }
        catch (InvalidDataException exception)
        {
            SetFailure(NetworkRequestResult.DataProcessingError, exception.Message, exception);
        }
        catch (Exception exception) when (exception is IOException or SocketException)
        {
            SetFailure(NetworkRequestResult.ConnectionError, exception.Message, exception);
        }
        catch (Exception exception)
        {
            SetFailure(NetworkRequestResult.DataProcessingError, exception.Message, exception);
        }
        finally
        {
            _activeOperation?.Dispose();
            _activeOperation = null;
            if (entered) NetworkMemorySettings.Exit();
        }
        return this;
    }

    private void CaptureResponse(HttpTransportResponse response)
    {
        ResponseCode = response.StatusCode;
        ResponseHeaders = response.Headers;
    }

    private void SetFailure(NetworkRequestResult result, string error, Exception exception)
    {
        Result = result;
        Error = error;
        Exception = exception;
    }

    private static HttpNetworkRequest CreateWithBody(
        string uri,
        HttpRequestMethod method,
        ReadOnlySpan<byte> data,
        string contentType,
        IHttpTransport? transport) =>
        new(CreateUri(uri), method, transport)
        {
            UploadData = data.ToArray(),
            UploadContentType = contentType
        };

    private static Uri CreateUri(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        return ValidateUri(new Uri(uri, UriKind.Absolute));
    }

    private static Uri ValidateUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException(
                "HTTP requests require an absolute HTTP or HTTPS URI.", nameof(uri));
        return uri;
    }

    private void ValidateTimeout()
    {
        if (Timeout <= TimeSpan.Zero && Timeout != InfiniteTimeout)
            throw new InvalidOperationException(
                $"Timeout must be positive or {nameof(InfiniteTimeout)}.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class ObservingDownloadHandler(
        HttpNetworkRequest owner,
        IHttpDownloadHandler inner) : IHttpDownloadHandler
    {
        private long _received;
        public void ReceiveResponse(HttpTransportResponse response)
        {
            owner.CaptureResponse(response);
            if (response.Headers.TryGetValue("Content-Length", out var values) && values.Length > 0 &&
                long.TryParse(values[0], out var length) && length > owner.MaximumDownloadBytes)
                throw new InvalidDataException("HTTP Content-Length exceeds the configured response byte limit.");
            inner.ReceiveResponse(response);
        }

        public void ReceiveData(ReadOnlyMemory<byte> data)
        {
            if (data.Length > owner.MaximumDownloadBytes - _received)
                throw new InvalidDataException("HTTP response exceeded the configured response byte limit.");
            _received += data.Length;
            if (inner is BufferedDownloadHandler)
            {
                // Account stream growth and the final response copy during transfer.
                var reservation = checked((long)data.Length * 3);
                NetworkMemorySettings.Reserve(reservation); owner._bufferReservation += reservation;
            }
            inner.ReceiveData(data);
        }
    }

    private sealed class BufferedDownloadHandler : IHttpDownloadHandler, IDisposable
    {
        private readonly MemoryStream _stream = new();

        public void ReceiveResponse(HttpTransportResponse response)
        {
        }

        public void ReceiveData(ReadOnlyMemory<byte> data) => _stream.Write(data.Span);
        public byte[] ToArray() => _stream.ToArray();
        public void Dispose() => _stream.Dispose();
    }
}
