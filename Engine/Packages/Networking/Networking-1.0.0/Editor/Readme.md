# Networking

TCP, UDP and WebSocket asynchronous clients.

HTTP 的 `Timeout` 限制请求总时长，`InactivityTimeout` 限制连续未收到响应头或数据的时长，默认不启用后者。大文件可设置 `Timeout = HttpNetworkRequest.InfiniteTimeout` 并配置有限的 `InactivityTimeout`：持续接收数据不会因下载总时长过长而中止，真正停滞仍会返回 `TimedOut`。超时检查先处理传输进展，避免主线程长帧后将已经完成的响应误判超时。取消和释放仍会立即中止请求。

`DotNetHttpTransport` 对已知长度的大响应连续异步填充最多 1 MiB 的缓冲区，不再为服务器返回的每个小块等待下一游戏帧；缓冲区填满后等待所属线程消费，接收缓冲不会随下载增长。进度按实际收到的字节更新，下载回调只在所属线程的 `Poll` 中执行，每次 `Poll` 最多交付 1 MiB。未知长度的流继续及时交付部分数据。

Web 默认使用浏览器原生 `fetch` 异步接收，JavaScript 预读独立于游戏帧推进，所属主线程的 `Poll` 每次最多向下载 handler 交付 1 MiB。每个请求的 JavaScript 接收缓冲在 BYOB reader 模式下最多 2 MiB，普通 reader 回退模式下最多 3.75 MiB；最终响应内容、托管交付缓冲和浏览器内部缓冲另计。`Abort` 中止接收，`Dispose` 释放请求句柄和接收缓冲。

旧 Web Host 缺少原生 HTTP 桥，或桥明确未接受请求（不支持或并发已满）时，自动回退到现有 .NET HTTP 后端。已经启动的请求若失败，会保留失败结果交给上层处理，不会通过后端回退自动重发 `POST` 或其他上传请求。

Enable com.bengine.networking in Package Manager. Disabling the package removes its tools and types from the project.

### 跨平台网络快速开始

`BEngine.Networking` 使用 .NET 的 `TcpClient`、`UdpClient`、`HttpClient` 和 `ClientWebSocket`，Web HTTP 优先使用浏览器 `fetch`，不自行实现协议。所有异步操作接受 `CancellationToken`；TCP、UDP 和 WebSocket 通过 `Timeout`、`LastResult`、`Error`、`Exception` 暴露最近状态，HTTP 请求通过 `Result`、`Error`、`ResponseCode`、`DownloadData/DownloadText` 暴露完成状态。`TimedOut`、`Canceled`、`ConnectionError`、`ProtocolError` 和 `DataProcessingError` 应分别处理。客户端和请求都必须 `Dispose`/`DisposeAsync`。

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

