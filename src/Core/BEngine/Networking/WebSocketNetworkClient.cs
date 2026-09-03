using System.Net.WebSockets;
using System.Text;

namespace BEngine.Networking;

public sealed class WebSocketNetworkClient : NetworkClientBase
{
    private readonly IWebSocketTransport _transport;

    public WebSocketNetworkClient() : this(new ClientWebSocketTransport())
    {
    }

    public WebSocketNetworkClient(IWebSocketTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public WebSocketState State => _transport.State;
    public string? SubProtocol => _transport.SubProtocol;
    public WebSocketCloseStatus? CloseStatus => _transport.CloseStatus;
    public string? CloseStatusDescription => _transport.CloseStatusDescription;
    public bool IsConnected => State == WebSocketState.Open;

    public BValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("ws" or "wss"))
            throw new ArgumentException("WebSocket connections require an absolute WS or WSS URI.", nameof(uri));
        if (State != WebSocketState.None)
            throw new InvalidOperationException("The WebSocket client has already been used.");
        return RunAsync(token => _transport.ConnectAsync(uri, token), cancellationToken);
    }

    public BValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, cancellationToken);
    }

    public BValueTask SendBinaryAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        SendAsync(data, WebSocketMessageType.Binary, cancellationToken);

    public BValueTask SendAsync(
        ReadOnlyMemory<byte> data,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (messageType is not (WebSocketMessageType.Text or WebSocketMessageType.Binary))
            throw new ArgumentOutOfRangeException(nameof(messageType));
        var bytes = data.ToArray();
        return RunAsync(
            token => _transport.SendAsync(
                new ArraySegment<byte>(bytes), messageType, true, token),
            cancellationToken);
    }

    public async BValueTask<WebSocketMessage> ReceiveAsync(
        int maximumMessageSize = 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessageSize);
        return await RunAsync(async token =>
        {
            var buffer = new byte[Math.Min(16 * 1024, maximumMessageSize)];
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _transport.ReceiveAsync(
                    new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return new WebSocketMessage(
                        WebSocketMessageType.Close,
                        [],
                        result.CloseStatus,
                        result.CloseStatusDescription);
                if (message.Length + result.Count > maximumMessageSize)
                    throw new InvalidDataException(
                        $"The WebSocket message exceeds the {maximumMessageSize} byte limit.");
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            return new WebSocketMessage(result.MessageType, message.ToArray());
        }, cancellationToken).ConfigureAwait(false);
    }

    public BValueTask CloseAsync(
        WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure,
        string? statusDescription = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State is WebSocketState.Closed or WebSocketState.Aborted) return BValueTask.CompletedTask;
        return RunAsync(
            token => _transport.CloseAsync(closeStatus, statusDescription, token),
            cancellationToken);
    }

    public void Abort()
    {
        ThrowIfDisposed();
        _transport.Abort();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _transport.Dispose();
    }

    private void EnsureConnected()
    {
        ThrowIfDisposed();
        if (!IsConnected) throw new InvalidOperationException("The WebSocket client is not connected.");
    }
}
