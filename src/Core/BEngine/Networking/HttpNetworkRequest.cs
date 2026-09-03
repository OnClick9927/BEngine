using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace BEngine.Networking;

public sealed class HttpNetworkRequest : IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private HttpResponseMessage? _response;
    private bool _sent;
    private bool _disposed;

    public HttpNetworkRequest(
        Uri uri,
        HttpMethod method,
        HttpClient? client = null,
        bool disposeClient = false)
    {
        Uri = ValidateUri(uri);
        Method = method ?? throw new ArgumentNullException(nameof(method));
        _client = client ?? new HttpClient();
        _ownsClient = client is null || disposeClient;
    }

    public HttpNetworkRequest(Uri uri, HttpMethod method, HttpMessageHandler handler)
        : this(uri, method, new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))), true)
    {
    }

    public Uri Uri { get; }
    public HttpMethod Method { get; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public byte[]? UploadData { get; set; }
    public string? UploadContentType { get; set; }
    public byte[]? DownloadData { get; private set; }
    public string? DownloadText => DownloadData is null ? null : Encoding.UTF8.GetString(DownloadData);
    public HttpStatusCode? ResponseCode { get; private set; }
    public IReadOnlyDictionary<string, string[]> ResponseHeaders { get; private set; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    public NetworkRequestResult Result { get; private set; } = NetworkRequestResult.NotStarted;
    public string? Error { get; private set; }
    public Exception? Exception { get; private set; }
    public bool IsDone => Result is not (NetworkRequestResult.NotStarted or NetworkRequestResult.InProgress);
    public bool IsSuccess => Result == NetworkRequestResult.Success;

    public static HttpNetworkRequest Get(string uri, HttpClient? client = null) =>
        new(CreateUri(uri), HttpMethod.Get, client);

    public static HttpNetworkRequest Delete(string uri, HttpClient? client = null) =>
        new(CreateUri(uri), HttpMethod.Delete, client);

    public static HttpNetworkRequest Post(
        string uri,
        ReadOnlySpan<byte> data,
        string contentType = "application/octet-stream",
        HttpClient? client = null) =>
        CreateWithBody(uri, HttpMethod.Post, data, contentType, client);

    public static HttpNetworkRequest Put(
        string uri,
        ReadOnlySpan<byte> data,
        string contentType = "application/octet-stream",
        HttpClient? client = null) =>
        CreateWithBody(uri, HttpMethod.Put, data, contentType, client);

    public void SetRequestHeader(string name, string value)
    {
        ThrowIfDisposed();
        if (_sent) throw new InvalidOperationException("Request headers cannot be changed after sending starts.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _headers[name] = value;
    }

    public string? GetResponseHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ResponseHeaders.TryGetValue(name, out var values) ? string.Join(", ", values) : null;
    }

    public BValueTask<HttpNetworkRequest> SendAsync(CancellationToken cancellationToken = default) =>
        SendAsync(ReadResponseBodyAsync, cancellationToken);

    public async BValueTask<HttpNetworkRequest> SendAsync(
        Func<HttpResponseMessage, CancellationToken, BValueTask> responseHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(responseHandler);
        ThrowIfDisposed();
        if (_sent) throw new InvalidOperationException("An HTTP network request can only be sent once.");
        ValidateTimeout();
        _sent = true;
        Result = NetworkRequestResult.InProgress;
        Error = null;
        Exception = null;

        using var timeout = new CancellationTokenSource();
        if (Timeout != System.Threading.Timeout.InfiniteTimeSpan) timeout.CancelAfter(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token, timeout.Token);
        try
        {
            using var request = CreateRequestMessage();
            _response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            ResponseCode = _response.StatusCode;
            ResponseHeaders = ReadHeaders(_response);
            await responseHandler(_response, linked.Token).ConfigureAwait(false);
            if (_response.IsSuccessStatusCode)
            {
                Result = NetworkRequestResult.Success;
            }
            else
            {
                Result = NetworkRequestResult.ProtocolError;
                Error = $"HTTP {(int)_response.StatusCode} ({_response.ReasonPhrase}).";
            }
        }
        catch (OperationCanceledException exception) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested &&
            !_lifetime.IsCancellationRequested)
        {
            SetFailure(NetworkRequestResult.TimedOut,
                $"The HTTP request timed out after {Timeout}.", exception);
        }
        catch (OperationCanceledException exception)
        {
            SetFailure(NetworkRequestResult.Canceled, "The HTTP request was canceled.", exception);
        }
        catch (InvalidDataException exception)
        {
            SetFailure(NetworkRequestResult.DataProcessingError, exception.Message, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            SetFailure(NetworkRequestResult.ConnectionError, exception.Message, exception);
        }
        catch (Exception exception)
        {
            SetFailure(NetworkRequestResult.DataProcessingError, exception.Message, exception);
        }
        return this;
    }

    private async BValueTask ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        DownloadData = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

    public void Abort()
    {
        ThrowIfDisposed();
        if (!IsDone) _lifetime.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _response?.Dispose();
        if (_ownsClient) _client.Dispose();
        _lifetime.Dispose();
    }

    private HttpRequestMessage CreateRequestMessage()
    {
        var request = new HttpRequestMessage(Method, Uri);
        if (UploadData is not null)
        {
            request.Content = new ByteArrayContent(UploadData);
            if (!string.IsNullOrWhiteSpace(UploadContentType))
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(UploadContentType);
        }
        foreach (var (name, value) in _headers)
        {
            if (request.Headers.TryAddWithoutValidation(name, value)) continue;
            if (request.Content is null)
                request.Content = new ByteArrayContent([]);
            if (!request.Content.Headers.TryAddWithoutValidation(name, value))
                throw new InvalidOperationException($"HTTP header '{name}' is invalid.");
        }
        return request;
    }

    private void SetFailure(NetworkRequestResult result, string error, Exception exception)
    {
        Result = result;
        Error = error;
        Exception = exception;
    }

    private static HttpNetworkRequest CreateWithBody(
        string uri,
        HttpMethod method,
        ReadOnlySpan<byte> data,
        string contentType,
        HttpClient? client) =>
        new(CreateUri(uri), method, client)
        {
            UploadData = data.ToArray(),
            UploadContentType = contentType
        };

    private static IReadOnlyDictionary<string, string[]> ReadHeaders(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(header => header.Value).ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static Uri CreateUri(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        return ValidateUri(new Uri(uri, UriKind.Absolute));
    }

    private static Uri ValidateUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("HTTP requests require an absolute HTTP or HTTPS URI.", nameof(uri));
        return uri;
    }

    private void ValidateTimeout()
    {
        if (Timeout <= TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
            throw new InvalidOperationException("Timeout must be positive or Timeout.InfiniteTimeSpan.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
