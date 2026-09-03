using System.Net.WebSockets;
using System.Text;
using BEngine.Networking;

namespace BEngine.ExampleTests.Networking;

internal static class WebSocketNetworkTests
{
    public static async Task RunAsync()
    {
        var transport = new ScriptedWebSocketTransport();
        transport.Enqueue("hel", WebSocketMessageType.Text, false);
        transport.Enqueue("lo", WebSocketMessageType.Text, true);
        await using var client = new WebSocketNetworkClient(transport);

        TestAssert.Equal(WebSocketState.None, client.State,
            "WebSocket initial state was incorrect.");
        await client.ConnectAsync(new Uri("ws://loopback.invalid/socket")).ConfigureAwait(false);
        TestAssert.Require(client.IsConnected, "WebSocket state did not become open.");

        await client.SendTextAsync("request").ConfigureAwait(false);
        TestAssert.Equal("request", Encoding.UTF8.GetString(transport.SentData),
            "WebSocket text message was not sent.");
        TestAssert.Equal(WebSocketMessageType.Text, transport.SentMessageType,
            "WebSocket message type was not retained.");

        var message = await client.ReceiveAsync().ConfigureAwait(false);
        TestAssert.Equal("hello", message.Text,
            "WebSocket fragmented message was not combined.");
        TestAssert.Equal(WebSocketMessageType.Text, message.MessageType,
            "WebSocket received message type was incorrect.");

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done").ConfigureAwait(false);
        TestAssert.Equal(WebSocketState.Closed, client.State,
            "WebSocket close did not update the exposed state.");
        TestAssert.Equal(WebSocketCloseStatus.NormalClosure, client.CloseStatus,
            "WebSocket close status was not exposed.");
        await client.DisposeAsync().ConfigureAwait(false);
        TestAssert.Require(client.IsDisposed, "WebSocket async disposal was not observable.");
        TestAssert.Equal(WebSocketState.Closed, transport.State,
            "WebSocket async disposal did not release the transport.");
    }

    private sealed class ScriptedWebSocketTransport : IWebSocketTransport
    {
        private readonly Queue<Frame> _frames = new();

        public WebSocketState State { get; private set; } = WebSocketState.None;
        public string? SubProtocol => null;
        public WebSocketCloseStatus? CloseStatus { get; private set; }
        public string? CloseStatusDescription { get; private set; }
        public byte[] SentData { get; private set; } = [];
        public WebSocketMessageType SentMessageType { get; private set; }

        public void Enqueue(string data, WebSocketMessageType type, bool endOfMessage) =>
            _frames.Enqueue(new Frame(Encoding.UTF8.GetBytes(data), type, endOfMessage));

        public BValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = WebSocketState.Open;
            return BValueTask.CompletedTask;
        }

        public BValueTask SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SentData = buffer.ToArray();
            SentMessageType = messageType;
            return BValueTask.CompletedTask;
        }

        public BValueTask<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = _frames.Dequeue();
            if (frame.Data.Length > buffer.Count)
                throw new InvalidOperationException("Scripted frame exceeds the receive buffer.");
            frame.Data.CopyTo(buffer.Array!, buffer.Offset);
            return BValueTask<WebSocketReceiveResult>.FromResult(new WebSocketReceiveResult(
                frame.Data.Length, frame.MessageType, frame.EndOfMessage));
        }

        public BValueTask CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseStatus = closeStatus;
            CloseStatusDescription = statusDescription;
            State = WebSocketState.Closed;
            return BValueTask.CompletedTask;
        }

        public void Abort() => State = WebSocketState.Aborted;
        public void Dispose() => State = WebSocketState.Closed;

        private sealed record Frame(
            byte[] Data,
            WebSocketMessageType MessageType,
            bool EndOfMessage);
    }
}
