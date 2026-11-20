using BEngine.Platform;

namespace BEngine.Networking;

/// <summary>Uses the active host without choosing a native or browser backend.</summary>
public sealed class PlatformHttpTransport : IHttpTransport
{
    public static PlatformHttpTransport Shared { get; } = new();

    public IHttpTransportOperation Send(HttpTransportRequest request, IHttpDownloadHandler downloadHandler)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(downloadHandler);
        var transport = PlatformRuntime.GetService<IPlatformHttpTransport>() ??
            throw new PlatformNotSupportedException("The host has no HTTP transport.");
        var operation = new Operation(downloadHandler);
        var method = request.Method switch
        {
            HttpRequestMethod.Get => "GET", HttpRequestMethod.Post => "POST",
            HttpRequestMethod.Put => "PUT", HttpRequestMethod.Delete => "DELETE",
            HttpRequestMethod.Patch => "PATCH", HttpRequestMethod.Head => "HEAD",
            _ => request.Method.ToString().ToUpperInvariant()
        };
        operation.Bind(transport.Send(new PlatformHttpRequest(request.Uri, method, request.Headers,
            request.UploadData, request.UploadContentType) { BypassCache = request.BypassCache }, operation));
        return operation;
    }

    private sealed class Operation(IHttpDownloadHandler handler) : IHttpTransportOperation, IPlatformHttpDownloadHandler
    {
        private IPlatformHttpOperation _native = null!;
        internal void Bind(IPlatformHttpOperation native) => _native = native;
        public NetworkOperationStatus Status => _native.Status switch
        {
            PlatformNetworkOperationStatus.Running => NetworkOperationStatus.Running,
            PlatformNetworkOperationStatus.Succeeded => NetworkOperationStatus.Succeeded,
            PlatformNetworkOperationStatus.Failed => NetworkOperationStatus.Failed,
            _ => throw new InvalidOperationException("The host returned an unknown HTTP operation status.")
        };
        public Exception? Exception => _native.Exception;
        public HttpTransportResponse? Response { get; private set; }
        public long DownloadedBytes => _native.DownloadedBytes;
        public long? ContentLength => _native.ContentLength;
        public void Poll() => _native.Poll();
        public void Abort() => _native.Abort();
        public void Dispose() => _native.Dispose();
        public void ReceiveResponse(PlatformHttpResponse response)
        {
            Response = new HttpTransportResponse(response.StatusCode, response.ReasonPhrase, response.Headers);
            handler.ReceiveResponse(Response);
        }
        public void ReceiveData(ReadOnlyMemory<byte> data) => handler.ReceiveData(data);
    }
}
