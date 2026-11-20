# IDE Integration

为 Visual Studio 和 VS Code 提供项目工程同步与本机编辑器调试目标发现。它只包含 Editor 代码，不进入游戏或增加运行时依赖。

1. 在 Package Manager 启用 **IDE Integration**。
2. 安装 Hub 同级 `Plugin` 文件夹内相应 IDE 的 `.vsix` 插件。VS Code 同时需要官方 C# 扩展；Visual Studio 使用已安装的 C# / .NET 开发工具。
3. 在 IDE 打开 BEngine 项目文件夹或项目根 `BEngine.Project.sln`，使用 **Attach to BEngine** 命令即可附加。多个匹配目标时由用户选择。
4. 在编辑器内点击 **Tools / IDE / Sync Solution** 重新同步，或 **Open Solution** 打开工程。双击 C# 脚本和 Console 源码位置时会优先按 External Script Editor 偏好打开完整工程。

工程以引擎实际 asmdef 图、引用 DLL 和编译宏生成。项目 Runtime / Editor 分开，Editor 工程引用对应 Runtime 工程；不会把 Editor 引用加入 Runtime。编辑生成的方案不会替代引擎自己的编译、加载或增量顺序。

详见 [实现和协议](Doc/Overview.md)。
