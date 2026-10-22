using System.Net;

namespace BEngine.Networking;

public enum HttpRequestMethod
{
    Get,
    Post,
    Put,
    Delete,
    Patch,
    Head
}

public sealed record HttpTransportRequest(
    Uri Uri,
    HttpRequestMethod Method,
    IReadOnlyDictionary<string, string> Headers,
    byte[]? UploadData,
    string? UploadContentType);

public sealed record HttpTransportResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    IReadOnlyDictionary<string, string[]> Headers);

public interface IHttpDownloadHandler
{
    void ReceiveResponse(HttpTransportResponse response);
    void ReceiveData(ReadOnlyMemory<byte> data);
}

public interface IHttpTransportOperation : INetworkOperation
{
    HttpTransportResponse? Response { get; }
    long DownloadedBytes { get; }
    long? ContentLength { get; }
}

public interface IHttpTransport
{
    IHttpTransportOperation Send(
        HttpTransportRequest request,
        IHttpDownloadHandler downloadHandler);
}
