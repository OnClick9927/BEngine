# 场景查询、对象身份与资源所有权边界

Core 提供对象身份、场景成员查询、定点数学、Transform 缓存和通用资源生命周期。Rendering2D 负责相机选择、Sprite 来源解释、渲染收集、排序、遮罩与几何；UIElements 负责文档、样式、布局和控件输入。Core 不解释 Sprite 元数据，不创建 UI 对象，也不选择绘制策略。

## 通用能力

- `BObject.RestorePersistentIdentity` 是给派生类型解码器使用的 protected 水合入口，保留对象注册和运行时 instance ID。公开 `Id` 仍不能赋值，不允许恢复已销毁对象。
- `RuntimeAssetReferences` 公开既有引用协议的路径归一、GUID 定位、子资源标识解析及子资源元数据查询。`TryReadMetadata` 与 `ReadSubAssetMetadata` 保留两个独立扩展点；元数据文件名和领域设置由适配器决定。
- `Vector2.Rotate` 保存原 Transform 旋转助手的定点运算顺序、角度归一和直角快速路径。`Transform.GetWorldPose` 一次读取已缓存的世界位姿，并拒绝已销毁对象。
- `Component.TryGetGameObject` 允许未挂载组件安全查询所属对象。UI 不再访问 Core 的内部 nullable owner。
- `BAsset.VisitOwnedAssets` 是 protected 所有权声明。`MarkTransient` 由 Core 将本体及拥有资源标为不可保存；借用引用不得声明为拥有。迭代遍历支持共享、循环和深链。重复调用会重新检查当前拥有图，不缓存旧图。Scene 本体仍使用 Core 的场景临时标记机制。
- `BAsset.CopyAssetReferenceFrom` 向派生资源提供受保护的克隆支持，只复制来源引用，不复用对象身份、不改变生命周期、不复制拥有的 payload。已销毁的来源和目标均拒绝复制。

## 包内迁移

Rendering2D 复用 `Scene.QueryComponents<T>` 的只读成员快照。热点采用索引循环，保持原查询时刻的成员集合，不使用接口枚举或逐帧数组复制。非 Sprite 渲染器缓存按快照引用失效，不读取内部 hierarchy revision。Sprite 的弱引用缓存和纹理导入规则留在包内，旧 `guid:...#subasset=...` 及主/子 GUID 语义不变。

UIElements 使用公开文本解码与引用路径服务。输入法请求和候选窗矩形经过已有 `InputUtility` / `IInputService`，不扩大 Input 内部状态 setter。VisualTreeAsset 声明其 inlineStyleSheet 与克隆拥有的样式；源文档借用的样式不被标记或释放。默认工作台通过公开资源生命周期入口标记 Play Mode 镜像。

## 验证和范围

`Test-SceneObjectContracts.ps1` 使用未获 friend 授权的独立消费者验证所有权、身份、定点运算、缓存查询、实际绘制、IME、Sprite 来源与 UI 克隆。记录 RHI 验证调用及批次，不代表真实 GPU 性能；另用 Example/Snake Windows/Web 验证实际 Player。

关闭 `BEnginePackageInternalAccess` 只禁用生成的 Core 友元授权。包内手写 `InternalsVisibleTo` 仍需逐项审计；当前证明这些包通过公开 Core 契约工作，不代表它们对 Graphics、AssetPipeline 或其他包的访问已全部公开化。Animation、TextureAtlas 等后续隔离诊断也不能按编译错误条数视为全部剩余职责问题。
