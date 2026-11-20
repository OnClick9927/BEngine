# Networking

独立 Networking 包，提供 HTTP、TCP、UDP 与 WebSocket 异步客户端；运行时程序集为 `BEngine.Networking`，公开 API 命名空间保持不变。

HTTP 的 `Timeout` 限制请求总时长，`InactivityTimeout` 限制连续未收到响应头或数据的时长，默认不启用后者。大文件可设置 `Timeout = HttpNetworkRequest.InfiniteTimeout` 并配置有限的 `InactivityTimeout`：持续接收数据不会因下载总时长过长而中止，真正停滞仍会返回 `TimedOut`。超时检查先处理传输进展，避免主线程长帧后将已经完成的响应误判超时。取消和释放仍会立即中止请求。

`PlatformHttpTransport` 将请求交给宿主的 `IPlatformHttpTransport`，不自行选择操作系统或浏览器后端。Platform.DotNet 中的 `DotNetHttpTransport` 对已知长度的大响应连续异步填充最多 1 MiB 的缓冲区，填满后等待所属线程消费，接收缓冲不会随下载增长。进度按实际收到的字节更新，下载回调只在所属线程的 `Poll` 中执行。已知长度响应每次最多交付 1 MiB；未知长度流及时交付部分数据，最后一个待完成的读取块可能使单次交付多出最多 64 KiB。

Web 宿主注册 `WebHttpTransport`，默认使用浏览器原生 `fetch` 异步接收。JavaScript 预读独立于游戏帧推进，正常帧的 `Poll` 每次最多交付 1 MiB；加载阶段可在剩余帧预算内分块交付至多 4 MiB。正常请求的 JavaScript 接收缓冲在 BYOB reader 模式下最多 2 MiB，普通 reader 回退模式下最多 3.75 MiB；加载阶段的额外缓冲受共享配额约束。最终响应内容、托管交付缓冲和浏览器内部缓冲另计。`Abort` 中止接收，`Dispose` 释放请求句柄和接收缓冲。

旧 Web Host 缺少原生 HTTP 桥，或桥明确未接受请求（不支持或并发已满）时，自动回退到现有 .NET HTTP 后端。已经启动的请求若失败，会保留失败结果交给上层处理，不会通过后端回退自动重发 `POST` 或其他上传请求。

在 Package Manager 中启用 `com.bengine.networking`；Application 通过包依赖自动启用它。仅单独使用网络 API 的程序集引用 `BEngine.Networking`，无需引用 Application。

### 跨平台网络快速开始

`BEngine.Networking` 提供请求、客户端状态、超时、内存准入、HTTP/1.1 解析及 BTask 工作流。原生 `Socket`、`HttpClient`、`ClientWebSocket` 和浏览器导入位于 Platform，通过中立接口接入；Networking 不引用具体平台程序集。运行时异步操作接受 `BCancellationToken`，由所属线程推进 BTask 调度；平台内部可使用系统异步 I/O。TCP、UDP 和 WebSocket 通过 `Timeout`、`LastResult`、`Error`、`Exception` 暴露最近状态，HTTP 请求通过 `Result`、`Error`、`ResponseCode`、`DownloadData/DownloadText` 暴露完成状态。`TimedOut`、`Canceled`、`ConnectionError`、`ProtocolError` 和 `DataProcessingError` 应分别处理。客户端和请求都必须 `Dispose`/`DisposeAsync`。

```csharp
using System.Net;
using System.Text;
using BEngine;
using BEngine.Networking;

using var stop = new BCancellationSource(); // 在所属线程调用 stop.Cancel()。

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

HTTP 提供 `Get/Delete/Post/Put` 工厂，也可用 `HttpNetworkRequest(Uri, HttpRequestMethod, IHttpTransport?)` 注入领域传输。大响应使用 `SendAsync(IHttpDownloadHandler, BCancellationToken)` 流式处理；回调数据只在当前回调内有效，需持有时自行复制。WebSocket 可向 `WebSocketNetworkClient` 注入 `IWebSocketTransport`，默认 `PlatformWebSocketTransport` 委托宿主；TCP/UDP 默认 `SocketTransportFactory` 也是中立平台能力的适配器。应用仍可注册或注入自己的领域传输。

宿主用 `AddBEngineDotNetPlatform` 注册中立网络能力，Web 宿主覆盖 HTTP；旧 `AddBEngineDotNetNetworking` 已移除。Networking 的模块初始化只注册领域适配器，程序集退役时随贡献注销。原来位于 Networking 程序集的 `DotNetHttpTransport`/`DotNetWebSocketTransport` 现在归 Platform.DotNet，并实现中立接口；直接引用旧实现的插件需重新编译并改用领域适配器或中立宿主接口。HTTPS、WebSocket TLS 和默认 HTTP DNS 由平台实现；默认原生 TCP/UDP 仍只接受 IP 地址及 localhost，需要其他解析策略时提供宿主或领域传输。

