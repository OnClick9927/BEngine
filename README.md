# BEngine

BEngine 是基于 .NET 10 LTS 与 C# 14 的轻量化单线程 2D 游戏引擎。运行时世界状态使用 Q32.32 定点数，脚本 API 采用 Unity 风格，工程、场景、设置和包清单统一使用 YAML。

## 启动

前置条件：Windows 10/11 与 .NET 10 SDK。仓库根目录的 `global.json` 会统一引擎、扩展包与测试使用的 SDK feature band。

双击 `Output/BEngine.bat` 打开工程管理器。Launcher 会记住多个历史工程，并从 `Output/Packages` 展示所有可用扩展包。

命令行：

```powershell
dotnet restore src/BEngine.sln
dotnet build src/BEngine.sln -c Release
dotnet run --project src/Core/BEngine.Launcher/BEngine.Launcher.csproj -c Release
```

重新生成 `Output/BEgine` 中的 Launcher、Editor、Player 与 Core 资源：

```powershell
& src/ExportEngine.ps1 -Configuration Release
```

## 源码结构

```text
src/
  Core/
    BEngine/
    BEngine.Editor/
    BEngine.Launcher/
    BEngine.Player/
    Resources/
    EditorResources/
      Doc/index.html
      Readme.md
  Animation/
    BEngine.Animation/
    BEngine.Animation.Editor/
    Resources/
    EditorResources/
      Doc/index.html
      Readme.md
    package.yaml
  Physics2D/
  Navigation2D/
  TiledMap/
  UIElements/
```

- `BEngine.dll` 是纯运行时公共程序集，不包含 PackageManager、Codex 或具体扩展包引用。
- `BEngine.Editor.dll` 提供编辑器宿主、PackageManager、Codex 与扩展 API；它不静态引用任何可选包程序集。
- `BEngine.Launcher` 负责工程选择、创建和启动。
- `BEngine.Player` 是通用 Player 入口，按导出目录加载启用的运行时扩展。
- Animation、Physics2D、Navigation2D、PropertyAttributes、TiledMap、UIElements 是可独立启停的扩展包。

## 2D 渲染与层级

排序层值只能是 `2^1` 到 `2^63`。其中 `2^59` 到 `2^63` 固定为五个 UI 层，世界 Sprite/Particle 使用更低的层；编辑器通过 `Edit > Sorting Layers...` 编辑名称，并显示指数而不是原始 `ulong` 值。

Sprite、Tilemap、Particle 与 UIElements 进入同一渲染队列，顺序依次由 Sorting Layer、Order in Layer、Hierarchy、透明度和提交顺序决定。相邻项只有在 Material、Shader 和 Atlas 都相同时才会合批，因此 UI 与粒子可以按层级互相穿插。

Core 内置 Texture Atlas：通过 `Assets/Create/2D/Texture Atlas` 创建 `.atlas.yaml`，在 `Window/2D/Texture Atlas` 添加 PNG 并自动打包。输出采用 2 的整数次方尺寸、确定性 MaxRects 布局、Padding 和边缘 Extrude；Sprite、Particle 与 TiledMap 都可复用生成的图集和归一化 UV。

有状态对象使用 .NET 标准 `Microsoft.Extensions.DependencyInjection`。`BEngine` 只暴露 Abstractions；Editor、Launcher、Player 各自负责构建根 Provider，项目与 Scene 使用嵌套 scope。扩展包通过 `IEngineServiceModule` 注册服务，不需要 Core 维护包列表。

## 包结构

每个包包含独立的 Runtime 与 Editor 程序集；`package.yaml` 位于包根，说明文件和离线网页位于 `EditorResources`：

```text
src/Animation/
  BEngine.Animation/
  BEngine.Animation.Editor/
  EditorResources/
    Doc/index.html
    Readme.md
    Skills/bengine-animation/
      SKILL.md
      agents/openai.yaml
  Resources/
  package.yaml
```

`Resources` 存放运行时默认材质、图片和 YAML；`EditorResources` 存放组件图标、模板、说明文件、离线网页和其他编辑器专用资源。Package Manager 会从每个包的 `EditorResources/Doc/index.html` 打开文档。

程序集项目目录只允许存在 `.cs`、`.csproj` 和根程序集定义。资源、图标、YAML、模板与说明文件必须位于包级 `Resources` 或 `EditorResources`；只有 `package.yaml` 留在扩展包根。程序集通过 `Resources` / `EditorResources` API 读取，不链接、不复制也不嵌入程序集。

`src/BEngine.sln` 的包级资源节点与磁盘目录保持一致。新增或移动 `Resources`、`EditorResources` 文件后运行 `src/SyncSolutionLayout.ps1`，即可同步 VS2022 的 Solution Items；`SourceLayout` 回归会阻止未同步的提交。

Runtime 包项目引用 `BEngine`。Editor 包项目引用 `BEngine.Editor` 与自己的 Runtime 程序集。包依赖只记录其他可选包，不把核心引擎伪装成 package。

## 编辑器扩展

扩展程序集通过 `BEngine.Editor` 的公共能力注册功能：

- `[MenuItem]`
- `EditorWindow`
- `Editor` / `[CustomEditor]`
- `SerializedObject` / `SerializedProperty`
- `PropertyDrawer` / `[CustomPropertyDrawer]`
- `RuntimeLifecycle` / `[RuntimeInitializeOnLoadMethod]`
- `AssemblyReloadEvents` / `CompilationPipeline`
- `AssetPostprocessor` / `AssetModificationProcessor`
- `ObjectFactory` 与初始化、窗口、资源生命周期

所有内置编辑器窗口使用 GPU IMGUI。Inspector 的 ColorField 会显示色块和十六进制值，点击后打开 HSV、Alpha 与系统调色板。UIElements 作为可选扩展包提供。

## 工程目录

```text
MyGame/
  Project.yaml
  Assets/
    Editor/
    Scenes/
    Scripts/
  Packages/
    manifest.yaml
  ProjectSettings/
  Library/
  Logs/
  Temp/
```

PackageManager 仅存在于 `BEngine.Editor`。全局可用包只从 `Output/Packages` 发现；工程 `Packages/manifest.yaml` 记录选择状态，启用包的源码与资源复制到工程 `Packages/<package-id>`，由该工程按依赖顺序编译到 `Library/ScriptAssemblies`。禁用包会删除工程中的对应源码和编译引用，Player 仅导出该工程生成的启用 runtime 产物。新工程默认不包含任何扩展包，`BEngine.dll` 不记录具体包。

## 测试

示例工程与独立功能测试位于根目录 `Example/`，测试代码统一放在 `Example/Tests/<Feature>/`。

```powershell
Get-ChildItem Example/Tests -Filter *.csproj -Recurse |
    ForEach-Object { dotnet build $_.FullName -c Release }

dotnet run --project Example/Tests/SourceLayout/SourceLayout.csproj -c Release
```
