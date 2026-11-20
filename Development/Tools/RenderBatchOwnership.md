# 绘制批次所有权

Graphics 的 `RenderBatchBuffer` 拥有连续命令区间的分组和可复用批次生命周期。
Rendering2D 仍负责场景排序、相机、几何和绘制提交，不再直接构造或修改 Graphics
内部的 `RenderBatch`。UIElements 自己的顶点批次规划保持不变。

```csharp
using var batches = new RenderBatchBuffer();
// 命令生产者先确定顺序；相同键的非相邻命令不会合并。
batches.BuildOrdered(commands);
try
{
    for (var i = 0; i < batches.Count; i++)
        Draw(batches[i]);
}
finally { batches.Clear(); }
```

输入为 `List<RenderSubmission>`，缓冲区借用其连续区间，不复制命令或排序。
借用期间不得修改输入；批次、列表视图和 Span 不得跨 Clear、重建、Dispose 使用。
实例由单一绘制调用拥有，不支持并发修改。稳定容量下 BuildOrdered/Clear 不分配。
Clear 保留空批次对象，释放源列表及 Material/Shader/Atlas/MaskGroup/PayloadType 键；
Dispose 另外释放复用容量并阻止再次构建。

原 `RenderBatchBuilder2D.Build(IEnumerable<RenderSubmission>)` 公开签名、稳定排序、
完整键的相邻合并规则和返回值接口保持不变，内部委托共享批次缓冲。
删除的构造、Add 和 BuildContiguous 都是内部实现；外部生产者应使用公开缓冲 API。
场景、Prefab、GUID、资源编解码和包清单未修改，Core/Platform 不增加包依赖。

`Test-RenderBatchOwnership.ps1` 用不引用场景/UI 的独立消费者验证完整批次键、输入顺序、
冷热容量变化、借用释放、可回收包类型和稳定分配。Parity 项目以同一源码分别链接
上一版及当前版，比较 512 组排序/批次快照，并注入绘制异常验证结束清理和后续恢复。
GraphicsSceneBoundary、GraphicsResourceContracts 和 RenderDiagnosticBoundary 同时回归。
记录型 RHI 的 500 Sprite + 500 UI 测试仅度量 CPU 提交与输出，不代替实际 GPU 帧率。
