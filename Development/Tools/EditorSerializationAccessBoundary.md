# EditorServices 与 Core 的序列化、资源和工程机制边界

本轮范围是 EditorServices 对 Core 非公开机制的调用，以及资源注册变化时的缓存发布。全仓库 44 个包的语义审查尚未完成；静态依赖和友元访问检查不能代替实现审查。

## 机制与调用方

- Core 的 `SerializationMetadata.GetObjectMembers(Type)` 暴露普通持久化对象的成员查询。它保留继承顺序、私有序列化字段和当前元数据提供方，不暴露可变的内部反射数组。它不使用组件根对象的保留名字过滤策略，也不把 Inspector 的隐藏规则混入持久化规则。
- EditorServices 的嵌套 Inspector、Prefab 比较和元数据预热使用上述查询。Inspector 的分组、显示与交互策略仍归 EditorServices；没有向 Core 添加 Inspector 或具体包名称。
- Core 的 `ResourceFolder` 将一个相对目录段适配到已有资源根、解码和虚拟提供方。它不持有缓存或提供方注册。EditorServices 选择 Editor 目录并委托加载；Core 不写死这个目录。
- Core Editor 的 `BPackageManager.OpenManifest` 提供不加载程序集的清单事务入口。工程创建使用现有公开的 ProjectWorkspace.Open，而不是开放构造器或复制包解析器。
- 场景加载和默认场景保存使用已有 ISceneDocumentCodec；调用时传入的服务提供方优先于默认注册。创建默认场景后释放临时 Scene，保留工程暂存与失败回滚。源地址和程序集定义资源使用 RuntimeAssetReferences 绑定。
- Selection 使用组件的公开 TryGetGameObject，保留未附着组件返回空的行为。Prefab 与 Scene 源文件写入使用公共瞬态对象策略，运行中的场景和对象不得写回源数据。

## 缓存生命周期

RevertAsset 和嵌入子资源解码在调用提供方前捕获资源缓存版本。提供方执行期间若注册发生变化，当前调用仍可取得已完成结果，但不得将旧结果重新发布到缓存。下一次查询使用新提供方；没有注册变化时保留原有缓存身份。提供方代码在缓存锁外运行。

这些改动不增加每帧反射、扫描或轮询，不增加持有类型的新强引用缓存，也不修改包清单、资源 GUID、Scene/Prefab 格式。

## 验证入口

`Test-EditorAccessContracts.ps1` 包含无图形 Core 消费者和 EditorServices 集成消费者，两者没有 Core 友元授权。覆盖普通对象成员规则、活动元数据提供方替换、文件与虚拟资源、稳定/重入资源恢复、嵌入子资源、Selection、场景提供方优先级、运行态写入限制、程序集定义身份、工程创建及失败回滚。

同一集成测试二进制分别加载上一轮和本轮 EditorServices，单独复现资源恢复和嵌入子资源的旧缓存发布问题；修订后的二进制均通过。测试夹具缺失 using 和编辑器平台服务的早期失败不作为产品缺陷证据。
