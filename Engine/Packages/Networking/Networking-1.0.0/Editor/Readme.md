# Networking

TCP, UDP and WebSocket asynchronous clients.

Enable com.bengine.networking in Package Manager. Disabling the package removes its tools and types from the project.

### 跨平台网络快速开始

`BEngine.Networking` 只使用 .NET 的 `TcpClient`、`UdpClient`、`HttpClient` 和 `ClientWebSocket`，不自行实现协议。所有异步操作接受 `CancellationToken`；TCP、UDP 和 WebSocket 通过 `Timeout`、`LastResult`、`Error`、`Exception` 暴露最近状态，HTTP 请求通过 `Result`、`Error`、`ResponseCode`、`DownloadData/DownloadText` 暴露完成状态。`TimedOut`、`Canceled`、`ConnectionError`、`ProtocolError` 和 `DataProcessingError` 应分别处理。客户端和请求都必须 `Dispose`/`DisposeAsync`。

```csharp
using System.Net;
using System.Text;
using BEngine.Networking;

using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));

await using var tcp = new TcpNetworkClient { Timeout = TimeSpan.FromSeconds(5) };
await tcp.ConnectAsync("127.0.0.1", 7000, stop.Token);
await tcp.SendAsync(Encoding.UTF8.GetBytes("ping"), stop.Token);
var tcpBuffer = new byte[1024];
var tcpCount = await tcp.ReceiveAsync(tcpBuffer, stop.Token);

await using var udp = new UdpNetworkClient(new IPEndPoint(IPAddress.Any, 0));
await udp.SendAsync(
    Encoding.UTF8.GetBytes("status"),
    new IPEndPoint(IPAddress.Loopback, 7001),
    stop.Token);
NetworkDatagram datagram = await udp.ReceiveAsync(stop.Token);

using var request = HttpNetworkRequest.Get("https://example.test/status");
request.Timeout = TimeSpan.FromSeconds(5);
request.SetRequestHeader("Accept", "application/json");
await request.SendAsync(stop.Token);
if (request.Result == NetworkRequestResult.Success)
    Console.WriteLine(request.DownloadText);

await using var socket = new WebSocketNetworkClient();
await socket.ConnectAsync(new Uri("wss://example.test/events"), stop.Token);
await socket.SendTextAsync("subscribe", stop.Token);
WebSocketMessage message = await socket.ReceiveAsync(
    maximumMessageSize: 256 * 1024,
    cancellationToken: stop.Token);
await socket.CloseAsync(cancellationToken: stop.Token);
```

HTTP 提供 `Get/Delete/Post/Put` 工厂，也可在构造函数注入共享 `HttpClient` 或 `HttpMessageHandler`；默认不会释放外部 `HttpClient`。需要流式处理大响应时使用 `SendAsync(Func<HttpResponseMessage, CancellationToken, Task>, token)`，不要先缓冲到 `DownloadData`。WebSocket 的扩展边界是 `IWebSocketTransport`：生产环境默认使用 `ClientWebSocketTransport`，可先配置其 `Options` 再传给 `WebSocketNetworkClient`，测试或平台适配可注入自己的 transport。协议、TLS、代理、压缩和握手仍交给 .NET 实现；扩展包应组合这些 sealed 客户端或实现 transport，不应复制协议栈。

