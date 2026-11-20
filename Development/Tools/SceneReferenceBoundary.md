# 场景引用与独立编译会话边界

Core 负责读取和恢复不透明的 SceneLoadData。显式提供 IResourceObjectProvider 时，恢复完成后调用所属组件注册的 SceneReferenceRestorer；默认恢复路径保持原有行为。SceneComponentReferences 只提供旧字段的只读查询，不公开或允许修改内部场景 DTO。注册按实现类型替换，查找按组件类型缓存，仅在加载期间执行，程序集卸载时释放注册及类型缓存。

Rendering2D Editor 注册 SpriteRenderer 的旧引用恢复策略：内容存储有 Sprite 时使用该对象；没有替代内容时保留已有源 Sprite；两者皆无或提供方返回错误类型时失败。AssetBundles Editor 只提供资源对象和场景字节，通过公共 SceneAssetLoading 读取/恢复，再绑定场景地址，不再了解 Sprite 或内部场景文档。其私有 IResourceObjectProvider 直通 AssetBundleAssetLoader，保留原来每次解码和精确地址规则，不引入通用 Resources 提供方的对象缓存/目录搜索。场景失败和取消仍由 Core 释放部分对象。既有场景、Prefab、GUID、Sprite 子资源标识及序列化格式不变。

Rendering2D Editor 的默认场景使用已注册的 ISceneDocumentCodec 保存。Sprite 导入绑定调用所属包已有方法及 Core 的公开资产身份查询；拾取使用与原方法相同的 Vector2.Rotate 定点数实现。UIElements 的规则标题截取使用标准 Span.IndexOf，不再调用 Core 私有字符串辅助方法。

ProjectScriptSession 是 Core Editor 为独立工具提供的编译/加载生命周期。它复用现有包编译、项目 Runtime→Editor 依赖图和平台作用域，既不发布或替换正在运行的编辑器项目图，也不使用测试包类型。会话持有自己的加载作用域；Editor 阶段只共享本会话的 Runtime 类型，Dispose 或失败时只清理本会话，不强制全局 GC。调用方须释放程序集及对象引用才能最终回收可卸载作用域。调用与释放均在编辑器所有者线程进行。

Testing Editor 的命令行通过 ProjectScriptSession 发现本次项目程序集，不再直接调用内部编译服务或全局卸载编辑器项目。服务组合、工程路径和报告策略仍由测试工具负责。

验证入口：Test-SceneReferenceBoundary.ps1。覆盖 Core 独立恢复（不加载图形/AB/UI）、YAML 与二进制场景、GUID、回调顺序、异步进度、取消/失败释放、注册卸载、Sprite 策略、AB 场景地址/歧义/读取失败，以及两个独立会话和已有编辑器图的真实卸载调用归属。另以格式解码器计数及实际场景恢复，核对直通加载的对象身份、精确地址和读取失败不被缓存掩盖。

本轮验证不能代替全部 44 个包的语义和反射调用审查；严格编译和静态 IL 结果只用于定位待处理访问。
