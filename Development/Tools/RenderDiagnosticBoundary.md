# 绘制诊断职责与验证

Graphics 提供通用、有界的 CPU 与分配采样。场景、UI 和自定义绘制扩展持有各自的阶段名，不需要修改基础绘图库中的枚举，不建立全局阶段注册表。

## API 归属与迁移

- `SceneRenderDiagnostics` 改为 Graphics 的 `RenderDiagnostics`。旧持久化类型标识保留迁移别名；源码重新编译，不承诺旧二进制 API 兼容。
- 原枚举中的 2D 阶段改用 Rendering2D 的 `SceneRenderDiagnosticPhases`，标签前缀为 `Rendering2D.`。
- 原 `Ui*` 阶段改用 UIElements 的 `UIRenderDiagnosticPhases`，标签前缀保持 `UI.`。
- 自定义扩展保存 `static readonly RenderDiagnosticPhase`，在已有捕获中调用 `using (RenderDiagnostics.Measure(phase))`。
- `SceneRenderDiagnosticSnapshot` / `SceneRenderDiagnosticSample` 继续作为平台契约中的中立统计信封。既有字段与序列化格式保留，新增 `OverflowPhaseOccurrences` 在旧 JSON 中默认为 0。

同名阶段按 ordinal 规则合并，名称区分大小写。默认值不产生采样；构造时拒绝空白名字。阶段行与渲染器类型行独立，即使名称相同也不混合。共享采样器不解释名字，不引用 Rendering2D、UIElements 或 Diagnostics 实现包。

## 成本与生命周期

关闭诊断时，不创建 CaptureContext 或缓冲，不读取时钟、分配计数或渲染器类型。启用时按调用方持有的 CaptureContext 延迟创建并复用缓冲，每次捕获清空前一帧的标签及计数。每个捕获最多保留 64 个阶段名、64 个渲染器类型名、64 层采样作用域；嵌套绘制最多使用 8 个有效捕获。超限汇入独立的 other 行并记录次数，避免无界扩展数据。

已发布的不可变统计快照由消费者持有，创建快照会分配内存；不能将启用诊断描述为整体零 GC。单独的缓存复用和采样记账不分配。阶段没有全局注册，不保留扩展 Type/MethodInfo 或实例。旧令牌通过捕获代次和作用域标识校验，重复/乱序释放不会关闭新捕获；异常清理保留原绘制异常并提供最后未完成捕获。

## 回归入口

`Test-RenderDiagnosticBoundary.ps1 -EngineBinaries <BuildRuntime> -OutputDirectory <Temp/...>` 运行独立 Graphics 及场景集成两组检查，覆盖身份迁移、阶段扩展、ledger、溢出、嵌套、令牌复用、异常恢复、JSON 和关闭诊断成本。集成检查使用记录型 RHI 绘制 500 Sprite + 500 UI 矩形，校验 1000 提交、2 批次及关闭诊断后的零 GC；它不代表真实 GPU 帧率。

`Tests/RenderDiagnosticBoundary/Benchmark` 可针对拆分前后 SDK 分别编译，使用相同工作负载比较采样关闭/开启时的 CPU 与分配量。验证产物位于工作区 `Temp/Engine/Verification/RenderDiagnosticBoundary_20261111`，游戏业务源码不随本次改动迁移。
