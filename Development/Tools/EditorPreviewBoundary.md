# IMGUI 预览、引用与生命周期边界

Inspector 预览提供方契约、预览图像值和提供方发现缓存归 IMGUI Editor 所有。Core Editor 不再持有 `IAssetPreviewProvider`、`AssetPreviewImage` 或 `AssetPreviewProviders`，包管理器也不再调用具体预览缓存。Rendering2D Editor、TextureAtlas Editor 本就通过 IMGUI 绘制预览并声明依赖，不新增包关系。

## 机制复用

- 文件画布解析器使用公开的 `EditorResource.FindPath` 和 `ResourceFolder.FindPath`，保持 Editor 根优先、Resources 回退、绝对路径及预览修订后缀的处理，不复制资源搜索或缓存。
- `RuntimeAssetReferences.BindReference` 接入无导入、无读取、无缓存的资源地址。保留对象 Id、现有 GUID 文本与 artifact；可选 source 为 null 时保留原值，空串时清空。绑定根引用会清除子资源关联；子资源应使用已有 BindSubAsset。该中立入口不公开 BAsset 可变字段，也不包含 Texture 或 GUISkin 策略。
- GUISkin 的内置分段背景和未解析图片通过 BindReference / EnsureAssetType 绑定。其 URI、相对/绝对引用、空 GUID、显示名和不可编辑标记保持；不会为占位图制造导入 GUID。
- 预览中的资源解析使用现有 RuntimeAssetReferences.ResolvePath，保留 GUID/子资源加载及 artifact 优先、项目地址和上下文相对路径的顺序。
- PropertyDrawer 使用 RuntimeTypeCache.TryCreateInstance，继续共用 Core 的构造工厂缓存。不在包内复制工厂，不提前执行构造，不添加每帧发现。保留公共无参构造筛选、继承匹配和同优先级排序、每 SerializedObject/属性实例、扩展失效及构造异常隔离。
- Inspector Dispose 在关闭回调和 SerializedObject 释放后调用 Core 的 DestroyImmediate；由 BAsset 清理加载缓存及对象注册。Dispose 有重入保护，未启用的直接创建实例也能释放注册而不虚构 OnDisable。Destroy→Dispose、Dispose→Destroy 和 destroyed 回调重入均只清理一次。

## 卸载与类型迁移

IMGUI 继续订阅 EditorExtensionLifecycle.Invalidated。其 RuntimeAssemblyCleanup 回调在任意扩展程序集清理时释放预览提供方缓存，覆盖包卸载和加载回滚；清理 IMGUI 自身时还退订生命周期通知。Core 只调用中立的生命周期和程序集清理机制，不知道具体预览实现。

两个迁移的公开类型由 IMGUI 声明旧 `BEngine.Editor` 程序集限定名的 RuntimeTypeAlias。通用别名属性允许接口，复用原有发现与解析，不在 Core 添加包类型转发。仓库使用方重新编译；这是程序集归属调整，不承诺第三方旧二进制无需重编译。场景、Prefab、GUISkin、GUID 和工程设置数据格式未变。

`Test-EditorPreviewContracts.ps1` 验证无 Editor/Graphics 的 Core 引用入口、实际 Windows 文本能力下的 UI 工作流，以及真实可卸载预览扩展。测试使用公共行为入口；Core 无界面探针不加载 Editor 或图形程序集。完整编辑器回归及公开面/静态 IL 审查另行记录。严格编译已越过 IMGUI，后续包诊断仍需继续审查，不能将此层通过等同于全部 44 包完成。
