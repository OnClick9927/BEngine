# 动态顶点流与上传状态

Graphics 的 `GraphicsMeshUploadStream` 持有 CPU 顶点、上传历史及有预算的比较快照。
Rendering2D 继续持有场景排序、几何键和投影策略，通过公开写入作用域生成顶点；
不再访问 Graphics 的私有快照方法或在场景包中保存“上次上传仍匹配”的证明。

```csharp
using var vertices = new GraphicsMeshUploadStream(dynamicMesh);
using (var write = vertices.BeginWrite())
{
    // 初始化全部新增元素。新范围原有内容不作保证。
    FillVertices(write.GetSpan(0, floatCount));
    write.SetLength(floatCount);
}
vertices.Upload();
```

写入作用域内不可上传、嵌套写入或改变快照策略。借用 Span 不可跨下一次写入、
作用域结束、上传或释放保留；完成的作用域不能重新写入，也不能关闭后续作用域。
写入异常保留部分 CPU 数据，调用方可以重建；它不会被误认为已上传的数据。
驱动上传异常后必须重传，成功后才建立匹配状态。

连续变化的生产者可同时使用 `BeginWrite(false)` 与 `Upload(false)`，跳过比较和
快照复制。切回保留快照时，在第一次修改之前由 Graphics 从已知成功上传的 CPU
内容建立快照。CPU 没变化时直接复用，无需逐字节比较。相机是否变化仍由场景包判断。

`maximumRetainedBytes` 只限制比较快照，不限制活动顶点容量；0 禁用快照。
`SetLength` 增长部分及稀疏写入前的间隔填零，缩短保留容量。
未绑定的流可先生成数据，再 `AttachMesh`，以保留纹理管线的延迟创建。
只能绑定一个 Dynamic mesh；mesh 由调用方释放。在流外修改 mesh 后必须调用
`Invalidate()`，与现有 `GraphicsMeshUploadCache` 的独占上传约定一致。
这些类型面向单个同步生产者，不创建线程，不承担并发写入协调。

验证入口：`Development/Tools/Test-MeshUploadOwnership.ps1`。独立绘图消费者覆盖失败恢复、
写入生命周期、多流、预算、外部更新及 10000 次预热连续/静止写入分配。
`Tests/MeshUploadOwnership/Parity` 对旧/新渲染器的 256 帧实际顶点与 draw 区间
做哈希比对，包含混合纹理、移动/静止、局部修改、空帧与缓存禁用。
记录型 RHI 的耗时只代表 CPU 提交，不代表 GPU FPS。
