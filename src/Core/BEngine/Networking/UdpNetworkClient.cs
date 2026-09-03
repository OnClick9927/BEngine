using System.Net;
using System.Net.Sockets;

namespace BEngine.Networking;

public sealed class UdpNetworkClient : NetworkClientBase
{
    private readonly UdpClient _client;

    public UdpNetworkClient() : this(new UdpClient())
    {
    }

    public UdpNetworkClient(IPEndPoint localEndPoint) : this(
        new UdpClient(localEndPoint ?? throw new ArgumentNullException(nameof(localEndPoint))))
    {
    }

    private UdpNetworkClient(UdpClient client)
    {
        _client = client;
        State = NetworkClientState.Disconnected;
    }

    public NetworkClientState State { get; private set; }
    public IPEndPoint? LocalEndPoint => _client.Client.LocalEndPoint as IPEndPoint;
    public IPEndPoint? RemoteEndPoint => _client.Client.RemoteEndPoint as IPEndPoint;

    public void Connect(string host, int port)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        if (port > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(port));
        _client.Connect(host, port);
        State = NetworkClientState.Connected;
    }

    public BValueTask<int> SendAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State != NetworkClientState.Connected)
            throw new InvalidOperationException("Connect the UDP client or provide a remote endpoint.");
        return RunValueAsync(
            token => _client.SendAsync(data, token),
            cancellationToken);
    }

    public BValueTask<int> SendAsync(
        ReadOnlyMemory<byte> data,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        return RunValueAsync(
            token => _client.SendAsync(data, remoteEndPoint, token),
            cancellationToken);
    }

    public async BValueTask<NetworkDatagram> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunValueAsync(
            token => _client.ReceiveAsync(token),
            cancellationToken).ConfigureAwait(false);
        return new NetworkDatagram(result.Buffer, result.RemoteEndPoint);
    }

    public void Close()
    {
        ThrowIfDisposed();
        _client.Close();
        State = NetworkClientState.Closed;
        SetClosedResult();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _client.Dispose();
        State = NetworkClientState.Closed;
    }
}
