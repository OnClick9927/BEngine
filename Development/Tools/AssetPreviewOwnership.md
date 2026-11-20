# 资产预览职责与 API 迁移

EditorServices 提供 `IEditorAssetPreview`、值描述 `EditorAssetPreviewContent` 和共享源元数据缓存 `EditorAssetPreviewSources`。资源所属包通过已有 `EditorAssetAdapter.Preview` 贡献策略，不创建另一套全局发现机制。

Graphics Editor 负责 Texture/RenderTexture、Material、Shader、Font 和图形源格式的分类、元信息及预览描述。它不引用 IMGUI，不要求窗口或 GPU 才能查询这些信息。共享文档资产的基础描述归 EditorServices。IMGUI 的 `AssetPreview` 保留原有入口，解释描述绘制通用图元；既有 `IAssetPreviewProvider` 的自定义 UI 优先级保持不变。

`AssetPreviewImage` 移至 EditorServices；原 IMGUI 程序集保留类型转发，旧 Core 和 IMGUI 类型标识由目标包注册别名。Core 不添加具体包类型转发。使用新 API 编译的扩展需要正常引用 EditorServices；已编译的旧预览插件可继续加载。

Sprite、图集、GPU 预览缓存和诊断图像统一使用共享源修订标识及失效接口。源缓存只保存路径、文件戳和标量结果，不保存资产对象或 GUI。程序集退休时释放注册及候选缓存，失效回调同时检查注册方、方法和目标对象的程序集。可选图片解码能力变更时重试已缓存的缺失结果。

稳定类型的候选列表仅在注册变更时重建，文件类型/路径相关的 CanPreview 每次查询仍会执行。注册与调用遵循既有编辑器主线程约定；文件缓存及失效订阅通过锁协调。可重载类型退休后清除类型候选，避免阻止程序集回收。

验证脚本：`Test-AssetPreviewOwnership.ps1`。覆盖无界面 Graphics 提供者、源缓存失效、类型优先级和回收、旧二进制插件，以及新旧 23 类预览在三种区域尺寸下的实际 GUI 命令和元数据精确对照。其他回归覆盖原生编辑器加载、Shader 编译、代码重载及导入设置 Apply/Revert。

本轮是职责修正，不声称整体启动加速或 Player 包体缩小。Graphics/Rendering2D 其他协作以及各领域包的语义审查仍在继续。
