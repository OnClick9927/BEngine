using System.Net;
using System.Net.Sockets;

namespace BEngine.Platform;

/// <summary>Owns native HTTP and ephemeral-port acquisition. Callers supply all request policy.</summary>
public sealed class SystemEditorHttpListenerFactory : IEditorHttpListenerFactory
{
    public static SystemEditorHttpListenerFactory Shared { get; } = new();

    public IEditorHttpListener ListenLoopback()
    {
        // HttpListener cannot bind port zero. Another process may acquire the released
        // port before HTTP starts, so retry acquisition rather than exposing that race.
        for (var attempt = 0; ; attempt++)
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var address = new Uri($"http://127.0.0.1:{port}/");
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add(address.AbsoluteUri);
                listener.Start();
                return new Listener(listener, address);
            }
            catch (HttpListenerException) when (attempt < 7)
            {
                listener.Close();
            }
            catch
            {
                listener.Close();
                throw;
            }
        }
    }

    private sealed class Listener(HttpListener listener, Uri address) : IEditorHttpListener
    {
        public Uri BaseAddress => address;
        public async Task<IEditorHttpContext> AcceptAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var registration = cancellationToken.UnsafeRegister(static value => ((HttpListener)value!).Close(), listener);
            try { return new Context(await listener.GetContextAsync().ConfigureAwait(false)); }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested &&
                exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            { throw new OperationCanceledException("HTTP accept was cancelled.", exception, cancellationToken); }
        }
        public void Dispose() => listener.Close();
    }

    private sealed class Context(HttpListenerContext context) : IEditorHttpContext
    {
        private int _disposed;
        public string Method => context.Request.HttpMethod;
        public Uri Url => context.Request.Url!;
        public long RequestContentLength => context.Request.ContentLength64;
        public Stream RequestBody => context.Request.InputStream;
        public Stream ResponseBody => context.Response.OutputStream;
        public int StatusCode { get => context.Response.StatusCode; set => context.Response.StatusCode = value; }
        public string? ContentType { get => context.Response.ContentType; set => context.Response.ContentType = value; }
        public long ResponseContentLength { set => context.Response.ContentLength64 = value; }
        public void SetResponseHeader(string name, string value) => context.Response.Headers[name] = value;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) context.Response.Close();
        }
    }
}
