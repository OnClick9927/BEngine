# AssetBundles

独立资源包，提供 AssetBundle Package 分组和增量构建、异步加载、StreamingAssets 直接读取、下载更新与修复。

运行时程序集为 `BEngine.AssetBundles`，编辑器程序集为 `BEngine.AssetBundles.Editor`。现有 `BEngine.AssetBundles`、`BEngine.Content` 与 `BEngine.Editor` 的公开 API 命名空间保持不变；自定义程序集若曾从 `BEngine.Player` 或 `BEngine.Application.Editor` 引用这些类型，应重新编译并引用独立程序集。

运行时依赖 Networking、Pooling、Graphics 与 Rendering2D；运行时和编辑器均不依赖 Application。底层 AB 构建器只处理导入资源和 `AssetBundleBuildOptions.AdditionalBundles` 的通用内容。Player 内容接入、Play Mode、程序集与包资源收集由本包适配层实现，注册到 Core Player 的宿主与构建扩展接口。

**构建入口：** `Assets → Build Asset Bundles...` / `AssetBundle Packages...` 只构建所选导入资源。完整 Player 构建由 Core 管理构建事务，本包的内容提供者补充代码和运行资源。原 `IncludeHotUpdateAssemblies`、`IncludeManagedSymbols`、`IncludePackageRuntimeResources` 构建选项已移除。

`EditorVirtualAssetBundleBuilder`、`IEditorVirtualContentSession` 及其 factory 归属 `BEngine.AssetBundles.Editor`，命名空间为 `BEngine.Editor.Build`；使用这些集成类型的编辑器脚本需要更新引用并重新编译。

编辑器的 **Project Settings → Editor Play Mode** 由本包提供内容接入，EditorWorkbench 提供界面。可选择默认的 Virtual 或读取发布目录的 Real。参见 [Editor Play Mode：虚拟 AB 与真实 AB](Doc/EditorPlayMode.md)。

- [配置、分包与增量构建](Doc/index.html)
- [运行时加载、按包更新与修复](Doc/runtime-guide.html)
- [格式与加密说明](Doc/Overview.md)
- [平台构建与 StreamingAssets](Doc/PlatformAndBundles.md)

目录依赖闭包与加载句柄的临时列表使用有容量上限的 Pooling 工作区；目录索引、返回的资源和持有中的句柄保留独立所有权。加载取消、失败和管理器销毁都会归还工作区，句柄支持并发幂等释放。

[索引、并发与更新约定](Doc/EngineContracts.md)。
