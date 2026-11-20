# AOT 模板与图形构建策略的职责

Core 的 `BEngine.Editor.Build.ProjectSystem.AotProjectLayout` 仅定义 AOT 程序集、资产根目录、asmdef 和场景身份，以及相应的分区/保护规则。Core 不定义 AOT 场景必须使用哪套 UI，也不提供业务更新界面。

Application 持有默认快速开发模板。原来位于 Core 布局类的以下常量移至 `BEngine.App.Editor.ProjectSystem.AotTemplateLayout`：`UiAssetRoot`、`UiDocumentAssetPath`、`UiStyleAssetPath`、`UiControllerAssetPath`、`LogoAssetPath`。仓库调用方已迁移；外部源码使用这些常量时应更新类型名并重新编译。常量值、生成资源内容、已有路径、场景与 Prefab 格式及资源 GUID 不变。Core 不增加指向 Application 的类型转发或替代常量。

`PlayerProjectPolicies.NormalizeBuildSettings` 是通用工作流扩展事件。Core 在校验文件格式/版本后调用，在保存前继续执行自身的配置校验。回调可迁移自己拥有的旧配置，不应递归读取或保存构建设置。失败向调用方传播，不能把无效配置写入文件。提供方负责在程序集卸载时移除回调。Application 使用它迁移 `Assets/Aot/BEngine.png` 到自己持有的 UI 目录；无 Application 的 Core 不推断这条路径。开发者设置的其他图片保持原值。

Platform 的 `BEngine.Editor.PlatformGraphicsBuildPolicy` 持有内置宿主后端支持、桌面链接变体名称、离线 Shader 后端选择、运行时编译器取舍及配置有效性。当前内置 Vulkan 后端可消费离线 SPIR-V；其他内置后端沿用各自源代码格式。选择离线编译与编译工具是否成功注册分开：请求了离线产物而缺少工具时必须报错，不能静默漏掉资源。Core Player 的既有后端选择 API 继续保留，转调平台策略；SDK 发布和内容归档使用同一策略。

Core Editor 的 `ShaderBuildPipeline` 继续统一收集/去重 Shader 变体，并调用注册的平台编译器。Graphics 持有源文件与资源格式规则。AssetBundles 不再判断 Vulkan，也不直接选择原生编译器，只按平台返回的后端调用 Core 的公开收集/编译 API，再将产物地址、哈希、字节和统计适配进自己的归档。Shader 产物地址及归档格式保持不变。真正的编译器实现仍在 Platform。

专项验证入口为 `Test-StartupTemplateShaderBoundary.ps1`，需要当前编辑器依赖闭包和上一版真实 `Build/bin`。Core、Application、Platform 分进程执行：包含无框架配置、可替换模板、旧数据迁移、GUID 保留、回调卸载、内置后端矩阵、真实上一版模板和 AB 归档适配对照。归档适配对照使用确定性的测试编译器隔离原生工具；原生 SPIR-V 编译由现有 EditorBootstrap 测试及实际 Player 构建覆盖。组合矩阵、旧程序集对照和检查条数分别记录，不能叠加为独立检查总数。

本轮只调整构建期职责，不据此宣称运行时 CPU、GC 或峰值内存改善。全部包的语义、反射、工具宿主及生命周期审查仍须继续。
