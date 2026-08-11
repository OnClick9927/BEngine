# BEngine 程序集与包边界

## 两个引擎程序集

引擎功能代码统一编译为两个程序集：

- `BEngine.dll`：全部运行时代码，供 Editor、Player 和 `GameScripts.dll` 使用。
- `BEngine.Editor.dll`：全部编辑器 API、窗口、资源工具和包编辑器代码，可引用 `BEngine.dll`，但 Player 不得引用或加载它。

`BEngine.Player` 与 `BEngine.Launcher` 是独立入口程序，不属于功能程序集。用户工程脚本仍分别生成 `GameScripts.dll` 和 `GameEditorScripts.dll`。

## 逻辑包

包仍是功能启停、命名空间、源码目录和依赖关系的边界，但不再对应独立 DLL。每份 `package.yaml` v2 都遵守：

- `runtime.assembly: BEngine`
- `editor.assembly: BEngine.Editor`
- `rootNamespace` 保持包自己的 API 命名空间，例如 `BEngine.Rendering`、`BEngine.Rendering.Editor`
- runtime 依赖只能使用 `target: runtime`
- editor 依赖可以使用 `target: runtime` 或 `target: editor`

多个逻辑包可以声明同一个聚合程序集；同一个程序集不能同时被声明为 runtime 和 editor。`com.bengine.codex` 是 editor-only 包，因此没有 runtime 节点。

当前逻辑依赖仍由 10 份 `package.yaml` 记录：Core、Serialization、Project System、Rendering、UIElements、Physics3D、Terrain、Navigation、Animation 和 Codex。包管理器依据这些依赖联动 enable 状态；动态脚本编译器把所有启用 runtime 包去重为一个 `BEngine.dll` 引用，把 editor 包去重为一个 `BEngine.Editor.dll` 引用。

## 项目结构

引擎解决方案只包含四个项目：

| 项目 | 产物 | 职责 |
| --- | --- | --- |
| `src/BEngine/BEngine.csproj` | `BEngine.dll` | 聚合全部运行时包源码 |
| `src/BEngine.Editor/BEngine.Editor.csproj` | `BEngine.Editor.dll` | 聚合全部编辑器包源码和桌面 Host |
| `src/BEngine.Player/BEngine.Player.csproj` | `BEngine.Player` | 游戏运行入口，只引用 `BEngine` |
| `src/BEngine.Launcher/BEngine.Launcher.csproj` | `BEngine.Launcher` | 工程选择、创建和编辑器启动入口 |

各包源码仍位于 `src/BEngine.<Package>` 与 `src/BEngine.<Package>.Editor`，由两个聚合项目以链接源码方式编译。这些目录不再包含 `.csproj`，因此不会生成旧的包级 DLL。

## 测试与发布

测试代码不属于引擎解决方案，位于 `E:/Project/_BZP/BEP/EngineTests`，通过独立的 `BEngine.EngineTests.sln` 引用两个聚合项目。程序集边界测试会验证引擎内只存在两个功能项目、Player 只引用 runtime、所有 package 定义指向正确的聚合程序集。

构建 `BEngine` 时，10 份内置包定义会复制到输出目录的 `Packages/<Package>/package.yaml`。用户工程的 `Packages/manifest.yaml` 只保存包 ID、版本和 enable 状态，不复制程序集或依赖定义。
