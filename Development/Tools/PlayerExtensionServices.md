# Player 内容扩展接口

Player 内容提供者通过公开接口调用通用执行机制，不能依赖 Player 或 DotNet 后端的内部实现。两者的 `BEnginePackageInternalAccess` 均为 `false`；Core 和平台之间的内部授权以及专用测试授权不受影响。

- `PlayerExecution.Complete` 只观察已经结束的 BTask/BValueTask；不会阻塞等待，也不会偷偷推进调度器。未完成的工作必须由所属运行时的 Update 推进。
- `PlayerExecution.TryCompleteShutdown` 用所属运行时的 Update 完成有界关闭。返回 false 表示推进失败或仍未完成；返回 true 表示已经观察完成，异常仍会送给报告回调，不能把返回值当作业务成功标记。取消按正常停止处理。
- `PlayerExecution.RunTransitionAsync` 封装激活、提交、完成及失败回滚。恢复前一阶段始终会在回滚尝试后执行；多个失败按原错误、回滚错误、恢复错误的顺序返回 AggregateException。调用方通过 BTask 调度器推进异步提交。
- `PlayerHostServices.ConfigurePersistentDataDirectory` 只配置路径，不创建目录。显式路径必须为绝对路径；未指定时读取宿主环境配置。外部宿主的路径服务由宿主拥有，子容器不能释放它。
- `PlayerHostServices.CreateBufferedLogWriter` 返回 `IPlayerLogWriter`。成功构造后由它拥有 TextWriter，Dispose 排空已接收日志并释放 Writer。队列及字符预算有界，饱和时保留原背压行为；不支持线程的宿主可使用同步模式。构造参数验证失败时不接管 Writer。
- `ManagedCodeServices.OrderModules` 校验并排序命名模块，拒绝重复、缺失和循环依赖。`ReleaseRegistrations` 按托管代码租约释放注册，不卸载程序集域；必须先停止对应运行时，并在引擎所属线程调用。
- `PlayerStartupPerformanceDiagnostics.Begin` 返回不可公开构造的值类型 Scope。采样关闭时不分配；开启时保持 128 个样本上限、嵌套关系和 Reset 代次隔离。

原执行、回滚、日志、路径和模块实现仍为 internal。此处为受支持的扩展入口，并未把实现类型整体改成 public，也没有新增逐帧反射或程序集扫描。仓库内内容扩展已同步迁移；依赖旧内部成员的二进制扩展必须重新编译。

回归使用非友元程序集 `PlayerExtensionServices`，覆盖成功、取消、故障、未完成操作、异步恢复、回滚、借用服务生命周期、日志饱和和零分配路径：

```powershell
./Development/Tools/Test-PlayerExtensionServices.ps1 `
  -EngineBinaries '<导出目录>/BEgine/BEgine-0.1.0' `
  -OutputDirectory 'E:/Project/_BZP/BEngine/Temp/Engine/Verification/PlayerExtensionServices'
```

Core Runtime、Core Editor 和 Player Editor 的历史全包内部授权仍待逐项迁移，不能将本次收紧理解为所有基础程序集已清理完成。
