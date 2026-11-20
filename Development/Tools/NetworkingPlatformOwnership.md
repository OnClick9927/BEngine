# Networking 与宿主网络能力

Networking 保留 HTTP/TCP/UDP/WebSocket 客户端、状态与错误、超时、内存准入、
BTask 工作流、流式下载 handler 和 HTTP/1.1 解析。它不再实现原生 Socket、
HttpClient、ClientWebSocket 或浏览器导入，也不引用具体平台程序集。

Platform.Contracts 提供 IPlatformHttpTransport、IPlatformSocketTransportFactory、
IPlatformWebSocketTransport 及可轮询操作。Platform.DotNet 提供系统实现；Web 提供
fetch 桥及提交前的后端选择。已接受请求发生错误后不回退重发，避免重复上传。
Core、平台宿主及 Platform 无需知道 Networking 包的身份。

Networking 的 PlatformHttpTransport、PlatformWebSocketTransport 和原有
SocketTransportFactory 适配上述中立能力。HTTP 上传/头集合及回调块按约定借用，
适配器不复制正文；UDP 领域结果只映射一次。模块贡献随包程序集退役，宿主能力保留。
外部实例的 Dispose、取消及业务生命周期仍由持有者负责。

## API 迁移

- 领域 IHttpTransport、ISocketTransportFactory、IWebSocketTransport 和客户端构造注入保留。
- 旧 AddBEngineDotNetNetworking 移除；宿主注册 AddBEngineDotNetPlatform，包初始化注册领域适配器。
- DotNetHttpTransport/DotNetWebSocketTransport 从 Networking 迁至 Platform.DotNet，
  返回中立平台操作。直接使用旧实现的插件需重编译；领域消费者改用
  PlatformHttpTransport/PlatformWebSocketTransport。具体平台库不依赖领域包。
- SocketHttpTransport 的协议解析留在包内，通过中立 TCP 的非阻塞读写接入。
  默认 TCP/UDP 仍只解析 IP 地址和 localhost，本轮没有扩大 DNS 或浏览器 raw socket 支持。
- 原生默认 HttpClient 延迟到首次真实 HTTP 请求创建，宿主注册不提前分配客户端。

已知长度的 .NET HTTP 响应每次 Poll 最多交付 1 MiB；未知长度流的末次读取可能
多交付最多 64 KiB，保留原有行为。Web 正常 Poll 最多 1 MiB，加载时依照剩余帧
预算最多 4 MiB。所有领域回调及 BTask 完成仍发生在所属线程的 Poll 中。

## 验证

结果位于工作区 Temp/Engine/Verification/NetworkingOwnership_20261114。
TransportTests 使用迁移前源快照与当前程序集连接真实回环服务，核对正文哈希、
分块/截断响应、HEAD/204、上传、流式响应、取消释放、TCP/UDP/WebSocket。
额外验证领域自定义注入、中立宿主替换、借用内存及模块退役。

本机 Edge 验证真实 fetch、托管导入、精确进度、请求只提交一次、BTask 和释放；
WebAssembly 首次循环存在解释器预热分配，记录预热各轮后再断言稳态零分配。
严格编译覆盖 Networking、Web、Desktop、Android、iOS 和 AB Editor；移动平台
未在设备运行。44 包的编译图、包边界及 Cecil 程序集归属另有独立校验。

本轮是职责迁移；Networking DLL 缩小不代表整个 Player 包体等比例变小。
整仓 PlayerCheck 仍有先前渲染与 Launcher/Core API 迁移问题，不能以本项测试代替
全量游戏回归。Video、ICC、AB 浏览器哈希、诊断 I/O 等职责仍需继续逐项审查，
整个 Core 与 44 个包的语义整理尚未完成。
