namespace BEngine.Networking;

public interface IHttpNetworkRequestFactory
{
    HttpNetworkRequest Create(
        Uri uri,
        HttpRequestMethod method,
        IHttpTransport? transport = null);
}
