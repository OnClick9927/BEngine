# BEngine

BEngine 是基于 .NET 10 LTS 与 C# 14 的轻量化单线程 2D 游戏引擎。运行时世界状态使用 Q32.32 定点数，脚本 API 采用 Unity 风格，工程、场景、设置和包清单统一使用 YAML。

## 启动

前置条件：Windows 10/11 与 .NET 10 SDK。仓库根目录的 `global.json` 会统一引擎、扩展包与测试使用的 SDK feature band。

双击 `Output/BEngine.bat` 打开工程管理器。Launcher 会记住多个历史工程，并从 `Output/Packages` 展示所有可用扩展包。

命令行：

```powershell
dotnet restore src/BEngine.sln
dotnet build src/BEngine.sln -c Release
dotnet run --project src/Hub/BEngine.Launcher/BEngine.Launcher.csproj -c Release
```

重新生成 `Output/BEgine` 中的 Launcher、Editor、Player 与 Core 资源：

```powershell
& src/ExportEngine.ps1 -Configuration Release
```

## 源码结构

```text
src/
  Hub/
    BEngine.Launcher/
  Core/
    BEngine/
    BEngine.Editor/
    BEngine.Player/
    Resources/
    Editor/
      Doc/index.html
      Readme.md
  Packages/
    Animation/
      BEngine.Animation/
      BEngine.Animation.Editor/
      Resources/
      Editor/
        Doc/index.html
        Readme.md
      package.yaml
    Physics2D/
    Navigation2D/
    PropertyAttributes/
    TiledMap/
    UIElements/
```

- `BEngine.dll` 是纯运行时公共程序集，不包含 PackageManager、Codex 或具体扩展包引用。
- `BEngine.Editor.dll` 提供编辑器宿主、PackageManager、Codex 与扩展 API；它不静态引用任何可选包程序集。
- `Hub/BEngine.Launcher` 负责工程选择、创建和启动，仅通过工程引用使用 Core 程序集；Core 不反向依赖 Hub。
- `BEngine.Player` 是通用 Player 入口，按导出目录加载启用的运行时扩展。
- Animation、Physics2D、Navigation2D、PropertyAttributes、TiledMap、UIElements 是可独立启停的扩展包。

## 热更新与 Player 构建

Player 启动顺序固定为：初始化宿主 Core，播放可定制闪屏并进入 AOT Scene；使用者点击检查后，
若发现更新必须再次确认，随后才下载并激活 AssetBundle。通过沙盒完整性门禁后，Player 读取代码发布清单，
由目标平台选择托管代码后端并创建 HotDomain，最后启动场景。项目 C# 程序集、启用包的
Runtime 程序集、PDB 与 `Assets/__BEngine/HotUpdate/release.json` 都进入保留的
`bengine-hotupdate` AssetBundle；包 Runtime Resources 进入 `bengine-packages` AssetBundle。
内容版本同时覆盖代码与资源哈希，发布目录不保留这些松散 DLL、PDB 或包资源副本。

编辑器 PlayMode 使用 AssetDatabase 和同一份运行时发布图构造不可变虚拟 AssetBundle；
它不访问远端、不执行下载，但包代码、包资源、资源地址、代码发布清单和引用计数接口与
发布版一致。Inspector 仍操作 PlayMode 的 BObject 镜像，因此现有运行时查看和修改能力不变。

`File > Build Settings...` 使用与 Unity 相同的入口组织 Windows、Linux、macOS、Android、
iOS 与 Web 构建。当前分阶段 Player 的 `Scenes In Build` 必须且只能包含 `Assets/Aot/AOT.scene.yaml`；
游戏场景全部由 `Build Hot Resources` 发布。窗口同时提供 Development Build、Debug Symbols、
Self Contained、Build 与 Build And Run。构建期间显示可取消的阶段进度条，设置持久化到
`ProjectSettings/BuildSettings.yaml`；再次构建到同名目录时由编辑器确认，并在新包完成后
原子替换旧包。工程目录内部、工程目录本身及其父目录始终禁止作为替换目标。

`Hot Resource Version` 必须是规范的小写 `vN`（`N` 为无前导零的正整数），例如 `v1`、
`v2`、`v10`；`V1`、`v01`、`v1.0` 均无效。每个版本目录不可变，只有数值更高的版本会推进
`latest.json`。重新发布完全相同的旧版本只校验并保留该目录，不会把 `latest` 回退；最终发布由
跨进程锁串行化，并以同目录临时文件原子替换 `latest.json`。
`latest.json` 只是版本指针；客户端先读取它，再读取对应的 `vN/version.json` 和 catalog。运维切换
已经发布的版本时只需修改其中唯一的 `version` 值，例如将下面的 `v2` 改成 `v1`，不需要重新构建、
移动或覆盖任何版本目录。客户端以远端指针为准，因此可从 V2 切回缓存中的 V1，也可以选择暂不切换并继续运行当前有效版本。

```json
{"version":"v2"}
```

主包只从 AOT Scene 递归收集其位于 `Assets/Aot` 下的依赖，连 V1 游戏资源也不会内置。远端资源从游戏场景
递归收集 GUID/路径引用的 Prefab、Material、Texture、TextureAtlas、UI 与其他资产；`Resources` 和
`StreamingAssets` 按运行时动态寻址规则保留。包 Runtime Resources 和所有热更新 C# 程序集分别进入远端资源 AB
与 `bengine-hotupdate` 代码 AB。构建过程重新生成最小运行时描述，不复制工程的 Assets、源码、
Editor、Library、Packages 源文件、Logs 或 Temp。Player 的 BAsset、Sprite、材质、Prefab 和图集
引用统一经当前 AB Provider 恢复，不要求发布目录保留原始工程文件。

Windows 成品采用单文件 apphost；Player 与远端内容是 `Build` 下的两个同级发布物：

```text
Build/
  <project>/
    <project>.exe
    <project>_data/
      assembly/
      resources/
        player.bresources
        aot.bresources
    sandbox/             # 首次运行时创建，可在 Player Settings 修改
  hotres/
    <package>/
      latest.json
      v1/
      v2/
```

除约定的外层 `Build` 根目录外，构建器会递归校验所有成品文件和目录名均为小写。
`player.bresources` 保存启动元数据、平台清单、闪屏和 Player 全生命周期使用的 Core 图标与 Shader；
`aot.bresources` 只保存 AOT Scene、AOT 程序集及其依赖，并在进入游戏阶段后释放。两个归档都对索引和
payload 做确定性混淆，并分别执行 SHA-256 完整性校验；这用于避免资源以明文散落或直接出现在归档中，
不等同于持有外部密钥的加密。新包的 `_data` 只包含 `assembly` 和 `resources`，不再生成 `res`、
松散 `runtime.bmeta`、JSON、PNG 或 Shader 源文件，也不含 AB。宿主优先按 exe 名寻找同名 `_data`，exe 被改名时仅在
根目录存在唯一合法 `_data` 的情况下回退发现，因此不再需要根目录 `*.player` 文件。
`assembly` 只保存显式要求的调试符号等程序集侧产物。根目录不会散落 `.dll`、`.deps.json`、
`.runtimeconfig.json`、Editor 或 Launcher 文件。主流水线生成内容后再由平台 provider 封装宿主，
因此 APK/App Bundle/Web 文件系统也不能绕过相同的内容激活契约。

| 目标 | 内置构建边界 | 托管代码边界 | 渲染边界 |
| --- | --- | --- | --- |
| Windows/Linux/macOS | .NET 10 SDK；支持 x64/arm64，导出引擎可从 `BuildHosts` 独立构建 | 可回收 CoreCLR HotDomain | Vulkan/OpenGL（按目标自动选择） |
| Android arm64 | 需要 `android` workload、Android SDK、JDK，以及明确声明混合 AOT/解释能力的自定义 Host | 标准 `AndroidUseInterpreter` 会关闭正常 JIT，不能冒充 Core AOT；因此默认阻断，必须接入经验证的混合运行时 | 必须注册 Vulkan/OpenGL ES Host renderer |
| iOS arm64 | 必须在 macOS 上安装 `ios` workload 与 Xcode | Core AOT；仅 Host bridge 与下载的 C# IL 使用 Mono interpreter；禁止 NativeAOT | 必须注册 Metal Host renderer |
| Web WASM | 需要 `wasm-tools`；Release 会 AOT Host/Core，构建时间明显更长 | Host/Core WASM AOT，下载的 C# DLL 由 Mono interpreter 执行；禁止 NativeAOT | 必须注册 WebGPU/WebGL Host renderer |

Android/iOS/Web 的 SDK 生命周期模板会随引擎导出，但逻辑宿主不被当作完整 Player。
只有 workload、平台 SDK、混合运行时和真实 renderer 全部满足时 `CanBuild` 才返回 `true`；
`PlayerBuildPipeline.GetPrerequisites(targetId)` 返回逐项诊断和安装方式。自定义 Host 通过
`PlatformPlayerBuildCapabilities.RegisterRenderer` 声明工程路径、后端和解释器能力，也可以
用 `PlayerBuildPipeline.RegisterProvider` 整体替换平台 provider。

托管代码后端通过 `IManagedCodeRuntimeProvider` 扩展。桌面使用可回收 CoreCLR
`AssemblyLoadContext`；Mono/iOS/WebAssembly Host 使用非 NativeAOT 的官方解释器加载代码 AB。
`<project>.exe --validate` 可通过 `_data/resources/player.bresources` 中的启动元数据在 CI 中无窗口执行 AB 激活、HotDomain、
场景反序列化及一次 Start/Tick/Stop；仍可额外传入运行时数据路径用于诊断。导出流程会携带
`BuildHosts` 与只包含 Player/Core 依赖的独立
`BuildRuntime`，并排除 Host 的 `bin`、`obj` 与临时文件；平台构建不会引用 Editor/Launcher。

## 2D 渲染与层级

排序层值只能是 `2^1` 到 `2^63`。其中 `2^59` 到 `2^63` 固定为五个 UI 层，世界 Sprite/Particle 使用更低的层；编辑器通过 `Edit > Sorting Layers...` 编辑名称，并显示指数而不是原始 `ulong` 值。

Sprite、Tilemap、Particle 与 UIElements 进入同一渲染队列，顺序依次由 Sorting Layer、Order in Layer、Hierarchy、透明度和提交顺序决定。相邻项只有在 Material、Shader 和 Atlas 都相同时才会合批，因此 UI 与粒子可以按层级互相穿插。

Core 内置 Texture Atlas：通过 `Assets/Create/2D/Texture Atlas` 创建 `.atlas.yaml`，在 `Window/2D/Texture Atlas` 添加 PNG 并自动打包。输出采用 2 的整数次方尺寸、确定性 MaxRects 布局、Padding 和边缘 Extrude；Sprite、Particle 与 TiledMap 都可复用生成的图集和归一化 UV。

有状态对象使用 .NET 标准 `Microsoft.Extensions.DependencyInjection`。`BEngine` 只暴露 Abstractions；Editor、Launcher、Player 各自负责构建根 Provider，项目与 Scene 使用嵌套 scope。扩展包通过 `IEngineServiceModule` 注册服务，不需要 Core 维护包列表。

## 包结构

每个包包含独立的 Runtime 与 Editor 程序集；`package.yaml` 位于包根，说明文件和离线网页位于 `Editor`：

```text
src/Packages/Animation/
  BEngine.Animation/
  BEngine.Animation.Editor/
  Editor/
    Doc/index.html
    Readme.md
    Skills/bengine-animation/
      SKILL.md
      agents/openai.yaml
  Resources/
  package.yaml
```

`Resources` 存放运行时默认材质、图片和 YAML；`Editor` 存放组件图标、模板、说明文件、离线网页和其他编辑器专用资源。Package Manager 会从每个包的 `Editor/Doc/index.html` 打开文档。

程序集项目目录只允许存在 `.cs`、`.csproj` 和根程序集定义。资源、图标、YAML、模板与说明文件必须位于包级 `Resources` 或 `Editor`；只有 `package.yaml` 留在扩展包根。程序集通过 `Resources` / `EditorResource` API 读取，不链接、不复制也不嵌入程序集。

`src/BEngine.sln` 的包级资源节点与磁盘目录保持一致。新增或移动 `Resources`、`Editor` 文件后运行 `src/SyncSolutionLayout.ps1`，即可同步 VS2022 的 Solution Items；`SourceLayout` 回归会阻止未同步的提交。

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
