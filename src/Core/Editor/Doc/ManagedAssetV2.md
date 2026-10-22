# 原生资产对象图 v2

`.asset.yaml` 的 `BEngine.ManagedAsset` 新增 version 2，使用 `graph` 保存类型化成员；`name`、`hideFlags` 存放根对象元数据。Scene 已使用的对象图序列化器处理 Fix64、二维数学类型、枚举、集合、共享引用和多态 BObject 资源引用。创建、保存、菜单创建、运行时 BAsset 加载、AssetBundle 字节加载使用同一数据模型。

version 1 的 `data` 原始 YAML 仍可读取。保存旧资产会升级为 v2，旧版引擎不理解该格式；不要让旧 Player 加载新格式内容。此更新不改写未保存的项目资产。

业务代码使用 `AssetDatabase.CreateAsset/SaveAsset`。底层工具可用 `ManagedAssetSerialization.Serialize` 和 `Deserialize<T>`；Serialize 会拒绝 runtime-only 资产。持久化对象引用要求被引用资产已保存，场景对象引用会报错。普通 DTO 仍用 YamlUtility，其行为未更改；自定义后缀、自定义 Save/Load 和旧的内嵌 subasset payload 不自动升级。

`ISceneRuntimeSystem.BeforeUpdate` 是新增的默认空实现扩展点，在 Start 后、FixedUpdate 和 Update 前每帧执行一次；ScopedSceneRuntimeSystem 同步转发。原系统 Update/FixedUpdate 时序不变，输入动作包使用这个阶段采样。

平台 SPI `IInputService` 新增 `IsGamepadConnected(int)`，自定义平台输入实现需要提供该方法；不能通过 gamepadCount 推断连续的设备 ID。内置 DotNet 输入服务已实现，负数或未连接 ID 返回 false。
