# 源资产与 Prefab 编辑边界

Core 管理基础对象图捕获、恢复、GUID 映射、验证、克隆、解码提交和引用身份；AssetPipeline 管理源文件、导入产物选择、元数据索引、缓存失效，以及 Prefab 继承和三方合并策略。Player 的资源提供方通过统一契约解码，不重复实现文档 DTO。

## 公开契约

- `SerializedHierarchy`、`PrefabDocumentSnapshot`、`PrefabReferenceSnapshot` 和 `PrefabReferenceSet` 是脱离运行对象的编辑句柄。内部 DTO、对象注册表和字段存储不公开。修改句柄不会修改已有 PrefabAsset。`CreateAsset` 防御性复制对象图；该成本发生在编辑发布时，不在每帧执行。
- `ParseAsset` 为立即恢复且不保留编辑句柄的路径，直接消费新解析数据，避免普通 Prefab 加载额外复制。`SceneAssetDocuments` 提供场景对象图编解码。上述 API 不负责文件读取或保存。
- `IPrefabResolutionBackend` 和 `IPrefabAuthoringBackend` 通过现有服务贡献注册。源继承、嵌套引用、三方合并和变体保存由 AssetPipeline 实现；变体保存继续尊重当前文档编解码器及引用/加载后端，开发者可替换这些策略。Core 不识别包名。
- `AssetLoadSlot` 是拥有者线程使用的弱缓存槽。对外只开放命中、加载和递归保护；私有缓存对象和弱引用仍属于 Core。句柄复制保留同一缓存槽的身份。
- `RuntimeAssetDecoding` 提供解码器查询、文件加载与带提交门控的异步解码。源适配器在门控中检查 generation 和导入产物时间戳，Core 继续检查解码器生命周期、取消和数据释放。
- `IAssetArtifactResolver` 替代全局静态导入路径委托。EditorServices 注册实现；程序集卸载时随服务贡献一起清理。
- `RuntimeAssetReferences` 提供路径、身份和受限绑定方法；子资源身份通过只读查询获取。Document 仍尊重当前开发者注册的身份解析器。
- `Resources.LoadProvidedAsset` 只查已注册资源提供方，不回退源文件，避免源适配器递归调用自身。
- `RuntimeTypeCache.ResolveSerializedType` 现在是公开的持久化类型解析能力，继续使用已有类型别名和缓存。

## 兼容与迁移

Prefab/Scene YAML 格式、版本和 GUID 不变。`IPrefabResolutionBackend` 的方法签名改为不透明快照，需要重新编译自定义后端；仓库使用方已同步迁移。核心文档 DTO 保持 internal，没有通过类型转发或扩大 friend 权限解决依赖。

保存源文件仍采用同目录临时文件、覆盖移动及 finally 清理。运行中场景与瞬态对象仍禁止写回源文档。普通 Prefab 加载不额外建立编辑快照；显式创建可继续编辑的快照后发布资产会复制数据以隔离后续修改。

## 验证入口

`Development/Tools/Test-AssetAuthoringContracts.ps1` 使用独立、无 Core 私有权限的消费者，覆盖对象与引用隔离、旧格式 GUID、变体与嵌套 Prefab、64 层深度限制、循环/缺失引用、主/子资产、原子保存、瞬态限制、缓存重入与恢复、异步变更、取消、解码器替换及释放。

`Development/Tools/Tests/AssetAuthoringContracts/Benchmark` 使用同一个公开 API 夹具，对照源 Prefab 解析和实例化的耗时与分配。详细结果存放在仓库 Temp 下，不把微基准结果解释为整个编辑器或游戏的性能提升。

本阶段 AssetPipeline Runtime 已能关闭 package private access 编译；整仓隔离审计仍会暴露其他包的历史私有访问，不代表全部职责整理结束。
