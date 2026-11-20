# IDE Integration

## 工程与代码提示

Core 的 `ProjectCodeModel.Capture(ProjectWorkspace)` 提供真实编译器使用的 asmdef 源码分组、Runtime / Editor 目标框架、项目依赖、启用包引用和编译宏。抓取不触发编译、不加载新程序集、不修改包清单。

此包生成项目根 `BEngine.Project.sln` 和 `<项目>/Library/IDE/*.csproj`。源码通过绝对路径与 Link 纳入项目；项目之间使用 ProjectReference；启用的引擎与扩展包使用实际 DLL HintPath。若 DLL 同级存在 XML API 文档，C# 语言服务按标准引用约定自动读取。代码提示由 IDE 自身的 C# 语言服务提供，不重复实现或捆绑一套 C# 编译器。尚未打开、编译过的项目需先在 BEngine 中打开，确保相应包引用已经存在。

这些工程是代码编辑视图。其 Build 输出仅在 `Library/IDE/Build`，不会覆盖 `Library/ScriptAssemblies` 或改变六阶段编译与增量缓存。已有同名、非本包生成的 `BEngine.Project.sln` 不会被覆盖。未变更的源码工程保留时间戳，减少语言服务的无效重载。

## 附加与生命周期

初始化和脚本重载后，Editor 自动同步，并将当前真实进程注册到 `Library/IDE/Targets/<PID>.json`。退出、重载或卸载此包时删除当前注册。不存在心跳线程、网络监听端口和用户目录下的全局目标索引。崩溃可能留下文件，IDE 必须核对 PID、实际进程启动时间及工程路径，不得只凭进程名附加。

正常帧仅执行无分配的时间判断，最多每 0.5 秒轮询一次本项目请求目录；没有请求时不生成工程。编译完成或相关设置变化后合并同步。每次轮询最多处理四条请求。

`Library/IDE/project.json`：

```json
{"schemaVersion":1,"projectPath":"E:/Game","projectName":"Game","solutionPath":"E:/Game/BEngine.Project.sln","engineVersion":"0.1.0","enginePath":"E:/Hub/BEgine/BEgine-0.1.0/"}
```

目标记录含 `schemaVersion`、`processId`、`processStartTimeUtc`（ISO UTC）、`projectPath`、`projectName`、`kind`（`editor`）、`displayName`、`executablePath`、`solutionPath`、`runtime`（`coreclr`）。所有写入先完成同目录临时文件，再原子重命名。

## 同步请求

IDE 将请求原子写入 `Library/IDE/Requests/<requestId>.json`。requestId 只能是 N 或 D 格式的 GUID，必须与文件名一致；单条上限 64 KiB，不接受任意文件路径或其他命令：

```json
{"schemaVersion":1,"requestId":"30f040550d9c4b5d8a1b5ac496718812","command":"sync"}
```

Editor 在 `Responses/<requestId>.json` 中返回 `schemaVersion`、`requestId`、`success`、`solutionPath` 和 `error`，再删除已处理请求。IDE 客户端消费后删除响应。断开、超时和异常都应由客户端清楚显示，而不是假装已成功附加。

## 调试符号

编辑器项目编译使用 Debug、`Optimize=false` 和 portable PDB；程序集加载保留符号。Core 将循环保护改写的编译快照映射回原始源码路径、行号和 SHA-256 文档校验和。编译快照的稳定虚拟路径与原文档分离，既保留断点源码一致性检查，也保留 LoopGuard 和内容缓存。此设置不改变正式 Player Release 的裁剪或优化策略。

此版本附加本机 .NET 编辑器。WebAssembly、移动端和经过 AOT / 裁剪的发布包有独立平台调试机制，不冒充为本机 CoreCLR 附加目标。
