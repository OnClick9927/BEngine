# 接口与实现的职责边界

Application 是基于引擎及其他包的游戏开发框架，不是引擎宿主或打包器。它的运行时命名空间保持 `BEngine.App`，包含 Framework；编辑器部分仅负责游戏模板与业务开发约定。

```text
Core
├─ BEngine：基础结构、生命周期、调度、依赖注入及扩展注册
├─ BEngine.Editor：工程、编译、包管理及编辑器扩展机制
├─ BEngine.Player：Player 宿主契约、通用场景执行/切换、托管代码装载协议、部署格式及启动协调
└─ BEngine.Player.Editor：脚本编译、裁剪、构建事务、设置与内容构建扩展接口

Platform
├─ 操作系统、窗口、GPU、托管代码执行域及各平台构建实现
└─ Player/Hosts、RuntimeDistribution、ExportPlayer.ps1：宿主模板与运行时发布组合

Packages
├─ AssetBundles：AB 读写、内容构建和发布、内容会话及 Player 内容接入
├─ Networking：网络请求；由内容加载实现按需使用
├─ EditorWorkbench：默认工作台、打包窗口、Player Settings 与 Play Mode 设置界面
├─ Graphics / Rendering2D / IMGUI / UIElements：所属绘制与界面实现
├─ Diagnostics：性能分析与诊断工具
└─ Application：Launcher、GameDomain、可扩展 Services 与业务开发模板
```

## 接入方式

- 构建文本格式通过 Core Editor 的 `PlayerResourceTextFormats` 按程序集注册，Graphics/UIElements 持有其后缀和类型语义；原生字体绑定、后端符号与存活契约校验由 Platform Build 的 `PlatformTextBuildPolicy` 提供。Core 只组合引用分析与构建阶段，详见 `Development/Tools/BuildInputPolicyBoundary.md`。

Core 与 Platform 不引用具体包程序集、包类型或包身份。引擎定义并实际调用中立扩展协议；具体能力由所属包注册。包之间的依赖由清单明确声明，不通过 Application 作为传递能力入口。

- `PlayerResourceReferenceSet` 与 `PlayerResourceBuildPipeline` 属于 Core Player Editor，提供构建期资源引用、闭包和宿主资源选择；内容提供者持有分区、别名、保存范围及归档。Graphics Editor 通过 `IShaderResourceBuildPolicy` 注册图形后端文件规则及启动资源根，AB 不再硬编码这些规则。注册和卸载复用已有贡献者机制。接入说明见 `Development/Tools/ResourceReachabilityBoundary.md`。

- `IPlayerHost` / `IPlayerRuntimeSession` 由 Core Player 定义；内容包注册实际宿主，平台模板只访问契约。
- `PlayerRuntimeSession` / `PlayerWindowApplication` / `PlayerSceneStage` 由 Core Player 实现场景执行、窗口接入与所有权清理；`IPlayerRuntimeContent` 与 `IPlayerStageTransitionFlow` 将内容准备及策略接入该执行机制。AB 不再各自实现桌面和嵌入式会话。公开接入与释放约定见 `Development/Tools/PlayerContentExecution.md`。
- `IPlayerBuildContentProvider` 由 Core Player Editor 定义；AssetBundles 提供内容准备、内容发布及平台内容适配。
- Core Player Editor 负责 StreamingAssets 原始文件、目标和部署清单、启动图像调度及通用输出结构校验。Graphics 通过 `IPlayerStartupImageBuilder` 提供图像校验和合成；AssetBundles 在 `FinalizeAsync` 封装自己的元数据和资源归档，不接管通用部署。公开路径与阶段约定见 `Development/Tools/PlayerStagingBoundary.md`。
- `ResourcePackagingRequirements` 是 Core Editor 的不可变内容打包约束；内容提供者声明自己的格式需求，各平台负责 SDK 参数转换，不在平台中硬编码包格式。
- `RuntimeResourceMetadata` / `RuntimeResourceCodec` 由 Core 提供资源字节解码契约，格式所属包注册对象创建和子资源策略。AB 仅适配目录、元数据、字节与租约，不引用 Graphics / Rendering2D Runtime。迁移约定见 `Development/Tools/ResourceCodecBoundary.md`。
- `PlayerBuildValidation` 支持所属包贡献资源校验，UIElements 注册字体能力校验。
- `HostAssemblyRegistry.RegisterAnchor` 由能力所属程序集贡献公开锚点，裁剪器不维护具体包的类型名单。
- `tooling.json` 的 runtimeHost / webNativeHost 元数据决定发行组合，生成 `player.modules.json` / `player.native.json`；Core 的发行工程不写死包清单。
- 扩展注册在程序集卸载时释放，默认运行时宿主不接管可卸载的编辑器副本。

## 内容与程序集

`BEngine.Player.HotUpdate` 持有托管代码发布清单、执行域和激活协议；具体执行器由 Platform 实现。AssetBundles 持有资源内容清单、内容会话、AB 构建与真实/虚拟内容加载，并通过引擎扩展点组合代码和资源。Application 可以组合这些公开能力，但不再实现它们。

部署通用结构 `HostDeploymentManifest` 及序列化器位于 Core Player，命名空间保持 `BEngine.Hosting`。具体 AB 元数据和部署工作区适配位于 AssetBundles；读取入口为 `BEngine.Player.ProjectSystem.PlayerWorkspace.OpenRuntime`。普通工程继续使用 Core 的 `ProjectWorkspace.OpenRuntime`。

`AssetBundleBuildContent` / `AssetBundleBuildContentBundle` 是 AB 构建输入。AB 构建器接收地址、资源、导入器标识和文件/内存数据；托管代码内容收集通过所属内容适配实现，不进入 Application Framework。

## 编辑器与业务扩展

包管理、编译和中立编辑器生命周期由 Core Editor 提供；IMGUI 与 EditorWorkbench 持有具体窗口和呈现。Application 只注册游戏模板约定，不提供编辑器宿主、平台构建器、AB 构建器、部署序列化或默认打包窗口。

共享资产定位 `ProjectWindowUtil` 和工程设置状态 `EditorProjectSettings` 由 EditorServices 提供，无需图形或 IMGUI。`SceneView` 是 Rendering2D Editor 的静态工具入口，不是默认窗口；它与工具上下文、拾取、Gizmo 和视觉手柄策略一起供任意场景编辑宿主使用。宿主通过 `DrawTools` / `CancelToolInteraction` / `ScenePickingUtility.PickAll` 接入现有行为。EditorWorkbench 只组合默认窗口，不成为这些能力的反向依赖。新入口不授予额外友元权限。

消费包控制其他包的组件时，应在消费包内实现适配器并调用公开 API。仅为某个消费方服务的接口和状态不放入 Core。Timeline 的媒体适配、UIElements 的控件模型、Node Editor / Track Editor 的编辑控件都由所属功能包维护。

## API 与数据迁移

Inspector 预览契约、图像值和提供方缓存由 IMGUI Editor 持有；Core Editor 的包管理器只触发中立生命周期/程序集清理。目标包声明旧类型标识映射，仓库调用方重编译。GUISkin 通过中立引用绑定接入资源，PropertyDrawer 通过已有 Core 构造工厂创建，Inspector 销毁回到 Core 的 BAsset 生命周期。说明见 `Development/Tools/EditorPreviewBoundary.md`。

通用绘制命令及 `IRenderContributor` / `IDrawContributor` 属于 Graphics；具体 `RenderCamera` / `ISceneRenderContributor2D` / 场景几何与批次组织属于 Rendering2D。UIElements 使用通用绘制接口，不依赖 2D 相机。`RenderContributorRegistry` 持有唯一的设备实例缓存，Rendering2D 的旧注册入口转发到该缓存并保留注册者的程序集所有权。绘制命令旧名称及迁移到 Rendering2D 的旧 Graphics 程序集限定名由所属类型声明迁移映射；Core 不增加包类型转发。

绘制诊断中，Graphics 提供 `RenderDiagnostics` / `RenderDiagnosticPhase` 通用采样机制，Rendering2D 提供 `SceneRenderDiagnosticPhases`，UIElements 提供 `UIRenderDiagnosticPhases`。阶段由调用方持有，采样器仅在当前捕获内按名字合并，不注册或保留扩展程序集。平台统计信封不解释标签；Profiler 按数据展示。旧 `SceneRenderDiagnostics` 类型标识由新类声明别名，旧阶段枚举的源码调用迁到所属包的标签，仓库调用方同步重编译。

Framework 的 `BEngine.App` API 保持不变。构建 API 使用 `BEngine.Editor.Build`；Player 和托管代码协议使用 `BEngine.Player` 及其子命名空间。原程序集中的引擎类型迁到所属 Core 或功能包，仓库内使用方同步更新引用并重新编译，不在 Core 添加指向具体包的类型转发。

场景、Prefab、资源 GUID、工程设置和部署文件格式保持兼容；程序集位置变化会使相关编译缓存失效。Application 仍是必需包，旧 BFrame 身份由清单迁移，不再替代 Diagnostics 等无关包。

默认 AOT 游戏 UI 协议与流程由 Application 的 BEngine.App.Startup 提供；AssetBundles 仅暴露 AssetBundleStartupContext / IAssetBundleStartupWorkflow 和按所有者卸载的工厂注册点。Core 的 IPlayerStartupControl 是中立宿主控制接口，不定义更新阶段或确认按钮。Application 单向依赖 AB，依赖声明与实际程序集引用保持一致。

场景引用恢复由 Core 的 SceneAssetLoading 和只读 SceneReferenceRestorer 扩展点协调，组件所属包持有具体策略，内容包只提供 IResourceObjectProvider。独立工具使用 Core Editor 的 ProjectScriptSession 持有自己的编译与加载生命周期；Testing 不再管理全局编辑器程序集。详见 `Development/Tools/SceneReferenceBoundary.md`。

通用 Player 宿主发布回调属于 Core Player Editor：默认适配器生成裁剪参数、静态宿主注册、动态依赖描述、发布报告和宿主契约。内容适配器通过 `PackagedAssemblySources` 延迟提供自身归档中的冻结程序集位置，不接管 SDK 发布机制。AB 的格式发现、格式输出校验和 StreamingAssets 规则留在 AB。迁移及验证边界见 `Development/Tools/PlayerPublishBoundary.md`。

编辑器构建生命周期由 Core 的 `EditorBuildSession` 持有。Player、Runtime 包导出和 AB 菜单使用同一公开会话协议，成功提交或失败释放只结束一次；AB 菜单保留主线程完成和呈现策略，底层归档构建不自动广播外层事件。未将 `RaiseBuildStarted` / `RaiseBuildFinished` 公开给包，相关语义与测试见 `Development/Tools/BuildSessionBoundary.md`。
默认 AOT UI 资产路径与旧配置迁移属于 Application 的 `AotTemplateLayout` / 模板策略；Core 的 `AotProjectLayout` 只约束启动程序集与场景身份。内置图形后端选择、离线编译要求及宿主链接变体由 Platform 的 `PlatformGraphicsBuildPolicy` 管理；AB 只适配 Shader 构建产物到内容归档。公开常量迁移及验证范围见 `Development/Tools/StartupTemplateShaderBoundary.md`。

# 托管代码构建

Core Player Editor 通过 `PlayerManagedCodeBuildPipeline` 管理输入收集、编译、代码/资源收敛、链接与共享镜像。内容提供者实现 `IPlayerManagedCodeResourceGraph`，贡献资产闭包及资源筛选；返回值不包含可替换的代码镜像。AssetBundles 持有分区/归档规则，不再自行驱动通用代码链接循环。详细接入与验证范围见 `Development/Tools/ManagedCodeBuildBoundary.md`。

# Live Editor 脚本编译

EditorWorkbench 使用 Core Editor 的 `ProjectScriptHostSession`，不再持有内部编译器接口、构建图或程序集引用字典。Core 管理增量编译、请求归属、加载图替换、取消等待及自有程序集清理；工作台只管理编译进度和重载交互。隔离工具会话不受工作台释放影响，详见 `Development/Tools/ProjectScriptHostSession.md`。这一迁移不表示所有包的内部访问或职责混杂已经消除。

## Compilation transaction state

Core Editor's `CompilationPipeline` owns the paired compilation transaction and
read-only compiling state. EditorServices forwards that state; EditorWorkbench
owns user-facing progress/reload policy and releases the Core scope on completion,
failure, cancellation and scheduling rejection. Neither package sets compilation
state or invokes unpaired notification raisers. See `Development/Tools/CompilationTransactions.md`.
This removes a specific private coordination dependency; the remaining editing,
scene and cross-package internal accesses still require semantic review.


场景引用、整棵层级接入和关闭通过 Core Editor 的 `EditorSceneEditing` 提供；工作台保留选择、Undo、脏标记、导入和通知策略。源码保存调用已注册的公开文档编解码契约，运行时路径与临时性使用既有公开机制。详见 `Development/Tools/EditorSceneEditing.md`。


编辑器宿主通过 Core Editor 的 `EditorHostRuntime` 初始化应用编辑状态、发布 Play Mode 状态、推进编辑时钟并完成输入帧。工作台保留暂停/单步、场景与 UI 策略以及时钟快照恢复；不再直接使用 Core Runtime 的内部状态 setter、时钟或输入清理入口。详见 `Development/Tools/EditorHostRuntime.md`。


Core Editor 通过 `AssemblyReloadEvents` 的事务作用域持有程序集替换通知配对；首次启动只在扩展初始化和导入器协调成功后显式宣布就绪。`ProjectScriptHostSession` 准备反射及初始化自有扩展，排除其他可卸载工具图。工作台保留调度、菜单、选择、进度和重载交互策略。详见 `Development/Tools/EditorReloadLifecycle.md`。

包管理器装配由 Core Editor 的 `IProjectPackageManagerFactory` 持有，调用方控制返回管理器的生命周期。Shader 请求、串行执行、结果归属和过期拒绝由 `ProjectShaderCompilationSession` 管理；Graphics 保留 Shader 格式、缓存产物和指针发布，工作台保留任务调度及交互策略。详见 `Development/Tools/EditorCompilationOwnership.md`。


启动记录、无界面包命令与资产源事务的边界见 `Development/Tools/EditorStartupCommandBoundary.md`：Core 记录启动快照并管理包命令生命周期，工作台保存启动 profile 和界面策略；EditorServices 声明资产源能力，AssetPipeline 执行复制/移动。构建不再自动向所有包开放引擎内部成员。
