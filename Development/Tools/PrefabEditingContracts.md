# Prefab 编辑机制与工作流边界

`BEngine.Editor` 中的 `EditorPrefabEditSession` 拥有制作副本、来源与实例 ID 映射、增删组件/子树的映射更新，以及保持现有对象身份的层级恢复。`EditorPrefabRefresh` 准备来源合并结果，区分无变化、仅来源元数据变化和需要恢复层级的变化。Core 通过已注册的 `IPrefabResolutionBackend` 调用来源继承策略；继承解析与三方合并算法仍属于 AssetPipeline。

这些类型不选择项目写入路径或直接读写源文件；来源解析委托给已注册的后端。它们不注册 Undo，不标记脏对象，不发出工作台通知，不引用具体包。内部持久化 DTO、身份设置器和来源快照不作为公开字段或方法参数暴露，也不通过新增 JSON 转换跨越程序集边界。原有快照相等比较算法保持不变。

EditorServices 的 `PrefabUtility` 继续选择目标源文件，检查制作写入权限，通过文档服务保存文件，记录文件 Undo 和预览感知的层级 Undo，标记脏对象并发出实例更新通知。批量刷新的延迟、编译/Play Mode 保护和预览停止策略也保留在该包。值复制回调仍使用当前 SerializedObject/SerializedProperty 的可编辑规则，并保留跨字段共享/循环数据的复制行为。

公开制作会话采用显式生命周期：`Open(root, source)` 或准备结果的 `OpenSession()` 创建自有副本；`Dispose` 只销毁副本，不销毁借用的实例和源资源。调用方需要将修改包在自己的编辑事务中，发生异常时恢复事务快照；机制不会暗中建立历史记录。`AcceptSourceChanges` 应在源文件成功保存后调用。仅元数据更新通过 `ApplyMetadata` 应用，不会把刚保存的 Prefab Stage 再次标为未保存。

Core Runtime 中的场景/Prefab 资源结构和 GUID 格式不变；新增代码只进入 Core Editor。已有 PrefabUtility 的公开 API 保持不变。此处并不表示 AssetDatabase、资产身份访问或全部编辑包的历史友元访问已经整理完成。

验证入口：`Development/Tools/Test-PrefabEditingContracts.ps1 -EngineBinaries <完整二进制闭包> -OutputDirectory <Temp 下目录>`。`-CoreOnly` 只运行没有 EditorServices/AssetPipeline 程序集依赖的消费者；完整模式另外使用真实来源解析和文档服务，覆盖文件 Undo、属性/组件/子树操作、外部引用、嵌套/Variant、来源元数据更新、构造失败回滚和重试。
