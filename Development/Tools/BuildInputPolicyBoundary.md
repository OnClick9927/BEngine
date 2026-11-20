# 构建输入格式与平台字体策略

Core 提供代码、类型与资源引用分析。各格式的识别与类型语义由所属功能提供；平台字体后端的原生绑定与兼容性由 Platform 提供。Core 不维护 Shader/UI 后缀、控件标签规则或字体库选择规则。

## 文本资源

`BEngine.Editor.PlayerResourceTextFormats` 在 Core Editor 提供按程序集所有权的格式贡献。Core 自带通用 YAML/YML、JSON、XML、TXT 文档识别。Graphics Editor 注册 Shader/Include 及其文本后端格式；UIElements Editor 注册 BXML/BSS/BCSS，并贡献 BXML/XML 标签的类型发现。

扩展通过模块初始化调用 `Register`。只需声明扩展名时使用字符串数组；需要从格式中发现类型时使用带读取委托的重载。委托只返回类型名，不加载运行时对象。相同程序集重新注册会整体替换自己的贡献，其他程序集不受影响；不合法的注册不会覆盖原贡献。`RuntimeAssemblyCleanup` 移除所属程序集的格式和读取委托，重载重新注册。

格式查询使用不可变快照；查询不扫描包或程序集，资源与类型分析仅发生在构建阶段。已经取得的读取序列属于调用方的短期构建输入，不应跨程序集卸载长期保存。现有场景、Prefab、GUID、资源地址与保存设置不变。

原有 Shader/UI 文本引用保持可达。Graphics 额外声明其 WGSL、MSL、Metal 和 CG 文本，防止这些实际文本资源中的引用因旧 Core 名单不完整而被忽略；不将 SPIR-V 等二进制格式当作文本。

## 平台字体

`BEngine.Editor.PlatformTextBuildPolicy` 位于 Platform Build。它持有原生文本绑定的公开锚点、Web FreeType/Skia 编译符号和绑定选择、反射类型字面量策略，以及最终非宿主程序集的类型引用/转发契约检查。

Core 的脚本编译、输入图、宿主初始化和通用反射根分析组合此策略，不自行维护 SkiaSharp/HarfBuzzSharp 身份。未移除任何绑定时保留已有输入集合。FreeType 仍保留 HarfBuzz；只出现残留 AssemblyRef 行但没有有效类型契约时不会拒绝构建。完整 Web canonical 构建在裁剪后检查存活契约，其他构建在编译输入阶段检查，禁止把死代码误判为最终依赖。

验证由 `Development/Tools/Test-BuildInputPolicyBoundary.ps1` 分别运行无功能包 Core、实际 Graphics/UIElements 集成和 Platform 进程。真实旧程序集对照、普通/严格编译、完整安装回归和实际 Player 构建记录位于工作区 `Temp/Engine/Verification/BuildInputPolicyBoundary_20261113`。这些验证不代表所有 Core/包职责已完成审查。
