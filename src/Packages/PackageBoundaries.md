# 引擎与包的职责边界

以下记录当前实现归属；尚未完成的边界收敛在后文注明。Core 提供执行机制、基础数据与扩展契约，不直接引用包程序集或具体类型；部分历史友元元数据仍需收紧。Platform 通过 Core 契约调用外部能力。默认分发版组合完整功能；Core 可以独立执行无界面的场景逻辑。

本机 Unity 模块、包清单与 asmdef 的参照及适用边界见 [Unity 边界参照](UnityBoundaryReference.md)。

```text
Core Runtime
  对象 / 组件 / Transform / Scene / 生命周期 / 定点数
  BTask / 协作调度 / 输入 / Application 基础接口
  资源身份 / 引用 / 通用序列化 / 克隆扩展 / 内存预算
  DI / 模块注册 / 日志 / 轻量 Instrumentation / 类型迁移
  闪屏状态与进度协调 / 窗口、帧、绘制及 RHI 契约
  Player 内容无关的场景会话、窗口运行、阶段切换与退出清理
Core Editor
  工程 / AssetDatabase / 导入机制 / 包解析、缓存及编译
  菜单、设置、视图与服务贡献注册 / 延迟服务组合
  构建管线 / Shader 变体收集 / 编辑器与资源扩展契约
EditorServices（仅 Editor）
  SerializedObject / SerializedProperty / Undo / Selection
  共享编辑状态、工程设置、偏好与工具机制 / 资产创建与定位请求
Graphics
  Texture / Font / Material / Shader / RenderTexture
  图像编解码、纹理上传与缓存 / 通用绘制命令与批次视图
  与相机无关的绘制贡献契约 / 设备级贡献者注册与释放
  通用有界绘制采样 / 扩展自行声明采样阶段
  早期闪屏绘制 / 图形导入器及构建资源
Rendering2D
  Camera2D / Sprite / SpriteRenderer / 遮罩
  相机投影 / 场景几何 / 场景绘制、裁剪、排序与批次构建
  场景贡献者及 Gizmos / 场景资源与工具
  SceneView 工具契约、坐标转换、拾取扩展与手柄中心计算
  2D 场景绘制阶段的诊断标签
IMGUI（仅 Editor）
  GUI / GUILayout / EditorGUI / EditorGUILayout / 样式及布局
  EditorWindow / Inspector 基类 / 通用弹窗 / GUI 帧呈现器
EditorWorkbench（必需，仅 Editor）
  默认窗口、停靠与布局 / 菜单、图标与工作台资源
  Hierarchy / Project / Inspector / Scene / Game / Console
  Package Manager / Settings / Preferences / 默认编辑器宿主
Diagnostics（可选）
  性能及内存采集 / 远程诊断 / Profiler / Frame Debugger
Application
  Launcher / GameDomain / GameService / 游戏开发框架
  AOT 游戏入口与游戏模板 / 业务流程组合
AssetBundles
  AB 容器、版本目录、传输与挂载 / 真实及虚拟资源加载
  AB 内容构建、更新事务与托管代码内容适配；游戏启动策略由上层注入
```

Graphics Runtime 使用 AssetPipeline 的资源机制；Rendering2D Runtime 使用 Graphics。UIElements Runtime 使用 Graphics，不依赖 Rendering2D。Graphics Editor 提供导入器，不依赖 IMGUI；IMGUI Editor 可使用 Graphics Editor 的导入数据；Rendering2D Editor 使用这些能力；EditorWorkbench 再组合默认工作台。Diagnostics 的编辑器工具依赖 IMGUI、Graphics、Rendering2D 和 EditorServices，可以不安装默认工作台。

EditorWorkbench 不再提供功能包所需的共享编辑 API。`ProjectWindowUtil` 和 `EditorProjectSettings` 属于 EditorServices；前者发送中立的定位和 Ping 请求，后者负责原有 v3 工程设置的读写、校验与应用。静态 `SceneView` 工具入口、`SceneViewToolContext`、拾取提供者、拾取算法、Gizmo 收集与显隐、手柄视觉中心属于 Rendering2D Editor；默认 Scene 窗口仍在工作台。功能包没有指向工作台的依赖，UIElements 等只需创建资产的包也不因此依赖 Rendering2D。Rendering2D Editor 在加载时注册共享策略，卸载时释放拾取注册和 Gizmo 的类型及回调缓存。迁移保持命名空间、资产 GUID、场景和设置格式，旧程序集类型标识由所属类型声明别名；仓库内程序集同步重编译。

Graphics 的 `RenderOrderKey`、`RenderBatchKey`、`RenderSubmission`、`RenderBatch` 是通用有序绘制数据，不包含世界坐标、投影或场景几何；`IDrawContributor` 接收目标尺寸、层过滤和通用对象过滤。Rendering2D 持有 `RenderCamera`、`SceneQuad2D`、`SceneRenderContext2D`、排序缓存和批次组织。UIElements 注册独立绘制贡献者；场景宿主组合两类贡献者，但 UI 本身不加载场景绘制程序集。两类贡献者复用一个按设备缓存的注册表，没有每帧包扫描或跨类型命令转换。迁移和验证入口见 `Development/Tools/GraphicsSceneBoundary.md`。

Graphics 的 `RenderDiagnostics` 只提供有界 CPU/分配采样，不再维护 Sprite、排序、UI 等具体阶段枚举。Rendering2D 与 UIElements 分别持有自己的阶段标签；扩展通过 `RenderDiagnosticPhase` 声明标签，无全局阶段注册或逐帧程序集扫描。共享 `SceneRenderDiagnosticSnapshot` 仍是平台中立的统计信封；新增溢出计数兼容旧 JSON。关闭诊断时不分配采样缓冲、不读取时钟/分配计数/渲染器类型。采样缓存上限及 API 迁移见 `Development/Tools/RenderDiagnosticBoundary.md`。

Application 不提供引擎宿主、资源编解码、平台发布或 AB 实现，也不替代 Diagnostics 包身份。它通过通用工程策略注册默认游戏模板。默认发行版仍按项目策略标记 Application 为必需；其他包不反向依赖它。默认工作台保留通用 Build/Profile UI、命令行入口及 Play 工作流调度；真实/虚拟 AB 设置由 AssetBundles Editor 自行注册。Core/Platform 的通用启动器不引用这些组合。

通用编辑器启动器位于 Platform/BEngine.Editor.Host。它读取 editor.application.json 并调用 IEditorApplicationEntry。默认描述和组合项目由 EditorWorkbench/Editor/tooling.json 声明。运行时帧由 ISceneRendererFactory 提供；GUI 帧由 IEditorFramePresenter 提供；平台不构造具体场景绘制器或 GUI 绘制器。

编译前的工程策略通过包自己的 tooling `editorBootstrap` / `editorBootstrapProject` 声明，由统一导出器发布并记录依赖元数据。默认工作台及其 tooling 宿主均不静态依赖 Application；通用入口按描述组合解析器。Application 仅注册自己的游戏模板策略，不提供引擎发布实现。接入和旧描述兼容性见 `Development/Tools/EditorBootstrapBoundary.md`。

各平台 Player 宿主、移动端生命周期、Web 页面模板及运行时分发组合位于 `Platform/Player`；Core 不承载这些实现。导出后的 `BuildHosts`、`BuildRuntime` 布局保持不变。内容格式通过 `IPlayerBuildContentProvider.PackagingRequirements` 声明自己的打包约束，通用管线将不可变快照交给平台；Android 仅将声明转换为 SDK 的免压缩后缀，不认识任何包的文件格式。Web 的通用内容诊断入口为 `GetContentDiagnostics`。

AssetBundles 提供 AB 分包、增量构建、真实/虚拟加载、下载更新及修复，以及对内容事务和托管代码阶段的适配。默认 AOT 检查/确认/延后/重试/进入游戏策略及 UI 协议由 Application 提供，通过 AB 的工作流工厂接入，AB 不反向依赖 Application。通用 PlayerRuntimeSession、PlayerWindowApplication、PlayerSceneStage 已归 Core Player；AB 通过 IPlayerRuntimeContent 提供准备好的场景与内容提交、回滚、释放策略，平台窗口和嵌入式表面使用同一套场景切换机制。资源所属包通过 RuntimeResourceCodec 注册图形资源及子对象解码，AB Runtime 不再引用 Graphics / Rendering2D。Core 的通用阶段协议不再暴露 AOT 更新按钮；Platform 通过 IPlayerStartupControl 查询通用状态并推进，详见 Development/Tools/AotWorkflowBoundary.md。Networking 提供网络传输，Pooling 提供对象与集合池。UIElements 提供保留式 UI、UI Builder、HTML 转换和字体工具。Node Editor 与 Track Editor 只提供编辑器控件。其余功能包按各自 package.yaml 声明依赖，禁止反向依赖 Application。

构建内容提供者通过 `PlayerBuildSettings.ContentOptions` 与 `PlayerBuildRequest.ContentOptions` 传递自己的配置。Core 不定义 AB 压缩字段；AB Editor 的 `AssetBundlePlayerBuildOptions` 负责压缩设置及 `Project/Asset Bundle Build` 页面。旧构建文件和配置档中的标量扩展字段由通用加载器保存在 `legacy.<原字段名>`，因此没有安装原内容提供者时保存设置也不会丢失。提供者读取自己的旧字段，再在编辑时写入正式配置键；迁移不改变资源格式。

加密构建的密钥窗口、生成及校验由 AB Editor 提供；工作台通过 `IPlayerBuildEncryptionKeyPrompt` 请求密钥，只转发到统一构建请求，不理解密钥格式。自定义内容提供者可注册自己的提示实现。工作台的普通 Editor 程序集及其宿主均不直接引用 AssetBundles Runtime/Editor。

Core 初始化只注册构建能力，不强制存在内容提供者。内容扩展注册 `IPlayerBuildContentProvider` 后调用 `AddBEngineBuildPipelines` 组合完整构建管线；未安装内容扩展时，构建可用性查询返回不可用，编辑器仍可启动。编辑器服务解析优先保留宿主容器中的实例；延迟服务的构造函数和工厂也通过组合提供者解析宿主依赖，服务枚举合并宿主与新贡献，避免另建一份宿主单例。宿主实例和外部实例不由扩展释放；扩展只管理自己创建的实例及其子作用域。注册变化或宿主解绑时释放旧扩展，无需给工作台添加包引用，也不逐帧重建容器。回归入口见 `Development/Tools/Test-EditorServiceComposition.ps1`。

内容提供者通过 `IPlayerBuildContentProvider.RuntimeHostAnchors` 声明加载内容前所需的静态运行时模块。Core 在单次 Player/内容更新构建范围内生成模块注册及裁剪保留信息，不依赖编辑器偶然加载了哪些包。构建范围沿异步流程传递并在完成时释放；嵌套和并行构建的模块分类互相隔离。普通内容提供者默认不声明任何额外模块。

平台契约、Windows 编辑器后端、DotNet 后端及 Player Runtime 关闭了对包的内部访问授权。绘制数据校验、编辑器输入呈现周期及诊断纹理查询使用公共契约。Player 内容扩展通过 PlayerExecution、PlayerHostServices、IPlayerLogWriter、ManagedCodeServices 及启动采样 Scope 调用通用机制；执行、事务、路径和队列实现仍为 internal。公开入口及所有权约定见 `Development/Tools/PlayerExtensionServices.md`。Core Runtime、Core Editor 和 Player Editor 构建实现仍有历史内部访问授权；应逐项补充 SPI 后撤销，不能把整个实现无差别改为 public。

编译固定为 Core RT → Core Editor → 包 RT → 包 Editor → 项目 RT → 项目 Editor。同阶段依赖排序；未改变输入的阶段复用缓存。解决方案由 SyncSolutionLayout.ps1 同步；包的程序集引用必须与 package.yaml、asmdef 一致。Core 或接口归属变化会使包编译缓存失效。

包自己的 ModuleInitializer 注册资源格式、对象克隆与引用编解码、服务、图标、Shader 资源或呈现器。卸载通过 RuntimeAssemblyCleanup 及包程序集清理机制撤销注册。绘制热路径直接访问已解析的服务，不逐帧扫描包或程序集。

EditorWorkbench 的 required 标识位于其 package.yaml，包管理机制自动写入工程 manifest 并锁定移除。诊断工具可选。Editor 程序集及 Editor 资源不得进入 Player；运行时所需字体、Shader、图标等由所属包及 RuntimeBuildResources 声明，构建时汇总到引导资源，避免同时写入热更新资源。

迁移 API、旧数据处理及验证记录见 [Core 精简迁移说明](CoreSplitMigration.md)。
