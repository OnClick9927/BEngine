using System.Net;

namespace BEngine.Networking;

public sealed class UdpNetworkClient : NetworkClientBase
{
    private readonly IUdpTransport _transport;

    public UdpNetworkClient() : this(NetworkTransportRegistry.Sockets.CreateUdpTransport())
    {
    }

    public UdpNetworkClient(IPEndPoint localEndPoint) : this(NetworkTransportRegistry.Sockets.CreateUdpTransport(
        localEndPoint ?? throw new ArgumentNullException(nameof(localEndPoint))))
    {
    }

    public UdpNetworkClient(IUdpTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public NetworkClientState State { get; private set; } = NetworkClientState.Disconnected;
    public IPEndPoint? LocalEndPoint => _transport.LocalEndPoint;
    public IPEndPoint? RemoteEndPoint => _transport.RemoteEndPoint;

    public void Connect(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ThrowIfDisposed();
        if (State == NetworkClientState.Closed) throw new InvalidOperationException("The UDP client is closed.");
        ValidatePort(port);
        _transport.Connect(host, port);
        State = NetworkClientState.Connected;
    }

    public void Connect(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        Connect(address.ToString(), port);
    }

    public BTask<int> SendAsync(
        ReadOnlyMemory<byte> data,
        BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State != NetworkClientState.Connected)
            throw new InvalidOperationException(
                "Connect the UDP client or provide a remote endpoint.");
        return RunOperationAsync(
            _transport.Send(data), cancellationToken);
    }

    public BTask<int> SendAsync(
        ReadOnlyMemory<byte> data,
        IPEndPoint remoteEndPoint,
        BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        return RunOperationAsync(
            _transport.Send(data, remoteEndPoint), cancellationToken);
    }

    public BTask<NetworkDatagram> ReceiveAsync(BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return RunOperationAsync(_transport.Receive(), cancellationToken);
    }

    public void Close()
    {
        ThrowIfDisposed();
        _transport.Close();
        State = NetworkClientState.Closed;
        SetClosedResult();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _transport.Dispose();
        State = NetworkClientState.Closed;
    }

    private static void ValidatePort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        if (port > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(port));
    }
}
