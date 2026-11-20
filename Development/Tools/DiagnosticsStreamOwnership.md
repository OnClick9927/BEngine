# 诊断协议与系统 TCP 流

Diagnostics 提供诊断协议、鉴权、消息编解码、采样和客户端请求串行化。
原生 TCP 连接、监听、DNS、流与句柄的释放由 Platform.DotNet 提供。
Platform.Contracts 的 IPlatformTcpStreamFactory / Listener / Connection 不包含诊断类型、
协议、包身份或采样策略。Core 不增加诊断实现。

流接口用于已经拥有 Stream 协议的宿主消费者；已有非阻塞 Socket/Poll 接口继续用于
Networking 的逐帧请求，行为不变。两种入口共享同一个平台职责边界，不将通用流编解码
或新的网络领域框架搬入 Core。连接拥有 Stream，调用者不得在连接释放后继续使用它；
监听器释放不关闭已接受的连接。取消会终止未完成的连接/接收操作。

诊断服务通过 RuntimeServices 解析流工厂，可由自定义宿主替换。
原有 BEngine.Platform.DotNet.Diagnostics 命名空间的公开入口保留用于二进制兼容，
这些类型实际属于 BEngine.Diagnostics 程序集；不代表平台依赖诊断包。
诊断模块随所属程序集退役，平台流能力保留。Web 不支持 raw TCP，继续在创建原生
资源前明确拒绝，未增加浏览器监听或 TCP 模拟。

旧协议、端口范围、非回环令牌规则、16 客户端上限、BTask 主线程采样和请求格式保持。
同时补全启动失败时的监听器清理、握手失败时的客户端对象清理，以及取消与接受完成
同时发生时的连接归还。

测试位于 Development/Tools/Tests/DiagnosticsStreamOwnership：真实 IPv4/IPv6 回环、DNS、
取消、EOF、监听器与连接独立生命周期，旧/新程序集共同验证握手、采样、堆快照、
错误协议、并发请求和端口重用。自定义工厂计数检查成功与鉴权失败均无遗留连接。
实际 Editor 的初始化和代码重载各执行协议探针；Edge 执行 WebAssembly 能力拒绝测试。
验证产物及具体结果统一在工作区 Temp/Engine/Verification/DiagnosticsStreamOwnership_20261114。

实际独立包编译还发现 Diagnostics.Editor 直接使用依赖注入 API，却未在 package.yaml
声明对应 NuGet 依赖；ClrMD 的传递依赖使独立编译选中旧版 DI。现已在包清单及
工程文件中显式声明与引擎一致的 Microsoft.Extensions.DependencyInjection，避免只在
源码解决方案的中央版本管理下能够编译。

实际 500 Sprite 场景重载还复现了堆快照超过默认 1000 次循环保护而弹出阻塞对话框。
调用栈记录确认发生在 EngineObjectHeapSnapshot.Capture；该方法已有 100000 对象、
1000000 引用、4096 集合元素和递归深度限制。现在为数组长度和已有限制申请局部
循环预算，结束后恢复调用者预算；不关闭 Editor 循环保护或扩大其他代码的权限。

本轮是职责修正，不据此声称整体游戏帧率、GC 或包体改善。
既有 ReadLineAsync 在整行读完后才检查消息大小、在途请求期间 Dispose 的并发约定，
以及 Testing 的原生进程/监听职责仍需独立审查；未将整个诊断实现标记为审查结束。

内置资源归档的另一项审查确认：当前 BuiltInResourceArchive 属于内容包的格式与策略，
包含其地址规范、AB 加密/混淆、索引和压缩契约。PlayerBuiltInResourceProvider 是该格式
到中立 Resources 接口的适配器。它们本轮继续留在 AssetBundles，避免仅因名字通用就把
内容格式和领域加密搬回 Core；通用元数据封装已由此前的 PlayerMetadataOwnership 分离。
