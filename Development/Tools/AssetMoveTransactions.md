# 资产移动事务的职责

`EditorServices` 的 `AssetMoveTransaction` 提供同步、作用域内的文件移动日志、
源文件编辑快照、导入与失败回滚。它只使用通用项目提供者及平台文件能力，
不引用 TextureAtlas、Graphics、IMGUI 或默认工作台。

`TextureAtlasMoveHandler` 仍决定图集格式、旧版伴随 PNG 的命名、纹理引用更新，
以及生成在 Library 中的纹理不应按源 PNG 移动。图集打包、像素处理、生成子资源
仍由图集包负责。没有改变包清单、场景、Prefab 或现有 GUID 格式。

扩展的入口仍是 `AssetDatabase.MoveAsset`。它先运行资产修改处理器，再分派
`IAssetMoveHandler`。处理器内部可调用：

```csharp
return AssetMoveTransaction.Execute(transaction =>
{
    transaction.Move(oldPath, newPath);
    transaction.EditSource(newPath, physicalPath => UpdateDocument(physicalPath));
});
```

事务的 Move 直接调用项目提供者，避免再次分派相同处理器。保护路径、Play Mode
及 Assets 范围检查仍然生效。事务不重新调用伴随文件的修改处理器，保持旧行为。
不应保留作用域、跨异步边界使用它，或在编辑回调中切换工程。

失败时逆序回滚已完成的移动；即使提供者是在磁盘移动成功后的刷新阶段失败，
也会记录可确认的磁盘移动。随后先恢复全部编辑过的源文件字节，再导入恢复后的
路径。重复编辑只保留第一份快照；目录移动会同步更新快照位置。回滚失败会保留
原始错误及回滚错误，不报告成功；未能移回时在实际保留的路径恢复源文件。
事务结束释放快照并失效相关缓存；没有每帧扫描、后台任务或持久缓存。

真实 Editor 回归同时发现并修正了共享资产管线中的旧问题：增量导入主资产时，
不能只重新收集 Library 生成子资源，还必须重新导入仍存在的 Assets 文件子资源。
该索引及导入修正位于 AssetPipeline，不在 TextureAtlas 中补一次全工程刷新。
重读源文件和元数据可以同时观察伴随文件更新、解除父引用或源文件删除。

Windows 上偶发的原子替换错误由 Platform.DotNet 处理：只对原生共享/锁冲突
32、33、1175 做有限重试（最多额外 62ms），仍调用原子的 File.Replace。
持续占用及其他 IO 失败继续抛出；不删除目标、不清除只读属性，也不以覆盖复制
替代原子发布。1176/1177 可能已改变文件名称，不重试。
依据为 [Microsoft ReplaceFileW 的失败状态说明](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)。
这里增强瞬时错误恢复，不断言已定位此前每次启动替换失败的外部占用来源。

这不是文件系统崩溃恢复日志，也不会掩盖提供者的部分文件/元数据损坏。
提供者仍负责单次移动及元数据的一致性。独立故障注入、真实 Editor 和 API/依赖
检查的结果位于 `Temp/Engine/Verification/AssetMoveOwnership_20261115`。
