using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace BEngine.Networking;

public sealed class WebSocketNetworkClient : NetworkClientBase
{
    private readonly IWebSocketTransport _transport;

    public WebSocketNetworkClient() : this(NetworkTransportRegistry.CreateWebSocketTransport())
    {
    }

    public WebSocketNetworkClient(IWebSocketTransport transport) =>
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public WebSocketState State => _transport.State;
    public string? SubProtocol => _transport.SubProtocol;
    public WebSocketCloseStatus? CloseStatus => _transport.CloseStatus;
    public string? CloseStatusDescription => _transport.CloseStatusDescription;
    public bool IsConnected => State == WebSocketState.Open;

    public BTask ConnectAsync(Uri uri, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("ws" or "wss"))
            throw new ArgumentException(
                "WebSocket connections require an absolute WS or WSS URI.", nameof(uri));
        if (State != WebSocketState.None)
            throw new InvalidOperationException("The WebSocket client has already been used.");
        return RunOperationAsync(_transport.Connect(uri), cancellationToken);
    }

    public BTask SendTextAsync(string text, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SendAsync(
            Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, cancellationToken);
    }

    public BTask SendBinaryAsync(
        ReadOnlyMemory<byte> data,
        BCancellationToken cancellationToken = default) =>
        SendAsync(data, WebSocketMessageType.Binary, cancellationToken);

    public BTask SendAsync(
        ReadOnlyMemory<byte> data,
        WebSocketMessageType messageType,
        BCancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (messageType is not (WebSocketMessageType.Text or WebSocketMessageType.Binary))
            throw new ArgumentOutOfRangeException(nameof(messageType));
        var bytes = data.ToArray();
        return RunOperationAsync(
            _transport.Send(new ArraySegment<byte>(bytes), messageType, true),
            cancellationToken);
    }

    public async BTask<WebSocketMessage> ReceiveAsync(
        int maximumMessageSize = 1024 * 1024,
        BCancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessageSize);
        var bufferLength = Math.Min(16 * 1024, maximumMessageSize);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
        MemoryStream? message = null;
        var bufferReusable = true;
        try
        {
            while (true)
            {
                bufferReusable = false;
                var frame = await RunOperationAsync(
                    _transport.Receive(new ArraySegment<byte>(buffer, 0, bufferLength)), cancellationToken);
                bufferReusable = true;
                if (frame.MessageType == WebSocketMessageType.Close)
                    return new WebSocketMessage(
                        WebSocketMessageType.Close,
                        [],
                        frame.CloseStatus,
                        frame.CloseStatusDescription);
                if (frame.Count < 0 || frame.Count > bufferLength)
                    throw new InvalidDataException("The WebSocket transport returned an invalid byte count.");
                if ((message?.Length ?? 0) + frame.Count > maximumMessageSize)
                    throw new InvalidDataException(
                        $"The WebSocket message exceeds the {maximumMessageSize} byte limit.");
                if (message is null && frame.EndOfMessage)
                    return new WebSocketMessage(frame.MessageType, buffer.AsSpan(0, frame.Count).ToArray());
                message ??= new MemoryStream();
                message.Write(buffer, 0, frame.Count);
                if (frame.EndOfMessage)
                    return new WebSocketMessage(frame.MessageType, message.ToArray());
            }
        }
        finally
        {
            message?.Dispose();
            // A canceled transport can still be completing an asynchronous write.
            // Only a successful Receive confirms that this buffer can be reused.
            if (bufferReusable) ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public BTask CloseAsync(
        WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure,
        string? statusDescription = null,
        BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State is WebSocketState.Closed or WebSocketState.Aborted)
            return BTask.CompletedTask;
        return RunOperationAsync(
            _transport.Close(closeStatus, statusDescription), cancellationToken);
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
        if (!IsConnected)
            throw new InvalidOperationException("The WebSocket client is not connected.");
    }
}
