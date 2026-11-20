# Networking

独立网络包 `com.bengine.networking`，提供 HTTP 请求、流式下载、TCP、UDP 与 WebSocket。
运行时程序集和公开 API 命名空间均为 `BEngine.Networking`，仅依赖 Core；Application 的 AB 网络读取依赖本包。

在 Package Manager 中启用 Networking，或通过 Application 自动安装依赖。自定义程序集若在合并期间通过 `BEngine.Player` 引用网络类型，需改为引用 `BEngine.Networking` 后重新编译；现有 `using BEngine.Networking` 不变。

- [网络快速开始与请求生命周期](Doc/Overview.md)
- [跨平台运行指南](Doc/runtime-guide.html)

Web HTTP 优先通过浏览器 `fetch` 传输；浏览器平台的原始 TCP/UDP 能力受平台限制。进度与完成由所属线程轮询，平台异步接收不会直接修改游戏状态。
在 Examples 页签可导入 Networking Getting Started。

[实现与资源生命周期约定](Doc/EngineContracts.md)。
