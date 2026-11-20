# Core 第三轮职责精简

本轮按七项审计结果迁移实现，Core、Platform 不增加对具体包的引用。资源 GUID、场景、Prefab、项目设置格式及六阶段编译顺序保持兼容；程序集归属变化后重新编译。

## 实现归属

```text
Core Runtime
├─ 场景执行与通用数学基础
├─ ISceneRuntimeRecorder、采样作用域和统计数据契约
├─ HostStartupSession、动画计时及进度呈现确认
├─ RuntimeNativeExecutionAttribute：程序集的原生执行需求
└─ SkipLoopGuardAttribute：已知边界内核的编译策略声明

Core Editor
├─ 通用序列化、Undo、Selection、资产和包编译机制
├─ IProjectShaderCompiler 注册、异步结果归属及失效控制
├─ 统一 Shader 变体收集
└─ PlatformBuildOptions：按类型存储构建扩展参数

Audio
├─ PcmSampleMixer
└─ MpegGaplessReadStream

Physics2D
├─ CellPairWorkspace2D、IndexPair2D
├─ FixedCircleSeparationWorkspace2D、CircleSeparationBody2D
├─ FixedCircleMovement2D
├─ FixedRectangleQueries2D、FixedRectangleEdges2D
├─ FixedPolylineQueries2D
└─ FixedKinematics2D

Diagnostics
└─ SceneRuntimeRecorder：阶段、回调、内核、耗时和分配量的详细记录

Graphics.Editor
└─ CgProjectShaderCompiler：源文件识别、pragma、导入参数、SPIR-V 和缓存策略

EditorWorkbench.Editor
├─ ComponentClipboard、ComponentValueSnapshot
├─ PackageExampleCatalog、PackageExampleInfo、PackageExampleLayout
└─ PackageDocumentationCatalog

src/Platform/BEngine.Platform.Build
├─ WebBuildOptions、WindowsBuildOptions、AndroidBuildOptions、IosBuildOptions
├─ WebFontBackend、WebScriptCompilationMode、PlayerGarbageCollectionMode
└─ 原生发布所需的程序集冻结、引用与身份清单
```

删除 `HostSplashScreen`、`HostSplashPresentation`、`IHostSplashWindow`、`IHostSplashHost` 及单独窗口所需的图片解码/BGRA 转换路径。Windows 在应用窗口中绘制闪屏；Core 等待实际呈现 100% 后再继续。保留 0.5 秒 Logo 动画、后台准备、快速准备仍完整播放动画等要求。

## 兼容与执行策略

- 迁移的数学和音频类型保留命名空间及算法。使用这些 API 的程序集应声明所属包依赖；Core 不提供反向引用或类型转发。原有程序集限定类型及嵌套泛型类型通过现有通用类型解析机制读取。
- `SceneRuntimeDiagnostics` 不再持有具体算法名或详细采样数组。未安装记录器时不读时钟、不读取分配计数，也不产生采样对象。记录器卸载后，未结束的作用域失效，不继续持有包实现。
- 记录器使用 24 字节的不透明令牌，通用采样作用域为 32 字节，与迁移前一致；阶段及槽位编码只在 Diagnostics 内处理。过期捕获、关闭后遗留的令牌和越界槽位不能污染后续采样。
- `ProjectShaderCompiler` 保留入口。具体编译器由 Graphics.Editor 注册；后台结果绑定其注册实例，旧实例卸载后不能提交过期结果。`ShaderCompilationResult` 构造函数对扩展开放。
- `PlatformBuildRequest.Options.Get<T>()` 获取平台专用设置，`Set<T>()` 设置参数；Core 不认识具体平台参数类型。现有 Player Settings 的字段、枚举名称及 YAML 数据保持可读。
- Audio、Physics2D 声明原生执行需求。发布器根据元数据选择实际需要的程序集，不在 Core 或 Platform 写入包名白名单。AOT 输入保留 SHA-256、MVID 及原始字节，运行期继续验证身份。修改冻结的程序集需要重建 Player，这是原生编译的约束。
- Web 完整原生脚本模式继续使用联合裁剪后的规范镜像。移动端及 Web 解释模式缺少联合链接时，原生程序集及其依赖使用同一份规范原始镜像，避免启动与游戏阶段分别裁剪后身份不一致；未标记的游戏代码仍按原有流程处理。
- 原先位于 Core 的数据内核使用完整限定的 `SkipLoopGuard` 标记，保留已有循环执行方式。普通包和工程脚本的 Editor 循环保护不受影响。Player 发布本来就不插入 Editor 循环保护。
- 包编译缓存版本更新为 v16，循环重写版本更新为 v2，避免复用旧程序集归属或旧循环插桩结果。

## 验证

验证目录为 `Temp/Engine/Verification/CoreSlim3_20261106`；Snake 使用独立副本、临时目录和存档，位于 `Temp/Snake/Verification/CoreSlim3_20261106` 与 `Temp/Snake/Temp/CoreSlim3_20261106`。

最终测试、测量及导出记录见该验证目录下的 `FinalVerification.json`。

## 测量与回归结果

- 同一套仓库构建属性、Windows 条件和 Release 配置下，普通构建的 `BEngine.dll` 从 867,328 字节降到 795,136 字节，减少 72,192 字节，约 8.32%；`BEngine.Editor.dll` 从 1,009,152 字节降到 994,816 字节，减少 14,336 字节，约 1.42%。这些是 Core 程序集的变化，默认组合仍包含迁出的实现，不能据此推算整个 Player 的缩小比例。
- 计算基准每进程预热，执行七轮、每轮一万次，迁移前后交错各跑四次。四次中位数的中位数为 235.11 ms / 233.43 ms，基本持平；输出哈希一致，测量循环均为 0 字节分配。此结果不等同于整机帧率或启动耗时的提升。
- 六组机制测试共 150 项检查通过；另通过独立 Core-only 进程及单窗口闪屏呈现验证。覆盖旧类型标识、场景/Prefab/资源引用、克隆、异步加载、注册卸载、Shader 缓存及失效、平台参数、原生镜像、循环保护、过期采样令牌和 100% 进度实际呈现。
- 使用最终测试安装重新验证 Example Editor：两次 Play Mode、300 次场景验证、1776 项操作，错误数为 0。安装后的包源码重编译也已通过。
- 使用同一安装重新构建 Example、Snake 的 Windows / Web Player，四个包均通过。Example 每个平台覆盖 50 个场景、296 项操作；Snake 每个平台覆盖 20 个关卡的进入和返回，本次最长加载分别为 Windows 1.53 秒、Web 4.17 秒。Web 使用本机 Edge 和临时回环 HTTP 服务，存档隔离，原有工程业务逻辑及真实存档未修改。
- 源码与导出程序集边界检查通过：922 个源文件、74 个包程序集身份、21 个引擎程序集，无违规依赖。Android Native 平台层编译通过，零警告、零错误。Windows 环境未进行 iOS 原生构建或移动设备运行验证。

最终 Player 及回归记录位于两个验证根目录的 `Current` 子目录；Core 基准记录为 `KernelLong/Comparison.json`，配置及体积记录为 `FinalSourceMetrics.json`。重新执行 `Test-Final.ps1` 会创建独立结果目录，避免复用 Shader 测试缓存。

程序集归属与编译缓存版本变化会使现有工程在首次打开时重新编译。后续继续使用原有六阶段增量编译机制。
