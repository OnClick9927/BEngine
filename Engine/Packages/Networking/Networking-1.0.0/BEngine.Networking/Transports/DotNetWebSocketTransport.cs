using BEngine.Networking;
using System.Net.WebSockets;

namespace BEngine.Platform.DotNet;

public sealed class DotNetWebSocketTransport : IWebSocketTransport
{
    private readonly ClientWebSocket _socket = new();
    public WebSocketState State => _socket.State;
    public string? SubProtocol => _socket.SubProtocol;
    public WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;
    public string? CloseStatusDescription => _socket.CloseStatusDescription;

    public INetworkOperation Connect(Uri uri) => new Operation<bool>(async token =>
    { await _socket.ConnectAsync(uri, token).ConfigureAwait(false); return true; });
    public INetworkOperation Send(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage) =>
        new Operation<bool>(async token =>
        { await _socket.SendAsync(buffer, messageType, endOfMessage, token).ConfigureAwait(false); return true; });
    public INetworkOperation<WebSocketFrame> Receive(ArraySegment<byte> buffer) => new Operation<WebSocketFrame>(async token =>
    {
        var result = await _socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
        return new WebSocketFrame(result.Count, result.MessageType, result.EndOfMessage,
            result.CloseStatus, result.CloseStatusDescription);
    });
    public INetworkOperation Close(WebSocketCloseStatus closeStatus, string? statusDescription) =>
        new Operation<bool>(async token =>
        { await _socket.CloseAsync(closeStatus, statusDescription, token).ConfigureAwait(false); return true; });
    public void Abort() => _socket.Abort();
    public void Dispose() => _socket.Dispose();

    private sealed class Operation<T>(Func<CancellationToken, Task<T>> start) : INetworkOperation<T>
    {
        private readonly CancellationTokenSource _cancellation = new();
        private Task<T>? _task;
        private T? _result;
        private bool _disposed;
        public NetworkOperationStatus Status { get; private set; } = NetworkOperationStatus.Running;
        public Exception? Exception { get; private set; }
        public T Result => Status == NetworkOperationStatus.Succeeded ? _result! :
            throw new InvalidOperationException("The platform operation has not succeeded.");
        public void Poll()
        {
            if (_disposed || Status != NetworkOperationStatus.Running) return;
            try
            {
                _task ??= start(_cancellation.Token);
                if (!_task.IsCompleted) return;
                _result = _task.GetAwaiter().GetResult();
                Status = NetworkOperationStatus.Succeeded;
            }
            catch (Exception exception) { Exception = exception; Status = NetworkOperationStatus.Failed; }
        }
        public void Abort()
        {
            if (_disposed || Status != NetworkOperationStatus.Running) return;
            Status = NetworkOperationStatus.Failed;
            Exception = new IOException("The platform WebSocket operation was aborted.");
            _cancellation.Cancel();
        }
        public void Dispose()
        {
            if (_disposed) return;
            Abort();
            _disposed = true;
            if (_task is not null) _ = ObserveAsync(_task);
            _cancellation.Dispose();
        }
        private static async Task ObserveAsync(Task task)
        { try { await task.ConfigureAwait(false); } catch { } }
    }
}
