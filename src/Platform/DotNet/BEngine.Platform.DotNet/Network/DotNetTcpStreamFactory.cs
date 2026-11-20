using System.Net;
using System.Net.Sockets;

namespace BEngine.Platform.DotNet;

/// <summary>System stream sockets, without framing, authentication or application policy.</summary>
public sealed class DotNetTcpStreamFactory : IPlatformTcpStreamFactory
{
    public static DotNetTcpStreamFactory Shared { get; } = new();

    public IPlatformTcpStreamListener Listen(IPEndPoint endPoint, int backlog = 16)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(backlog);
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("TCP streams are not supported in the browser.");
        var listener = new TcpListener(endPoint);
        try
        {
            listener.Start(backlog);
            return new Listener(listener);
        }
        catch
        {
            listener.Stop();
            throw;
        }
    }

    public async Task<IPlatformTcpStreamConnection> ConnectAsync(string host, int port, bool noDelay = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("TCP streams are not supported in the browser.");
        var client = new TcpClient { NoDelay = noDelay };
        try
        {
            await client.ConnectAsync(host.Trim(), port, cancellationToken).ConfigureAwait(false);
            return new Connection(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed class Listener(TcpListener listener) : IPlatformTcpStreamListener
    {
        public IPEndPoint LocalEndPoint => (IPEndPoint)listener.LocalEndpoint;

        public async Task<IPlatformTcpStreamConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            try { return new Connection(client); }
            catch { client.Dispose(); throw; }
        }

        public void Dispose() => listener.Stop();
    }

    private sealed class Connection : IPlatformTcpStreamConnection
    {
        private readonly TcpClient _client;
        public Connection(TcpClient client) { _client = client; Stream = client.GetStream(); }
        public EndPoint? LocalEndPoint => _client.Client.LocalEndPoint;
        public EndPoint? RemoteEndPoint => _client.Client.RemoteEndPoint;
        public Stream Stream { get; }
        public void Dispose() => _client.Dispose();
    }
}
