# 渲染扩展与性能接口

引擎本体提供 RHI、排序、场景几何和公开扩展接口。UIElements、TiledMap、Lighting2D 等包保留各自的控件、索引、着色及绘制实现；扩展代码应放在包内。

`ISceneRenderContributor2D` 可以实现 `BeginScene(in SceneRenderContext2D)` 与 `Collect(in SceneRenderContext2D, ...)`。上下文携带场景、相机、视口、对象筛选器及共享的层级排序索引；通过 `GetHierarchyOrder(gameObject)` 获取排序值，避免每个包重复遍历层级。该上下文及其字典仅在收集过程中有效，不能保留到下一次场景收集。旧签名仍由默认接口方法调用。

`RenderCamera.CreateViewportTransform(width, height)` 返回可复用的 2D 投影；`RenderViewportTransform2D.FillQuadCorners` 复用相机旋转和缩放，四角的顺序与 `SceneQuad2D` 顶点颜色一致。`RenderCamera.FillViewBoundary` 写入调用方的 Span，减少临时数组。

`GraphicsMeshUploadCache` 包装调用方拥有的动态网格，按字节比较上次的顶点数据；相同数据无需再次上传。它保留的 CPU 快照默认最多 8 MiB，可以通过 `maximumRetainedBytes` 调整，设为 0 关闭缓存。对该网格的全部更新应经过同一缓存；若在其他路径更新网格，下一次使用前必须 `Invalidate()`。缓存自身的 `Dispose()` 只释放 CPU 快照，网格仍由调用方释放。

场景渲染器分别合并 Core 的实色、贴图顶点上传，然后按原有批次顺序和绘制范围提交。扩展绘制与透明排序保持原位置；静止画面可跳过字节相同的上传。

`SceneRenderStatistics` 增加收集、排序批次、Core 几何和 CPU 提交耗时，以及 Core 实际顶点上传次数和字节数。`GeometryMilliseconds` 与上传计数不包含包自己拥有的网格；`SubmitMilliseconds` 包含扩展绘制但不包含 Present。Diagnostics 包的 Profiler Rendering 详情显示这些数据，并累加同一帧内的多个视口。

桌面 OpenGL/Vulkan GPU 时间查询延迟读取已完成的结果，避免统计功能额外等待 GPU。Vulkan 仍在下一帧复用资源前等待正常的帧栅栏；这次修改没有实现多帧并行或多线程渲染。
