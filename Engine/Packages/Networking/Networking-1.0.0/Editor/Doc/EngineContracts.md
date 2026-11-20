# Networking implementation contracts

## Use and extend cross-platform networking

- Use `BEngine.Networking` for portable TCP, UDP, HTTP, and WebSocket requests and BTask workflows. Native Socket/HTTP/WebSocket I/O belongs to Platform, behind neutral host contracts. The package retains HTTP/1.1 parsing over a nonblocking TCP transport; reuse existing transports rather than copying protocol implementations.
- Pass a `BCancellationToken` to operations and set a finite `Timeout` when the workflow needs a deadline. Cancel from the owning thread. For TCP, UDP, and WebSocket inspect `LastResult`, `Error`, and `Exception` after failure. For HTTP inspect `Result`, `Error`, `ResponseCode`, response headers, and `DownloadData`/`DownloadText`.
- Handle `TimedOut`, `Canceled`, `ConnectionError`, `ProtocolError`, and `DataProcessingError` distinctly. Dispose or asynchronously dispose every client and request; do not reuse a sent `HttpNetworkRequest` or a used WebSocket client.
- Use `TcpNetworkClient.ConnectAsync/SendAsync/ReceiveAsync/CloseAsync` for streams. Use a bound `UdpNetworkClient(IPEndPoint)` and either connected or endpoint-based `SendAsync`; `ReceiveAsync` returns a `NetworkDatagram` with sender endpoint.
- Use `HttpNetworkRequest.Get/Delete/Post/Put` for one-shot calls. Set headers before `SendAsync`. Use `HttpNetworkRequest(Uri, HttpRequestMethod, IHttpTransport?)` to inject a domain transport; use `IPlatformHttpTransport` when implementing a host capability. There is no direct HttpClient/HttpMessageHandler constructor.
- Use `HttpNetworkRequest.SendAsync(IHttpDownloadHandler, BCancellationToken)` to stream large responses without filling `DownloadData`. Validate status and enforce application size limits inside the handler. Borrowed response memory is valid only during the callback.
- Use `WebSocketNetworkClient.SendTextAsync`, `SendBinaryAsync`, bounded `ReceiveAsync(maximumMessageSize)`, and `CloseAsync`. Inject `IWebSocketTransport` for custom behavior; the default `PlatformWebSocketTransport` delegates to the host's `IPlatformWebSocketTransport`.
- Prefer composition around sealed clients. Platform owns native TLS/handshake/I/O. Hosts register `AddBEngineDotNetPlatform`; package initialization registers domain adapters. The old `AddBEngineDotNetNetworking` entry point is removed. Public default transport policies are described in Overview.md; unsupported platform capabilities must not silently report success.

```csharp
using var stop = new BEngine.BCancellationSource();
using var request = HttpNetworkRequest.Get("https://example.test/status");
request.Timeout = TimeSpan.FromSeconds(5);
request.SetRequestHeader("Accept", "application/json");
await request.SendAsync(stop.Token);
if (request.Result == NetworkRequestResult.Success)
    Console.WriteLine(request.DownloadText);
```
