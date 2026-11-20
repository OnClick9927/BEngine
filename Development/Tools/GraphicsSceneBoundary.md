# 通用绘制与 2D 场景边界

Graphics 提供图形资产、绘制命令、设备资源及绘制贡献契约；Rendering2D 提供世界场景的几何、相机、投影、排序与批次组织。UIElements 的绘制实现只依赖前者，不需要装载 Rendering2D。

## API 迁移

- Graphics 中的 `RenderSortKey2D`、`RenderBatchKey2D`、`RenderSubmission2D`、`RenderBatch2D` 分别改为 `RenderOrderKey`、`RenderBatchKey`、`RenderSubmission`、`RenderBatch`。值语义、命令顺序及借用批次的有效期保持不变。
- `RenderCamera`、`CameraClearMode`、`RenderViewportTransform2D`、`SceneQuad2D`、`SceneQuadPayload2D`、`SceneRenderContext2D` 和场景贡献接口迁至 Rendering2D，命名空间不变。
- 自定义普通绘制器使用 `IDrawContributor`，输入为场景、目标尺寸、层掩码和对象过滤。世界场景扩展继续使用 `ISceneRenderContributor2D`。
- 所有贡献者使用 Graphics 的 `RenderContributorRegistry`，按注册 ID 的 ordinal 顺序排列，每个设备缓存实例。旧的场景注册入口保留为转发入口，不创建第二份缓存。
- 注册、替换或程序集卸载使缓存失效，并释放所替换的设备实例。普通帧直接借用稳定快照；调用方不得在注册变化后继续持有旧快照。

旧命令类型名及旧程序集限定名通过 RuntimeTypeAlias 解析。原有场景、Prefab、资源 GUID 和设置不需要重写；二进制消费者需要重新编译。仓库内消费者已同步，Snake 三处变更仅涉及绘制 API 类型名称。

## 验证

`Test-GraphicsSceneBoundary.ps1 -EngineBinaries <发行版 BuildRuntime> -OutputDirectory <Temp 子目录>` 包含两种独立进程配置：

- DrawingOnly：不引用 Rendering2D，验证真实 UI 命令收集与 RHI 绘制、过滤、旧名称映射和所有权清理，最后断言场景绘制程序集未被加载。
- Scene：增加 Rendering2D，验证相机/几何归属和旧标识、共享回调顺序、绘制、排序稳定性及批次合并。

两种配置均检查自定义集合回调导致的 UI 状态变化、重入注册、程序集覆盖及卸载恢复、异常释放、设备缓存命中和零分配快照。Benchmark 项目在相同记录型 RHI 下绘制 500 个 Sprite 与 500 个 UI 矩形，检查输出命令、顶点和批次数；记录 CPU 提交和分配，不代表实际 GPU 帧率。实际 Editor、Windows 和 Web 回归结果单独记录在工作区验证目录。

共享绘制诊断尚有场景/UI 专属阶段，属于下一步边界清理，不将本次迁移视作整个架构整改完成。
