# Core 精简与功能拆包迁移记录

验证日期：2026-11-05。验证工程、日志、构建产物及迁移前源码快照全部位于仓库外层 `Temp/Engine/Verification/CoreSplit20261105`；Snake 的独立验证位于 `Temp/Snake/Verification/CoreSplit20261105`。

## 职责与依赖

Core Runtime 保留对象、组件、Transform、场景和生命周期、定点数、BTask、调度、输入、通用资源身份与序列化、克隆扩展、服务注册、轻量 Instrumentation、闪屏状态以及窗口、帧和 RHI 契约。Core Editor 保留工程、资产数据库、导入和编译机制、包管理、SerializedObject、Undo、Selection、扩展注册、构建管线和统一 Shader 变体管理。

具体实现的归属：

```text
Graphics Runtime          → Core Runtime
Graphics Editor           → Core Editor + Graphics Runtime
Rendering2D Runtime       → Graphics Runtime
IMGUI Editor              → Graphics Editor
Rendering2D Editor        → Rendering2D Runtime + IMGUI Editor + Graphics Editor
EditorWorkbench Editor    → IMGUI Editor + Rendering2D Editor（required）
Diagnostics Runtime       → Core Runtime（可选）
Diagnostics Editor        → Diagnostics Runtime + EditorWorkbench Editor（可选）
```

箭头右侧为依赖。实际编译仍严格按 Core RT → Core Editor → 包 RT → 包 Editor → 项目 RT → 项目 Editor，阶段内部按依赖排序。Graphics Editor 不依赖 IMGUI，UIElements Runtime 只需要 Graphics，不依赖 Rendering2D。

- Graphics：Texture、Font、Material、Shader、RenderTexture、图片编解码、纹理处理、通用 GPU 绘制数据、闪屏绘制、图形导入器及压缩工具。
- Rendering2D：Camera2D、Sprite、SpriteRenderer、遮罩、场景渲染、批处理、裁剪、排序、Gizmos、对应 Inspector 和场景工具。
- IMGUI：GUI、GUILayout、EditorGUI、EditorGUILayout、GUIContent、样式和布局、EditorWindow、Inspector 基类、弹窗、输入和 GUI 帧呈现。
- EditorWorkbench：默认编辑器宿主、停靠、默认菜单、Hierarchy、Project、Inspector、Scene、Game、Console、Package Manager、Settings、Preferences 和工作台资源。
- Diagnostics：重型采集、内存分析、远程诊断及 Profiler、Frame Debugger 等窗口。Microsoft.Diagnostics.Runtime 随此包的 Editor 部分提供。

Application 保留游戏框架、AOT/热更新、内容部署、Player 构建及 Play 工作流，不再提供默认工作台，也不再替代 Diagnostics 包身份。EditorWorkbench 的 required 标记位于 package.yaml，其编辑器依赖自动展开，包管理界面保持可用。Editor 程序集和 Editor 资源不进入 Player。

Platform 保留 OS、原生窗口、GPU 后端和平台发布适配。通用编辑器启动器根据 tooling 生成的 editor.application.json 调用 IEditorApplicationEntry。Core 与 Platform 不引用具体包标识或包程序集。

## 扩展接口与 API 迁移

- 资源文件解码使用 RuntimeAssetCodecRegistry.RegisterFileLoader / Register；引用读写及特殊对象克隆使用 RuntimeObjectCodec / RuntimeObjectCodecs。Core 不再判断 Sprite、Material 或 Shader 类型。
- 图片能力通过 IEncodedImageCodec / EncodedImages 注册。无图形包的 Core 可以正常加载通用文件资源、运行场景与 BTask。
- 场景绘制由 ISceneRendererFactory / ISceneRenderer 提供；平台窗口不构造具体场景渲染器。
- 闪屏继续由 HostStartupSession 等 Core 状态机制协调，IHostStartupRenderer 提供应用内呈现。保持 0.5 秒 Logo 动画、进度完成后切换、Logo 与进度条布局及自定义角标规则。
- 编辑器原生窗口通过 IEditorFramePresenter 绘制。IEditorView、IEditorMenu 和 IEditorHost 保持中立；EditorWindow、GenericMenu 等实现移入 IMGUI。
- EditorAssetAdapters、AssetPreviewProviders、EditorPresentationRequests、EditorPreferenceAccess、EditorWorkflow 等注册点连接包实现与 Core 机制。
- Application 通过 EditorWorkflow 注册 AOT 资产保护、脚本定义持久化、Play 内容与代码会话，以及自己的构建命令。没有该工作流时，工作台可以使用 Core 场景流程。
- Texture.CreateSprite 改为 Rendering2D 提供的扩展方法。RenderCamera.FromCamera2D 改为 RenderCameraFactory.From。排序层通用定义改为 LayerRegistry / LayerDefinition。
- 原轻量 BEngine.Diagnostics 命名空间改为 BEngine.Instrumentation；重型诊断实现迁入独立包。
- 新程序集保留多数现有 BEngine / BEngine.Editor 类型全名。重新编译引用方，并在 asmdef / package.yaml 声明实际所属包，不添加 Core 指向包的类型转发。

## 数据与代码重载

RuntimeTypeCache 按活动域的类型全名解析原程序集限定名称，支持嵌套泛型和数组；RuntimeTypeAliasAttribute / RuntimeAssemblyAliasAttribute 允许目标包注册旧标识。诊断窗口的旧 BEngine.App.Editor 名称由 Diagnostics 包声明映射。

场景、Prefab、资源 GUID、Sprite 子资源 local ID 及项目设置格式保持不变。Sprite 编解码与缓存由 Rendering2D 提供，内置 Material / Shader 的持久化和 Material 克隆由 Graphics 提供。

包编译指纹版本升级为 v14，使程序集归属变化后的旧缓存失效。项目编译仍按实际输入和引用指纹复用缓存。Play 域优先共享 HostAssemblyRegistry 已注册的静态宿主模块，避免宿主与游戏域同时产生不同身份的 Texture、Material 等类型；项目程序集继续独立重载。静态宿主模块代码变更需要重启 Editor。

模块初始化在首次绘制前完成；RuntimeAssemblyCleanup 及编辑器程序集清理撤销所属模块的编解码器、类型映射、GUI/诊断钩子、资源提供器和缓存。帧绘制使用已解析接口，不逐帧扫描包或程序集。

## 资源与导出

图标、Shader、字体和纹理压缩工具由实际所属包提供。RuntimeBuildResources 按运行时程序集声明启动所需资源，Player 构建按资源所有者汇总，排除这些目录在 AOT/热更新包中的重复内容。Graphics 与 Rendering2D 共用的 Textured Shader 仅保留在 Graphics 的 Shaders/Common。

默认闪屏 PNG / 角标从 Graphics 包解析，不再依赖旧 Core/Editor/Icons 路径。平台发布收到规范化的工程根目录，传入 Project.yaml 的命令行调用也可正确生成 Temp、编译契约及构建产物。

导出脚本支持 OutputRoot，便于在 Temp 内验证完整安装。正式导出前检查正在使用目标安装目录的进程；发布中间产物与普通源码编译分开，包输出遵循依赖顺序，避免引用项目重复导出造成文件竞争。

## Player 链接与异步加载回归修复

共享的静态宿主程序集在动态脚本的局部链接阶段保持原始镜像，只由最终平台构建链接一次。否则动态插件按被局部链接器删去方法的接口编译，最终宿主保留完整接口，可能出现 TextureAtlas 的 TypeLoadException。动态脚本仍正常裁剪，静态宿主依赖闭包（包括图片解码器）不再同时被打进热更新程序集资源。

Graphics 注册通用 Texture 解码器后，AB 的异步加载必须继续走带有 Catalog 信息的纹理专用加载器。已修复原先优先使用通用解码器而遗漏 assetPath、GUID、子资源身份和导入设置的问题；新增异步与同步一致性、CreateSprite 来源及 payload lease 释放回归。该问题会导致 Snake 的独立纹理在关卡 GPU 预热时找不到来源。

## 验证记录

- 无界面 Core：15 项通过，覆盖生命周期、固定调度、BTask、场景/对象图、通用资源引用、克隆重映射、旧类型及泛型、卸载清理；没有加载图形、UI、诊断或窗口/GPU 模块。
- 数据回归：21 项通过，覆盖纹理解码、Sprite GUID/local ID/身份缓存、内置材质与 Shader、克隆、场景资源引用、旧程序集类型名、绘制组件快照失效、AB 异步纹理与同步结果一致。
- 编辑器机制：17 项通过，覆盖包依赖图、必需/可选身份、Selection、SerializedObject、Undo/Redo、停靠布局保存恢复及最大化、旧窗口名、包模板依赖、GUI 与诊断注册。
- Example 真实编辑器自动回归：虚拟 AB 与真实 AB 各完成 2 次 Play 会话、6 轮、300 次场景验证、1776 次交互断言，均为 0 错误；共 4 次 Play、600 次场景验证、3552 次交互断言。
- Example Windows 与 Edge Web Player：分别通过 50 个示例场景及 296 次交互断言，0 错误，包含 Prefab、序列化、动画、UI、粒子、灯光等实际功能。
- Snake Windows 与 Edge Web Player：分别完成大厅及 20 个关卡的进入/返回流程，0 错误；本机测试最大关卡加载耗时分别为 2025.827 ms 与 4237.300 ms。Web 使用 1280×720 的 Edge 无界面实例，共呈现 4959 帧；这些数据用于功能回归，不作为玩家机器的帧率保证。
- Windows / Web 的 Snake StreamingAssets 各 331 个发布文件与内容版本逐一核对 SHA-256。验证使用隔离存档；原工程业务脚本未修改。
- 增量编译：初次 RT/Editor 各编译一次；输入未变均跳过；仅 Editor 改动为 RT 0 / Editor 1；RT 改动为 RT 1 / 依赖 Editor 1，四项通过。
- 静态宿主依赖闭包检查通过，保留图形解码依赖，未错误纳入独立 Animation 包。
- 全量解决方案 95 个项目及 69 个包项目源码编译通过。源码及生成程序集的边界检查覆盖 939 个源文件、74 个包程序集身份、21 个 Core/Platform 程序集身份，0 违规。
- Windows、Linux、macOS、Android、iOS、Web 的托管发布适配与分发运行时已编译；本机 Android 原生工程编译通过，0 警告、0 错误。Windows 主机不具备 Apple 原生工具链，未完成 iOS/macOS 原生运行验证。

主要证据位于 `Temp/Engine/Verification/CoreSplit20261105` 的 Headless、DataTests、EditorContracts、Incremental、HostDependencies、Example.Result.json、Example.Real.Result.json、WebBrowser-Example-03；Snake 的汇总为 `Temp/Snake/Verification/CoreSplit20261105/PlayerRegression.json`。Windows/Web 构建、浏览器、内容发布及导出日志均保存在对应验证目录。

本次 Windows 桌面自动化原生管道不可用，未完成鼠标拖放及窗口截图视觉验收。停靠和交互机制已通过代码回归，实际 Editor Play 已运行。Edge 的 Web 验证可用并已完成。没有将这些结果表述为完整桌面视觉验收。

## 性能和体积对照

基线为本轮修改前保存的源码快照，基线与修改后使用同一 .NET 10 Release 配置；每个进程 500 个 MonoBehaviour，20000 次预热后测量 200000 次逻辑 Tick，前后交替启动独立进程，各 7 次取中位数。该基准不加载窗口、GPU 或图形包，启动耗时仅指无界面逻辑初始化，不代表完整编辑器或 Web 下载启动耗时。

默认 JIT 配置结果：

| 指标 | 拆分前 | 拆分后 |
| --- | ---: | ---: |
| Core 程序集 | 1,186,304 B | 890,880 B（减少 24.9%） |
| Core Editor 程序集 | 2,297,344 B | 1,069,056 B（减少 53.5%） |
| DotNet 平台程序集 | 85,504 B | 68,096 B |
| 逻辑初始化 | 61.820 ms | 56.823 ms |
| 单帧逻辑耗时 | 0.011829 ms | 0.013392 ms |
| 每帧分配 | 0 B | 0 B |
| 回收后托管存活内存 | 1,557,336 B | 1,399,024 B（减少 10.2%） |
| 工作集 | 46,399,488 B | 48,840,704 B |
| 私有内存 | 24,686,592 B | 28,274,688 B |
| 测量区间进程 CPU 时间 | 515.625 ms | 609.375 ms |

因默认 JIT 下存在帧耗时上升，另以相同程序、同样 7 次交替测量，双方均设置 `DOTNET_TieredCompilation=0`：初始化 97.009 → 88.507 ms；单帧 0.023674 → 0.024004 ms（增加约 1.4%）；每帧分配仍为 0；工作集 37,695,488 → 36,311,040 B；私有内存 15,364,096 → 14,897,152 B；测量区间进程 CPU 时间 2343.750 → 2468.750 ms。保留两套原始结果，不据此宣称 CPU 或帧耗时已经优化。

本轮实现的可确认收益是 Core 依赖减少、Core 程序集变小及无图形宿主的托管存活内存下降；模块接口不会每帧扫描程序集或创建新集合。默认完整安装仍包含原有功能、平台运行库及构建支持，功能迁入包不等于从默认安装删除。导出前正式安装与隔离安装的 BEgine + Packages 总量分别为 3,786,183,354 B 与 3,786,083,991 B，整体基本持平；两处安装库存可能存在历史文件差异，此总量只作为分发库存记录，不代替干净基线构建的包体测试。

原始测量分别为 Benchmark/ComparisonFinal.json、Benchmark/ComparisonNoTiering.json、InstallationSizes.json。未获得相同工程配置下完整 Editor/Player 的拆分前启动与渲染性能基线，不将本轮功能回归中的启动/关卡耗时作为前后提速结论。

## 正式导出

正式引擎、Hub 及 IDE 插件已通过 `Development/Scripts/ExportEngine.ps1 -Configuration Release -IncrementalPackages` 更新到 `Engine/Engine`。导出进程退出码为 0，包含 BENGINE_PLUGIN_EXPORTED / BENGINE_EXPORT_OK 标记；对正式安装重新执行边界检查，仍为 0 违规。汇总见本轮验证目录的 Result-Final.json，正式导出日志为 Export-Formal.log，最终边界结果为 Boundaries-Formal.json。
