# 默认 AOT 工作流边界

Core Player 的 `IPlayerStageTransitionFlow` 只负责开始、驱动、请求切换和接收切换失败。可选 `IPlayerStartupControl` 提供只读的通用状态及推进命令，供嵌入式宿主和验证工具使用。Core 与 Platform 不解释更新、确认、延后、AB 或游戏入口策略；早期应用内闪屏仍由 Core 协调，不经过本工作流。

Application 拥有 `BEngine.App.Startup` 下的 `IAotStartupFlow`、`AotStartupSnapshot`、`AotStartupPhase` 和 `AotStartupFlow`。它组合 AB 的检查、应用更新及内容校验能力，提供默认确认、延后、失败回退、重试、进入游戏策略。工作流只使用 BTask、单线程队列和传入的能力，不创建线程、不读取原生路径、不直接调用平台宿主。项目模板同步使用新命名空间。

AssetBundles 拥有 `AssetBundleStartupContext` 与 `IAssetBundleStartupWorkflow` 接入协议。内容适配器准备资源及托管代码阶段，通过 `AssetBundleStartupWorkflows.Create` 获取上层选择的策略；内容阶段的事务提交、补偿、资源/代码域释放仍由它提供。它不默认构造交互流程，也不依赖 Application。缺少已注册策略时给出明确错误，开发者需提供策略，不能在没有确认的情况下自动更新游戏。

Application 在宿主模块初始化时注册优先级 -100 的默认工厂。开发者可以在内容准备前调用 `AssetBundleStartupWorkflows.Register(context => new MyWorkflow(context))`，正常优先级 0 会覆盖默认值；相同优先级取最近注册项，释放返回的 lease 后恢复之前的工厂。注册及内容驱动均在运行时线程执行。卸载所属程序集时撤销注册，不逐帧扫描程序集或工厂。每个内容会话独占其工作流，负责释放；`ConfigureServices` 仅向 AOT 场景注册借用的接口实例，不转移所有权。

API 迁移：`BEngine.Player.Startup.*` 移至 `BEngine.App.Startup.*`，需重编译调用方；目标包注册旧枚举及快照类型标识映射，Core 不添加转发。`IPlayerRuntimeSession.StartupFlow` 改为 `StartupControl`，需要详细游戏交互状态的 AOT 组件通过场景服务解析 `IAotStartupFlow`；通用宿主通过 `StartupControl.Current` / `Advance` 接入。未修改场景、Prefab、GUID、资源格式或项目设置。Application 的 csproj、asmdef 和包清单同步增加对 AB 的单向依赖。

运行 `Test-AotWorkflowBoundary.ps1`，分别验证无 Application 的内容扩展和默认 Application 流程。覆盖工厂优先级/卸载、通用接口无反向依赖、确认/延后/必需更新、内容校验、失败回退、重试、BTask 取消及释放、字节进度、自动测试开关兼容，以及 100000 次宿主状态读取零分配。额外运行无包 PlayerContentExecution、基础服务/绘制/编辑器回归与 Example / Snake Windows 和 Web 验证。所有验证产物位于工作区 Temp。
