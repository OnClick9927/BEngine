# Player 内容与执行边界

`BEngine.Player.dll` 提供 `PlayerRuntimeSession`，通过 `IPlayerRuntimeContent` 接收内容；不需要安装 AB、图形或 UI 包便可执行逻辑场景。桌面 `PlayerWindowApplication` 持有一个 `WindowScenePresentation` 并使用同一个会话，闪屏与游戏始终在同一窗口；Web/移动平台仍使用 `IPlayerRuntimeSession`。

内容提供者负责读取/挂载资源、准备程序集与场景、提交及回滚内容事务；Core 负责生命周期、Update、场景切换、失败补偿、窗口或外部表面接入和退出。Core 不推断内容容器或传输格式。

## 公开接口

- `PlayerSceneStage`：场景管理器、执行工厂、服务和启动场景路径；可显式转移内容释放回调及服务所有者。
- `IPlayerRuntimeContent.InitialStage`：准备完成的初始场景。无切换策略时启动后直接提交内容。
- `IPlayerStageTransitionFlow`：策略的启动、轮询和切换请求；可提供 AOT 工作流界面。Core 不实现更新检查或确认策略。
- `PrepareTransitionAsync` / `CommitTransition`：内容准备下一阶段，Core 激活后提交内容；失败时回滚内容、终止失败阶段并恢复原场景执行。
- `PlayerRuntimeDiagnostics`：可选观察回调，不规定日志位置或内容包格式。

## 所有权与调度

会话借用 `RuntimeWorkspace` 和服务引用，仅释放构造时显式转移的 `startupServiceOwner`、`originalStartupServiceOwner`。构造失败仍由调用方释放这些所有者。未提供运行生命周期时会话自行取得并持有；传入的生命周期可通过 `ownsRuntimeLifetime: false` 借用。

内容提供者拥有其阶段并在 `DisposeAsync` 释放；`PlayerSceneStage.DisposeAsync` 可重复/重入调用，复用同一操作，依次释放场景、内容租约、显式服务所有者。不同释放步骤的异常会汇总，不能跳过后续清理。提交和释放可异步完成，通过 BTask 调度推进。退出先取消准备和切换，再等待相关操作，最后释放内容和服务；未合作完成的操作通过 `IsDisposalPending` 暴露，后续可继续调用 `Dispose` 排空。

外部宿主每帧调用 `Tick`；原生窗口已经推进 BTask 和输入，使用 `UpdateScenes`，不重复推进调度器。稳定帧不扫描内容包、程序集或新建适配器。

## 迁移与验证

`BEngine.Player.PlayerRuntimeSession` 的类型全名不变，程序集归属由 AB 改为 `BEngine.Player`，需要重新编译使用方；不在 Core 添加指向包的类型转发。内部 `PlayerApplication`、`PlayerApplicationStage`、`PlayerWindowStartupRunner` 已删除；AB 的内容协调器只适配内容协议。场景、Prefab、GUID、内容目录及发布格式没有因本次迁移修改。

执行 `Development/Tools/Test-PlayerContentExecution.ps1 -EngineBinaries <导出引擎目录> -OutputDirectory <Temp 验证目录>`。测试程序集不引用内容/绘图包，也没有友元授权；覆盖准备、启动、稳定帧、异步提交、阶段切换、回滚、取消、重入及所有权。
