using BEngine.Networking;
using System.Net.Http;

namespace BEngine.Platform.DotNet;

/// <summary>OS/browser asynchronous I/O. Download callbacks run only inside Poll on the owning thread.</summary>
public sealed class DotNetHttpTransport : IHttpTransport
{
    public static DotNetHttpTransport Shared { get; } = new();
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public IHttpTransportOperation Send(HttpTransportRequest request, IHttpDownloadHandler downloadHandler) =>
        new Operation(request, downloadHandler);

    private sealed class Operation : IHttpTransportOperation
    {
        private readonly HttpRequestMessage _request;
        private readonly IHttpDownloadHandler _handler;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task<HttpResponseMessage> _responseTask;
        private readonly byte[] _buffer = new byte[64 * 1024];
        private HttpResponseMessage? _message;
        private Task<Stream>? _streamTask;
        private Stream? _stream;
        private Task<int>? _readTask;
        private bool _disposed;

        internal Operation(HttpTransportRequest request, IHttpDownloadHandler handler)
        {
            ArgumentNullException.ThrowIfNull(request);
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _request = new HttpRequestMessage(new HttpMethod(request.Method.ToString().ToUpperInvariant()), request.Uri);
            if (request.UploadData is not null)
            {
                _request.Content = new ByteArrayContent(request.UploadData);
                if (!string.IsNullOrWhiteSpace(request.UploadContentType))
                    _request.Content.Headers.TryAddWithoutValidation("Content-Type", request.UploadContentType);
            }
            foreach (var (name, value) in request.Headers)
                if (!_request.Headers.TryAddWithoutValidation(name, value))
                    _request.Content?.Headers.TryAddWithoutValidation(name, value);
            _responseTask = Client.SendAsync(_request, HttpCompletionOption.ResponseHeadersRead, _cancellation.Token);
        }

        public NetworkOperationStatus Status { get; private set; } = NetworkOperationStatus.Running;
        public Exception? Exception { get; private set; }
        public HttpTransportResponse? Response { get; private set; }
        public long DownloadedBytes { get; private set; }
        public long? ContentLength => _message?.Content.Headers.ContentLength;

        public void Poll()
        {
            if (_disposed || Status != NetworkOperationStatus.Running) return;
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
                    Response = new HttpTransportResponse(_message.StatusCode, _message.ReasonPhrase, headers);
                    _handler.ReceiveResponse(Response);
                    if (_disposed || Status != NetworkOperationStatus.Running) return;
                    _streamTask = _message.Content.ReadAsStreamAsync(_cancellation.Token);
                }
                if (_stream is null)
                {
                    if (!_streamTask!.IsCompleted) return;
                    _stream = _streamTask.GetAwaiter().GetResult();
                }
                var budget = 1024 * 1024;
                while (!_disposed && Status == NetworkOperationStatus.Running && budget > 0)
                {
                    _readTask ??= _stream.ReadAsync(_buffer.AsMemory(), _cancellation.Token).AsTask();
                    if (!_readTask.IsCompleted) return;
                    var count = _readTask.GetAwaiter().GetResult();
                    _readTask = null;
                    if (count == 0) { Status = NetworkOperationStatus.Succeeded; return; }
                    DownloadedBytes += count;
                    budget -= count;
                    _handler.ReceiveData(_buffer.AsMemory(0, count));
                }
            }
            catch (Exception exception)
            {
                Exception = exception;
                Status = NetworkOperationStatus.Failed;
            }
        }

        public void Abort()
        {
            if (_disposed || Status != NetworkOperationStatus.Running) return;
            Status = NetworkOperationStatus.Failed;
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
