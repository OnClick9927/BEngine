# 共享编辑器图标的职责与生命周期

## 归属

`EditorBuiltinIcons`、`EditorAssetIcons` 和默认脚本/场景/Prefab 图标策略由
Graphics Editor 迁至 EditorServices Editor，命名空间仍为 `BEngine.Editor`。
它们只管理资源路径、文件/目录分类、类型元数据和当前编辑器主题查询，不解码图片，
不上传纹理，不创建 GPU、窗口或默认工作台。

Graphics 继续管理纹理、字体、材质、Shader、图片编解码及相应导入器。
实际通用编辑器图标文件仍由 EditorWorkbench 提供；领域工具图标仍由领域包提供。
Graphics Runtime 的两个引擎标记是 Player 闪屏资源，此次未迁走。

公开类、嵌套图标目录类、字段和方法签名保持不变。已编译使用方需要重新编译；
序列化类型查找通过迁入包声明的 `RuntimeTypeAlias` 支持旧 Graphics Editor
程序集限定名（包含 0.1.0.0 完整限定名）。Core 没有新增包引用或类型转发。

## 注册与卸载

名称、类型、类型回退、组件回退和主题回调均记录调用程序集的所有权。
同一所有者再次注册同一项会替换自身项；另一个所有者覆盖时保留仍存活的旧贡献。
卸载只移除该所有者及由被卸载程序集定义的回调，然后恢复仍存活的贡献。
类型变更会清空查询缓存，避免继续显示已经卸载的图标。

显式传入 null 仍清空对应回退/主题通道，保持原 API 语义。
IMGUI 的退出清理不再无条件清空共享主题回调；统一由共享编辑服务按所有权清理。
生命周期入口仍是 `RuntimeAssemblyCleanup.Unregister`。单独调用
`UnregisterExtensions` 只清理扩展注册，不能替代类型索引等完整卸载过程。

类型查询不再在缓存命中前创建 LINQ 捕获对象。可见注册顺序仅在注册/卸载时重建，
保持精确类型优先和继承匹配原有顺序，不引入每帧注册扫描。

## 验证

`Test-SharedEditorIcons.ps1` 在两个独立进程运行旧版和当前版公开 API 消费者。
它逐项比较常量、名称别名、扩展名回退、默认类型、文件夹状态、深浅主题路径；
扩展名回退使用同一个空格式注册夹具，避免将旧 Graphics 传递加载的 AssetPipeline
格式注册误认成图标目录本身的行为。另行验证格式注册的图标优先级。

当前消费者只引用 Core、共享编辑服务及 DotNet 平台，不加载 Graphics、Rendering2D、
IMGUI 或 EditorWorkbench。测试覆盖旧类型身份、覆盖/恢复、乱序卸载、缓存失效、
重复注册、调用者与回调定义者不同的清理以及可回收程序集。

变更为 Editor 代码。此次 Player 分发的实现等价性另做程序集核验，不将未运行的
Windows/Web 游戏标为本轮新实测。全部验证记录位于
`Temp/Engine/Verification/SharedEditorIconOwnership_20261114`。
