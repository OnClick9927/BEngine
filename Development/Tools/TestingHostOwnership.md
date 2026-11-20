# Player 测试协议与原生宿主

Testing 负责 Edit/Play/Player 的测试发现、筛选、覆盖率、报告格式和执行策略。
Testing.Editor 的 Web 路由、报告令牌、跨域内容响应、浏览器启动参数、ADB 命令编排
仍属于测试工作流。启动输入继续使用平台中立 HostLaunchEnvironment，不给平台增加
测试结果类型、测试变量、测试包身份或测试特例。

系统进程创建、等待、取消和回收统一使用已有 IProcessRunner / ProcessUtility。
浏览器与 Android SDK 的默认安装位置由 IEditorHostTools 查找；Testing 的显式
BENGINE_TEST_BROWSER / BENGINE_TEST_ADB 覆盖值仍由 Testing 解释。
该查找不安装工具、不启动浏览器、不创建用户目录，也不执行完整 SDK/workload 探测。

IEditorHttpListenerFactory / Listener / Context 属于 Platform.Editor.Contracts。
Platform.DotNet.Editor 提供 HttpListener 实现、回环端口获取及抢占后的重试、请求与响应
流和原生连接的释放；只监听 IPv4 回环地址，不包含静态文件、报告或游戏业务。
取消一个 AcceptAsync 会结束该监听器；释放会使未完成的 AcceptAsync 结束。
Context 的 Dispose 负责完成/中止响应，支持重复调用。

Testing.Editor 的 PlayerTestHttpServer 只协调自己的测试请求生命周期：取消时停止接收、
关闭在途响应，并等待处理器结束。内容路由流式读取文件，不再为每次请求分配整个文件
的 byte[]，也不自行实现 HTTP 原始请求行/头部解析。测试协议和原有 CORS 规则保留。

旧公开 PlayerTestSession / WebPlayerTestLauncher / AndroidPlayerTestLauncher 入口不变。
原生能力由服务容器提供，独立工具需加载/初始化 DotNetEditorPlatform；自定义宿主可以
替换三个中立接口。Core 及平台源码、工程和生成程序集不引用 Testing。

Development/Tools/Tests/HostLaunchBoundary 在旧/新分发程序集执行同一套真实 Edge、跨域预检、
ADB 进程夹具和共享 Web 输入协议验证。Development/Tools/Tests/TestingHostOwnership 使用独立
消费者验证原生 HTTP 的请求/响应、取消和重新绑定，以及替代宿主、真实浏览器、
进程失败和取消后的零租约。ADB 夹具不代表物理设备运行。EditorProbe 在真实 Editor
初始化和重载后检查同一系统能力。

本机 UnityEditor.CoreModule XML 提供 BuildPipeline.BuildPlayer、BuildPlayerOptions
及 WaitForPlayerConnection；此处仅参考引擎提供运行/构建机制、测试包持有测试协议的
边界，不推断 Unity 原生测试传输的具体实现。

本轮不代表全部包语义审查完成；诊断协议的有界读行、在途释放，以及完整
Example/Snake Player 回归仍需后续审查。验证内容位于工作区
Temp/Engine/Verification/TestingHostOwnership_20261114。
