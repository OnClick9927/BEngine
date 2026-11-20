using BEngine.Platform;
using System.Net.WebSockets;

namespace BEngine.Platform.DotNet;

public sealed class DotNetWebSocketTransport : IPlatformWebSocketTransport
{
    private readonly ClientWebSocket _socket = new();
    public WebSocketState State => _socket.State;
    public string? SubProtocol => _socket.SubProtocol;
    public WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;
    public string? CloseStatusDescription => _socket.CloseStatusDescription;

    public IPlatformNetworkOperation Connect(Uri uri) => new Operation<bool>(async token =>
    { await _socket.ConnectAsync(uri, token).ConfigureAwait(false); return true; });
    public IPlatformNetworkOperation Send(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage) =>
        new Operation<bool>(async token =>
        { await _socket.SendAsync(buffer, messageType, endOfMessage, token).ConfigureAwait(false); return true; });
    public IPlatformNetworkOperation<PlatformWebSocketFrame> Receive(ArraySegment<byte> buffer) => new Operation<PlatformWebSocketFrame>(async token =>
    {
        var result = await _socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
        return new PlatformWebSocketFrame(result.Count, result.MessageType, result.EndOfMessage,
            result.CloseStatus, result.CloseStatusDescription);
    });
    public IPlatformNetworkOperation Close(WebSocketCloseStatus closeStatus, string? statusDescription) =>
        new Operation<bool>(async token =>
        { await _socket.CloseAsync(closeStatus, statusDescription, token).ConfigureAwait(false); return true; });
    public void Abort() => _socket.Abort();
    public void Dispose() => _socket.Dispose();

    private sealed class Operation<T>(Func<CancellationToken, Task<T>> start) : IPlatformNetworkOperation<T>
    {
        private readonly CancellationTokenSource _cancellation = new();
        private Task<T>? _task;
        private T? _result;
        private bool _disposed;
        public PlatformNetworkOperationStatus Status { get; private set; } = PlatformNetworkOperationStatus.Running;
        public Exception? Exception { get; private set; }
        public T Result => Status == PlatformNetworkOperationStatus.Succeeded ? _result! :
            throw new InvalidOperationException("The platform operation has not succeeded.");
        public void Poll()
        {
            if (_disposed || Status != PlatformNetworkOperationStatus.Running) return;
            try
            {
                _task ??= start(_cancellation.Token);
                if (!_task.IsCompleted) return;
                _result = _task.GetAwaiter().GetResult();
                Status = PlatformNetworkOperationStatus.Succeeded;
            }
            catch (Exception exception) { Exception = exception; Status = PlatformNetworkOperationStatus.Failed; }
        }
        public void Abort()
        {
            if (_disposed || Status != PlatformNetworkOperationStatus.Running) return;
            Status = PlatformNetworkOperationStatus.Failed;
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
