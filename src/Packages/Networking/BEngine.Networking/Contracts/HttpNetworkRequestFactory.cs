namespace BEngine.Networking;

public sealed class HttpNetworkRequestFactory : IHttpNetworkRequestFactory
{
    public static HttpNetworkRequestFactory Shared { get; } = new();

    public HttpNetworkRequest Create(
        Uri uri,
        HttpRequestMethod method,
        IHttpTransport? transport = null) =>
        new(uri, method, transport);
}
