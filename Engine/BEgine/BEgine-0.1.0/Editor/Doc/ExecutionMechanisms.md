# 执行与编辑器扩展机制

Core 对外提供执行机制，默认工作流由宿主或扩展模块组合。Core 不声明任何具体扩展包依赖。

## 运行时

- `RuntimeLifecycle.BeginSession()` 返回一次运行的初始化会话。同一会话中，每个初始化方法只执行一次。默认宿主使用 `HostStartupSession.Begin()` 建立 `HostRuntimeLifetime`，先执行 `BeforeSplashScreen` 再显示启动界面；运行线程通过 `HostRuntimeLifetime.Acquire()` 接管该会话，所有场景和代码域停止后才释放。跨线程接管必须显式启用，只允许在尚未创建场景时完成。
- `HostStartupSession` 管理环境进度、Logo 动画和启动完成门控。所有内置平台都在应用内部呈现；桌面启动画面和场景复用同一个主窗口及图形设备。动画与环境准备并行，显示进度时必须成功呈现 100% 后才允许切入场景。结束启动画面不关闭主窗口，也不结束已移交的初始化会话。
- `WindowScenePresentation` 和 `EmbeddedScenePresentation` 负责窗口、输入和场景呈现；宿主提供场景集合和流程回调。`HostRuntimeFrame` 统一推进引擎调度并结束输入帧，即使流程回调失败也保持帧清理。
- `PlayerSceneRuntimeSession` 执行由任意内容提供者准备好的场景。调用者注册 `IRuntimeSceneManager`、`ISceneRuntimeFactory`，加载初始场景，并提供 `RuntimeWorkspace` 和服务容器。会话接管运行初始化生命周期、场景执行、闪屏门控和可选的嵌入式呈现；释放时停止并卸载场景，但不释放调用者的服务容器。使用 `Tick()` 驱动外部表面；原生窗口已推进调度时使用 `UpdateScenes()`，避免一帧重复推进 BTask。
- `InitializeThrough(phase, assemblies)` 可以单独推进新加载的代码域。场景加载仍发出逐场景生命周期事件，不会重跑已有代码域的进程初始化。
- `RuntimeAssemblyCleanup.Unregister(assemblies)` 移除引擎持有的注册、回调及待执行任务。调用者负责先停止代码域，并处理自己的程序集租约和外部资源引用。清理操作在所属引擎线程执行。
- `Resources.PrepareReferenceAsync(path, context)` 将准备请求转发给实现 `IAsyncResourceReferenceProvider` 的资源提供者。提供者拥有数据准备策略，Core 仍使用统一的对象引用绑定路径。
- `IResourceReferenceOwner.ContainsReference()` 只查询资源归属元数据，不加载资源。准备请求遵循提供者优先级；高优先级同步提供者拥有该引用或无法声明归属时，不会继续预热被它遮盖的低优先级资源。
- `BEngine.Profiling.Profiler` 和 `BEngine.Instrumentation.NativeMemoryTracker` 提供轻量采样与原生分配钩子。详细嵌套采样、调用栈、原生分配记录和对象图检查由可选诊断实现提供。未安装记录器时，钩子在首次初始化后不分配记录对象。采样作用域应在创建它的线程上结束；记录器卸载后，尚未结束的作用域自动失效。
- `ITextDataCodec` 和 `ISerializationMetadataProvider` 分离文本格式实现与 Core 数据机制。默认宿主初始化文本支持；只读取已编译二进制数据的逻辑宿主可以只引用 Core。`RuntimeWorkspace` 不写入工程布局，创作目录和编译产物由编辑器的 `ProjectWorkspace` / `ScriptAssemblyStore` 管理。

## 编辑器

- `IEditorHost.Step()` 实现实际单帧执行；公开 `EditorApplication.Step()` 调用这一操作。
- `EditorScriptingDefines.SetSymbols()` 是修改当前工程有效脚本宏的统一入口。`IEditorScriptingDefinePersistence` 可用于同步宿主自己的构建配置。
- `IEditorLayoutDefaults` 提供默认布局和只读预设；Core 的布局存储负责用户布局的保存与迁移。
- `EditorExecutionPump.Register()` 为当前线程安装临时界面泵，长时间同步等待可以调用 `Pump()`。释放注册后恢复上一层界面泵。

资源编译器通过现有注册机制接入公共运行时资源编译入口。编译配置分别表达插桩、共享引用和缓存选项，宿主自行组合发布策略。

## Player 内容扩展

`IPlayerBuildContentProvider.PrepareAsync()` 只负责准备该内容实现需要的文件。默认的 `Adapt()` 使用 Core 的 `PlayerPlatformBuildTargetAdapter`，统一转发目标平台参数、运行时程序集、输出布局和平台构建能力。需要发布钩子或额外校验时，继承适配器并覆盖 `CreateBuildHooks()` / `ValidateOutput()`；传输策略由 `UsesHttpStreamingAssets()` 声明。没有增量内容更新的提供者无需实现版本发布方法，调用时默认明确抛出 `NotSupportedException`。

`RuntimeBuildResources.Register()` 由资源所属模块声明启动所需目录。`EnumerateFiles()` 接收“运行时程序集名 → Resources 根目录”的解析函数，返回程序集、逻辑相对路径和源文件路径。它使用调用时的注册快照，检查目录边界，按确定顺序枚举，去除同一程序集重叠目录产生的重复文件，并支持取消。内容实现自行决定后端过滤、压缩、加密、存储格式及运行时 `IResourceProvider`，Core 不指定资源包格式。

自定义发行组合可通过 `PlayerHostRegistry.Register()` 提供 `IPlayerHost`，仍使用公共 `BEnginePlayer` 入口及现有平台宿主。标准发行组合的内容、程序集和热更策略由其模块继续管理；这组扩展接口不新增预设包模式。
