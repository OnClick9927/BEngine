# 资源所属包的编辑快照

Core Editor 的 `EditorObjectSnapshot` 提供内存快照、值复制和保留量估计；
EditorServices 的 Undo/Redo、交互快照和层级工作流复用这一机制。
Core 不识别 Texture、RenderTexture、材质或其他具体包类型。

只读属性、需要通过领域方法修改的状态，可以通过公开的
`EditorObjectSnapshotAdapter` / `EditorObjectSnapshotAdapters.Register` 补充。
普通序列化字段仍由 Core 捕获；扩展在普通状态恢复后执行。同一对象可组合多个
适用的贡献者，按注册顺序运行。接口仅在编辑操作中调用，不增加帧回调。

Graphics Editor 为 RenderTexture 注册额外的宽高快照。恢复及复制均调用
Graphics Runtime 的 `Resize`，由资源本身管理目标退役及尚未结束的绑定。
不保存原生目标、GPU 内容或某一设备的句柄，不使 Runtime 依赖 Editor。
新尺寸将在下一次实际绘制时创建目标；原来的绑定先完成再释放。

扩展应返回自有、可反复使用的托管数据。可变数组须先复制；不能返回目标借出的
缓冲、服务、原生租约或需要释放的句柄。恢复不能消耗/修改捕获数据。Core 将额外
数据计入既有的 Undo 保留量估计；这不是精确 GC 堆或 GPU 内存统计。

注册由实现及目标类型所在程序集拥有。替换同类型贡献者、退出包或卸载程序集时，
注册和类型查询缓存失效，旧快照的入口会在写入对象前明确拒绝使用。
这是内存状态而非持久化格式，不对已经退休的代码执行恢复。
调用者负责在卸载对象所属代码前释放其外部对象及快照引用；不能把内存快照作为
跨代码版本的存档。Core 的可撤销注册不会继续持有已退休的适配器或类型。

独立 Core 消费者、真实 Graphics Undo/Redo/复制/目标绑定与可收集程序集卸载验证
位于 `Development/Tools/Tests/ObjectSnapshotExtensions`；Editor 初始化及代码重载探针也使用
同一公开快照和 Undo API。旧场景、Prefab 和资源的持久化格式均未变更。
