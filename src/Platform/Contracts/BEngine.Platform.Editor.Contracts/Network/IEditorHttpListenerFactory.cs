namespace BEngine.Platform;

/// <summary>Native HTTP listening for local editor tools. No routes or application protocols are installed.</summary>
public interface IEditorHttpListenerFactory
{
    IEditorHttpListener ListenLoopback();
}

/// <summary>Owns an IPv4 loopback listener. Cancelling an accept terminates this listener.</summary>
public interface IEditorHttpListener : IDisposable
{
    Uri BaseAddress { get; }
    Task<IEditorHttpContext> AcceptAsync(CancellationToken cancellationToken = default);
}

/// <summary>A borrowed request and owned response. Dispose completes or aborts the response.</summary>
public interface IEditorHttpContext : IDisposable
{
    string Method { get; }
    Uri Url { get; }
    long RequestContentLength { get; }
    Stream RequestBody { get; }
    Stream ResponseBody { get; }
    int StatusCode { get; set; }
    string? ContentType { get; set; }
    long ResponseContentLength { set; }
    void SetResponseHeader(string name, string value);
}
