# BEngine 与 Unity 的非 3D 功能差距

复核日期：2026-10-10。范围排除纯 3D、多人游戏框架和 Package Manager；iOS 签名/真机流水线及 Burst 编译器延续排除。运行时只支持单线程协作异步，Jobs 不创建工作线程。Shader Graph 继续复用通用 NodeCanvas。
本文件只记录能够从源码和行为验证确认的结果。完整实现、编辑入口、持久化和实际运行证据缺一项时，不标记为 Unity 等价。

## 本轮已实现并验证

| 领域 | 原问题 | 当前结果 | 自动验证 |
| --- | --- | --- | --- |
| 单线程 Jobs | 工作线程实现不符合运行时约束，嵌套 Complete 卡死，collectible 包卸载被保留 | 所有平台统一 BTask 分帧调度、依赖/异常传递、NativeArray 安全、可重入 Complete、Shutdown/退出/卸载清理 | `JOBS_OK`、`JOBS_COLLECTIBLE_UNLOAD_OK`、真实单线程 WebAssembly `WEB_JOBS_SINGLE_THREAD_OK` |
| Unicode 双向文字 | Adlam、阿拉伯数字和中性标点方向判错，缺少完整级别解析 | Unicode 17 UAX #9 嵌入/isolate/括号/弱中性规则，按段落解析、按行重排并显式指定 HarfBuzz 方向 | 官方用例 861,948 项检查、0 失败；Vulkan/OpenGL 复杂文字回归 |
| Tiled 碰撞保真与兼容 | Tileset 三角形被替换成整格；常用图片层/模板/瓦片对象未导入 | 碰撞路径保存、克隆、变换与缓存刷新；staggered、图片层、gid 对象及外部模板/属性覆盖 | `TILED_COLLISION_GEOMETRY_OK`、`TILED_EXTENDED_FORMATS_OK`、`TILEMAP_ENHANCEMENTS_OK` |
| Web 流式媒体 | 长视频音轨被省略，帧率固定 30，容器必须完整下载 | MP4 Range 索引/AAC-LC 分段解码，游标有界 PCM 缓存；真实平均帧率、独立游标和释放 | 真实 Edge `WEB_MEDIA_STREAMING_OK`、`.NET WASM WEB_VIDEO_WASM_INTEROP_OK` |
| CLR 堆分析与窗口 | 缺少 retained-size/dominator、快照差异和大列表缓存 | 支配树/持有大小、类型增长比较、缓存 root path、可取消查询及 128 行分页 | 300 个随机图独立 oracle、百万对象链本机约 96ms；`MANAGED_HEAP_WINDOW_OK` |
| Aseprite 外部 tileset | 外部瓦片像素与依赖修改未支持 | 递归解析、循环拒绝、源依赖哈希与自动重导入 | `ASEPRITE_EXTERNAL_TILESET_OK`、`ART_IMPORT_2D_OK` |
| Shader Graph 跨语言生成 | Direct3D/WebGPU 代码生成直接拒绝 | HLSL/WGSL 参数/纹理绑定、自定义函数、Unlit/Lit2D；引擎对应设备后端仍待实现 | 原生 HLSL 编译、真实 WebGPU 像素；不代表对应 Player 已可用 |
| 运行时媒体与诊断调度 | 音频加载、视频、纹理和平台音频包含工作线程；远程堆采样在 I/O 续体执行 | BTask/帧循环推进、主线程接收 I/O 结果；音频 pending 保留游标，诊断按帧捕获 | Audio/Texture/Video/Diagnostics 回归、`AUDIO_PENDING_PCM_OK`、`VIDEO_ASYNC_OK` |
| Build Profiles | 只有单份全局构建设置，启动场景和脚本符号不能随配置切换 | 命名配置支持创建、复制、重命名、删除、激活；隔离完整构建设置、热更新启动场景和脚本符号 | `BUILD_PROFILES_OK` |
| UI 可访问性 | 没有语义节点、动作、公告、焦点顺序和平台适配协议 | UIDocument 输出语义树并处理动作/live region；UXML/UI YAML 保存元数据；提供 `IUIAccessibilityBridge` | `UI_ACCESSIBILITY_OK` |
| Navigation2D | collectObjects/useGeometry/agentTypeId 多处只是字段，Surface 变换不完整 | 收集过滤、Sprite 几何、Agent Type、area mask、旋转和非均匀缩放均进入实际烘焙/查询 | `NAVIGATION_ENHANCEMENTS_OK` |
| Inspector 属性 | 多行 HelpBox 仍按单行预留，窄面板文字与后续属性重叠 | 按宽度、图标和样式测量高度，布局与绘制共用结果 | `PROPERTY_ATTRIBUTES_ENHANCEMENTS_OK` |
| 多场景音频 | additive scene 各自向设备提交，不能形成一个全局输出块 | 通用整帧系统生命周期聚合所有场景；主音量和限幅在求和后执行，每帧只提交一次 | `AUDIO_MULTI_SCENE_OK`、`AUDIO_ENHANCEMENTS_OK` |
| UI 源资源 | 旧文档声称 BXML/BSS 未注册、双击不能打开 | 核查确认 `.bxml/.bss/.bcss` 已注册，`.bxml` 的 OnOpenAsset 会打开 UI Builder；已修正文档 | 编译与现有 UI 资源回归 |
| 复杂文字 | 旧 SDF 仅按码点排字，没有 shaping、BiDi、fallback、动态字形和彩色 Emoji | SkiaSharp/HarfBuzz 处理 OpenType shaping、字素 fallback 与双向运行；SDF v2 保存源字体/后备链并按需补字，彩色字形走 RGBA 图集 | `UI_ADVANCED_TEXT_GPU_OK`（Vulkan/OpenGL）、`UI_SDF_GPU_OK` |
| 原生可访问性 | 只有 UI 语义协议，没有系统屏幕阅读器适配 | Windows UIA provider、Web ARIA/live region、Android 虚拟 `AccessibilityNodeProvider`、iOS VoiceOver 语义代理均接入同一语义树并将动作排回游戏线程 | `UI_ACCESSIBILITY_OK`；Web/Android/iOS 宿主 Release 编译 |
| Physics2D CCD | 连续碰撞只用刚体中心射线，宽形状和高速旋转会穿透 | 基于实际 Circle/Polygon/Compound 碰撞形状执行 swept SAT；角运动按 10 度分段并对重叠区间二分 TOI | `PHYSICS_CCD_OK`、`PHYSICS_ENHANCEMENTS_OK` |
| 跨平台视频 | 原始容器只在 Windows 可播，其他目标没有统一可发行格式与能力查询 | 新视频默认导入便携 `.bvideo`，Windows 编辑器无 FFmpeg 时由 Media Foundation 转码；便携帧/音频和独立游标适用于所有目标，平台能力可查询；Original Container 保留为 Windows 路径 | `VIDEO_IMPORT_OK`、`VIDEO_ENHANCEMENTS_OK`、双后端 GPU 检查 |
| 高级音频 DSP | Mixer 只有基础增益/滤波/延迟/压缩 | Parametric EQ、失真、Chorus、4096 taps 卷积；托管/静态插件工厂及原生 C ABI v1，独立实例/参数/尾音/生命周期 | `AUDIO_ADVANCED_DSP_OK`、`AUDIO_NATIVE_DSP_OK`、`AUDIO_PLUGIN_OK`、`AUDIO_ENHANCEMENTS_OK` |
| 2D 骨骼约束与渲染 | 只有双骨 IK，没有通用链约束、FFD、GPU 蒙皮及重定向 | 任意链限角 IK、Transform Constraint、FFD、Vulkan/OpenGL GPU 权重变形、参考姿势重定向与 Inspector/Undo | `SPRITE_RETARGET_OK`、`SPRITE_SKINNING_2D_OK`；四骨 CPU/GPU 像素一致性 |
| SpriteShape | 固定采样、无孔洞/角点规则/布尔制作 | 自适应三次细分、共享渲染/碰撞孔洞、角点贴图规则、资产 Inspector 编辑/保存、四种布尔运算按岛生成独立资产 | `SPRITE_SHAPE_2D_OK`；面积/多岛/孔洞/持久化和 Vulkan/OpenGL 角点像素 |
| 美术导入覆盖 | 高位深/预测压缩/蒙版缺失，Aseprite 索引色、组和 tilemap 未处理 | PSD/PSB 8/16/32-bit、Raw/RLE/ZIP/prediction、Lr16/Lr32 和像素蒙版；Aseprite 新旧 palette、19 种混合、linked cel 位置/透明度、组隔离、内嵌 tilemap 与翻转 | `PHOTOSHOP_DEPTH_MASK_OK`、`ASEPRITE_ADVANCED_OK`、`ART_IMPORT_2D_OK` |
| 测试分支覆盖 | 只有语句覆盖，不能区分判断结果 | opt-in 工程源码记录 if/else、循环、switch、条件表达式结果；LCOV/JSON、Test Runner 显示及 Player 报告合并保留分支 | `TEST_BRANCH_COVERAGE_OK`：21/21；真实打包 Player 执行 2 个测试并回传分支 1/2、语句 9/10，并合并至编辑器；异步/迭代器、模式作用域、短路副作用及旧报告兼容 |

## 仍有差距

| 优先级 | 领域 | 与 Unity 相比仍缺少或不完整的能力 | 需要的完成证据 |
| --- | --- | --- | --- |
| P0 | iOS | 当前 Windows 主机只能验证平台程序集和模拟器目标编译；没有 Xcode 签名、真机构建、启动、输入、音频和图形证据 | macOS/Xcode 构建日志、签名产物、iPhone 实机运行日志和画面/输入/音频检查 |
| P1 | Jobs 编译优化 | Job 调度、依赖和安全容器已实现，但 `[BurstCompile]` 仍以 CoreCLR 方法预准备为主，不是类似 Burst 的 LLVM/AOT SIMD 原生编译器 | 独立编译流水线、受限 IL 校验、目标 ISA 代码生成、缓存/诊断和多平台数值/性能验证 |
| P1 | 平台可访问性验收 | Windows provider 与语义协议已验证；新 Web/Android/iOS 适配完成编译，但未完成屏幕阅读器端到端操作验收；Linux/macOS 无系统适配 | Narrator/NVDA、TalkBack、VoiceOver 与浏览器实际焦点/动作/公告检查 |
| P2 | 复杂文字平台验收 | UAX #9 官方一致性已通过；Windows 双后端像素通过，Android/Linux 原生依赖已打包；移动/完整 Web UI 尚待设备验收 | 目标设备 UI 画面、IME 与字体回退回归 |
| P2 | 视频格式与平台验收 | 已有 Windows MF、FFmpeg、Android MediaCodec、iOS AVAssetReader 和 Web MP4/AAC-LC 路径；fragmented MP4/WebM 音轨、HDR/DRM/直播未补齐，移动端实机仍待验收 | 对应格式和各平台媒体生命周期/音画同步验证 |
| P2 | Shader Graph 设备后端 | HLSL/WGSL 生成和独立 GPU 结果已验证；引擎 RHI 尚无 Direct3D11/12/WebGPU 设备实现 | 引擎编辑预览、场景和打包 Player 在这些设备上执行 |
| P2 | Tiled 扩展格式 | 重复图片层、text/capsule 对象、六边形 60/120 度瓦片旋转仍明确拒绝 | 对应真实素材的保存、渲染和碰撞验证 |
| P2 | 2D 骨骼平台和制作深度 | GPU 蒙皮目前为 GLSL Vulkan/OpenGL；其他后端兼容 CPU。已支持 alpha 轮廓网格，重定向基于显式本地参考姿势 | 其他 GPU 后端与大网格性能、真实素材制作验收 |
| P2 | 美术制作语义 | PSD 高位深保留源数据但输出 RGBA8；ICC 已有 Windows CMM。调整层/效果/智能对象/矢量蒙版没有完整重建。Aseprite 外部 tileset 已支持，非方形 diagonal flip 及 slice/user-data 编辑仍有限制 | 真实素材视觉比对及目标编辑语义；不能将读入高位深视为 HDR 编辑全流程 |
| P2 | 测试覆盖率深度 | 已有 opt-in 工程和预编译程序集插桩；更多 C# 语法及发布者强签名密钥场景仍需扩展验收 | 保持原语义的语法回归及带签名程序集验证 |

其他功能的实现和 Player/GPU/设备证据见 `Temp/Validation/Reports/EngineCompletionChecklist.md`。上述未完成项不能因存在 API、菜单、模拟器编译或文档而标记为完成。
