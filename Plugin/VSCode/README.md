# BEngine Tools for VS Code

为 BEngine 提供一键附加、真实 C# 工程同步、代码提示和源码导航。

1. 在 BEngine Package Manager 中安装 **IDE Integration**，打开工程。
2. 从 Hub 同级 `Plugin/VSCode` 目录安装 `BEngine.VSCode-0.1.0.vsix`：在 VS Code 扩展页选择 **Install from VSIX**。同时需要 Microsoft **C#** 扩展（`ms-dotnettools.csharp`）；C# Dev Kit 可选。
3. 用本机 VS Code 打开包含 `Project.yaml` 的工程文件夹。首次自动选择 `BEngine.Project.sln`。
4. 点击状态栏 **BEngine** 或执行 **BEngine: Attach to Editor / 附加到编辑器**，随后在 BEngine 中运行游戏。可对项目 Runtime / Editor 脚本及具有调试符号的包设置断点。

单个运行中的编辑器直接附加；多个目标会显示工程名、路径和 PID 供选择。附加前检查 PID、进程启动时间及可执行文件路径，忽略退出后遗留或 PID 被复用的目标。插件不会启动或关闭编辑器，也不会在附加时重新构建游戏。停止调试只解除附加。

**代码提示**由 Microsoft C# 的 Roslyn 语言服务提供，使用引擎生成的真实项目 Runtime / Editor 分离工程、引擎与包引用和编译宏。插件只设置当前工作区的 `dotnet.defaultSolution`，保留其余设置。添加代码或调整包后，执行 **BEngine: Synchronize C# Project / 同步 C# 工程**；引擎也会在相关变化后更新工程。需要本机安装与项目匹配的 .NET SDK。

## Commands

- **Attach to Editor / 附加到编辑器**：使用 Microsoft C# `coreclr` 调试器连接当前工程。
- **Synchronize C# Project / 同步 C# 工程**：请求已运行的 BEngine Editor 更新 C# 工程，可取消。
- **Open C# Solution / 打开 C# 解决方案**：选择并打开生成的解决方案。
- **Show Integration Log / 查看集成日志**：查看目标发现、同步和附加日志。

`bengine.justMyCode` 默认关闭，允许在符号可用时单步进入包。`bengine.syncTimeoutSeconds` 默认 45 秒。

本版针对本机 .NET/CoreCLR Editor，不支持通过 Remote SSH/WSL 窗口附加 Windows 编辑器，也不把浏览器 WebAssembly、IL2CPP 或移动设备伪装为 CoreCLR 目标。C# 调试器需要与编辑器相同的用户权限。运行 Release 且已去除符号的旧包，无法凭空恢复断点信息。

## Build

```powershell
./Build.ps1 -OutputDirectory E:/Project/_BZP/BEngine/Temp/Plugin/VSCode/Output `
  -ArtifactsDirectory E:/Project/_BZP/BEngine/Temp/Plugin/VSCode/Artifacts
```

构建使用固定版本的官方 `@vscode/vsce`，将工具、依赖、工作副本和日志全部放在 `ArtifactsDirectory`。不在源码目录生成 `node_modules`，不安装到用户扩展目录。统一发布由上级 `Plugin/Build.ps1` 管理。

## Local integration protocol

`Library/IDE/project.json` 和 `Library/IDE/Targets/<PID>.json` 由引擎包原子发布。同步通过 `Requests/<GUID>.json` / `Responses/<GUID>.json` 进行，不开启端口，不执行项目中的命令。协议版本为 1，文件大小限制为 64 KiB。只在可信本地工程工作区启用功能。

API references: [VS Code debugging extension API](https://code.visualstudio.com/api/references/vscode-api#debug), [Microsoft C# debugging](https://code.visualstudio.com/docs/csharp/debugging).
