# BEngine IDE Plugins

独立于 `src/BEngine.sln` 的 IDE 插件工程。VS Code 与 Visual Studio 使用相同的项目发现和调试目标协议；C# 补全、跳转、诊断和断点由 IDE 原有的 Roslyn/.NET 工具提供。

```text
Plugin/
├─ VSCode/          VS Code 扩展
├─ VisualStudio/    Visual Studio VSIX
├─ Protocol.md      引擎与插件之间的本地文件协议
├─ Build.ps1        构建两种插件并导出
└─ Export.ps1       发布已构建的插件
```

## 使用

1. 安装导出目录 `Plugin/VSCode` 或 `Plugin/VisualStudio` 中的 VSIX。VS Code 还需要微软 C# 扩展；Visual Studio 需要 .NET 开发工具。
2. 在项目 Package Manager 安装 **IDE Integration** (`com.bengine.ide-integration`) 并打开项目。
3. 用引擎的 IDE 菜单同步 C# 工程并打开对应 IDE。生成的根解决方案为 `BEngine.Project.sln`，项目文件和进程注册信息在 `<工程>/Library/IDE`。
4. 在 IDE 点击 **BEngine: Attach**。只有一个匹配目标时直接附加，有多个匹配目标时先选择。附加后可在项目 C# 源码中设置断点。

VS Code 打开 BEngine 游戏工程文件夹；Visual Studio 打开生成的解决方案。此处的插件解决方案用于开发插件本身，不是游戏工程。

附加目标是本机使用 CoreCLR 的 BEngine Editor。它不是 WebAssembly、移动设备或已裁剪原生程序的远程调试器。引擎的脚本编译仍由 BEngine 管理，IDE 工程用于代码提示与导航；在 IDE 点击 Build 不替代引擎的运行/构建流程。

## 构建和导出

从 `Engine` 仓库根运行：

```powershell
./Plugin/Build.ps1
```

需要 Node.js/npm、.NET SDK，以及 Visual Studio 扩展开发构建工具。NuGet 复用系统缓存。构建依赖、输出和验证数据均放在工作区 `Temp`；源码目录不存放 `node_modules`、`bin` 或 `obj`。

默认导出到 `Engine/BEngine.Hub.exe` 同级的 `Plugin/VSCode` 和 `Plugin/VisualStudio`。可使用 `-InstallationRoot` 指定另一个 Hub 安装目录，`-NoExport` 只构建，或通过 `Export.ps1` 发布先前构建的结果。

`src/ExportEngine.ps1` 同时构建并导出这些插件；`-SkipIDEPlugins` 可用于仅构建引擎的工作流。两种插件的开发项目保持在 `Plugin`，不会加入引擎 Runtime/Editor 的六阶段编译链。
