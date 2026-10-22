using System.Net;

namespace BEngine.Networking;

public sealed class TcpNetworkClient : NetworkClientBase
{
    private readonly ITcpTransport _transport;
    private bool _noDelay;

    public NetworkClientState State { get; private set; } = NetworkClientState.Disconnected;
    public TcpNetworkClient() : this(NetworkTransportRegistry.Sockets.CreateTcpTransport()) { }
    public TcpNetworkClient(ITcpTransport transport) =>
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    public bool IsConnected => State == NetworkClientState.Connected && _transport.IsConnected;
    public EndPoint? LocalEndPoint => _transport.LocalEndPoint;
    public EndPoint? RemoteEndPoint => _transport.RemoteEndPoint;
    public bool NoDelay
    {
        get => _transport.NoDelay;
        set
        {
            _noDelay = value;
            _transport.NoDelay = value;
        }
    }

    public async BTask ConnectAsync(
        string host,
        int port,
        BCancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ValidatePort(port);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (State != NetworkClientState.Disconnected)
            throw new InvalidOperationException("The TCP client has already been connected.");

        _transport.NoDelay = _noDelay;
        State = NetworkClientState.Connecting;
        try
        {
            await RunOperationAsync(
                _transport.Connect(host, port),
                cancellationToken);
            State = NetworkClientState.Connected;
        }
        catch
        {
            _transport.Close();
            if (State != NetworkClientState.Closed) State = NetworkClientState.Faulted;
            throw;
        }
    }

    public BTask ConnectAsync(IPAddress address, int port, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        return ConnectAsync(address.ToString(), port, cancellationToken);
    }

    public async BTask SendAsync(
        ReadOnlyMemory<byte> data,
        BCancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await RunOperationAsync(_transport.Send(data), cancellationToken);
    }

    public BTask<int> ReceiveAsync(
        Memory<byte> buffer,
        BCancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (buffer.IsEmpty)
            throw new ArgumentException("The receive buffer cannot be empty.", nameof(buffer));
        return RunOperationAsync(_transport.Receive(buffer), cancellationToken);
    }

    public BTask CloseAsync(BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (State == NetworkClientState.Closed) return BTask.CompletedTask;
        State = NetworkClientState.Closing;
        _transport.Close();
        State = NetworkClientState.Closed;
        SetClosedResult();
        return BTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _transport.Dispose();
        State = NetworkClientState.Closed;
    }

    private void EnsureConnected()
    {
        ThrowIfDisposed();
        if (!IsConnected) throw new InvalidOperationException("The TCP client is not connected.");
    }

    private static void ValidatePort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        if (port > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(port));
    }
}
