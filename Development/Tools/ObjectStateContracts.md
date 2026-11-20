# 对象状态与运行时包边界

Core 管理通用对象图捕获、恢复、引用身份及版本验证。Animation 决定哪些动画成员需要保存，并管理曲线、事件、控制器、混合树和领域校验。动画包不再引用内部 ComponentGraphData、SerializedMemberData 或 ComponentObjectGraphSerializer。

`ObjectStateSnapshot` 是拥有离线数据的状态快照。Capture 可以选择持久成员名称，拒绝基础身份及未知成员；Restore 保留目标身份，支持外部场景对象解析器。快照可以重复恢复和克隆，不与原始托管对象共享可变状态。捕获和恢复只要求宿主注册基础平台服务，不要求图形、窗口或文本格式提供者。

`SerializationDataAdapterAttribute` / `SerializationDataAdapter` 为不可变或不透明类型声明其数据表示。适配器只根据调用方明确请求的 CLR 类型查找，文档内容不能指定适配器类型。查找通过弱类型缓存完成；不增加需要清理的全局包注册列表。数据表示不得再次使用适配器，避免不消耗文档节点的循环。Core 提供机制，YAML 层实现对应的格式接入；二进制读取复用既有绑定计划，普通数据节点不增加适配器查询，也不改变节点数或深度限额。

快照的内部图 DTO 仍不公开。序列化桥保留原来的 version/members 映射，不增加包装字段、类型标签或程序集迁移标记。AnimationClipData.BindingGraph 与 AnimatorControllerData.Graph 使用该快照，旧动画和控制器文件可直接读取。

Lighting2D、SpriteSkinning2D 复用公开定点 Vector2.Rotate；Particles2D 复用 Transform.GetWorldPose；TiledMap 和 Animation 复用资源缓存代次；Timeline 和 Animation 复用文本解码。TextureAtlas 保留图集来源、校验和编译策略，来源解析经过公开协议，对象身份通过本类型的受保护水合能力恢复。YAML 独占转换器查询只公开状态与代次，不暴露或复制转换器注册表；编译前后的代次校验、外部转换器回退和损坏数据拒绝保持有效。Video 的 FFprobe CSV 字段切片属于视频解析器自身。

验证使用上一版已测试 DLL 生成的真实动画、控制器和图集文件，与新版输出逐字节比对；独立消费者没有友元授权。覆盖共享及循环状态、克隆隔离、选择成员、对象身份、文本/二进制/嵌套/空状态、自定义适配器、最大深度、图集转换器变更、损坏数据及视频索引边界。另有不引用图形或 YAML 的 Core 独立验证。

Runtime 到 Core 的隔离不能证明 Editor 和包之间已全部解耦。整体隔离编译已继续暴露 EditorServices 的资产记录及 Prefab 编辑 DTO 访问，后续需要迁移为资产数据库查询、编辑和文档操作契约，不能直接公开整个内部 DTO 模型。
