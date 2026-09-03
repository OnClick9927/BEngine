using System.Net.Sockets;

namespace BEngine.Networking;

public sealed class TcpNetworkClient : NetworkClientBase
{
    private readonly TcpClient _client;

    public TcpNetworkClient() : this(new TcpClient())
    {
    }

    internal TcpNetworkClient(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        State = client.Connected ? NetworkClientState.Connected : NetworkClientState.Disconnected;
    }

    public NetworkClientState State { get; private set; }
    public bool IsConnected => State == NetworkClientState.Connected && _client.Connected;
    public System.Net.EndPoint? LocalEndPoint => _client.Client.LocalEndPoint;
    public System.Net.EndPoint? RemoteEndPoint => _client.Client.RemoteEndPoint;
    public bool NoDelay
    {
        get => _client.NoDelay;
        set => _client.NoDelay = value;
    }

    public async BValueTask ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        if (port > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(port));
        if (State is not NetworkClientState.Disconnected)
            throw new InvalidOperationException("The TCP client has already been connected.");

        State = NetworkClientState.Connecting;
        try
        {
            await RunValueAsync(
                token => _client.ConnectAsync(host, port, token),
                cancellationToken).ConfigureAwait(false);
            State = NetworkClientState.Connected;
        }
        catch
        {
            State = NetworkClientState.Faulted;
            throw;
        }
    }

    public BValueTask SendAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return RunValueAsync(
            token => _client.GetStream().WriteAsync(data, token),
            cancellationToken);
    }

    public BValueTask<int> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (buffer.IsEmpty) throw new ArgumentException("The receive buffer cannot be empty.", nameof(buffer));
        return RunValueAsync(
            token => _client.GetStream().ReadAsync(buffer, token),
            cancellationToken);
    }

    public BValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (State is NetworkClientState.Closed) return BValueTask.CompletedTask;
        State = NetworkClientState.Closing;
        _client.Close();
        State = NetworkClientState.Closed;
        SetClosedResult();
        return BValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _client.Dispose();
        State = NetworkClientState.Closed;
    }

    private void EnsureConnected()
    {
        ThrowIfDisposed();
        if (!IsConnected) throw new InvalidOperationException("The TCP client is not connected.");
    }
}
