# Player 宿主发布机制与内容格式的边界

Core Player Editor 的 `PlayerPlatformBuildTargetAdapter` 默认提供宿主发布机制：

- 根据工程选项生成 SDK 裁剪、文本绑定和反射序列化参数。
- 生成静态宿主注册、动态代码依赖描述和预编译程序集注册语句。
- 在 SDK 发布后记录裁剪报告、发布日志和实际保留的宿主契约。

这些机制不再由 AssetBundles 提供，也不要求加载某个内容包。具体平台仍负责执行 SDK/原生工具链；内容格式不生成平台发布参数。

内容适配器可以覆盖受保护的 `PackagedAssemblySources` 属性，提供延迟执行的回调。平台需要复用已随包发布的冻结程序集时，才传入资源根目录及确切程序集路径。回调返回 `PlatformPackagedAssemblySource`，包括归档地址、长度、完整性、偏移及代码哈希；引擎和平台不解释具体格式，不预先扫描或重写这些位置。

AssetBundles 的适配器只在这个回调中使用 `AssetBundleManagedImageSources.Discover`；AB 专属的输出校验与 StreamingAssets 规则继续由 AB 持有。旧的 `CreateBuildHooks` 虚方法仍可扩展，公开调用方无需改签名。默认实现现在会提供完整的通用发布机制；自定义内容适配器应优先只贡献自己的格式能力。

本次未改变 Player 部署清单、场景、Prefab、资源 GUID 或归档格式。移除的内部 `PlayerPlatformBuildHooks` 归属调整后由 Core 持有；相关编辑器程序集需重编译。Core、平台和宿主不增加包引用、包身份判断或指向包的类型转发。

验证入口：`Development/Tools/Test-PlayerPublishBoundary.ps1`。测试不执行 SDK，而是通过公开适配器接入可观测平台发布者，实际运行发布回调，核对裁剪参数、注册源码、依赖描述、失败/成功报告及宿主契约，并确认没有加载 AB、Graphics 或 IMGUI。真实 Windows/Web 发布仍需 Example/Snake Player 回归；这些测试不能代替全平台工具链验证。

此项迁移不代表所有职责问题已经解决：AB 内容构建中仍有通用代码编译、代码剥离等调用需要继续审查；严格编译、普通包静态 IL、tooling 和反射调用的审查范围也必须分别记录。
