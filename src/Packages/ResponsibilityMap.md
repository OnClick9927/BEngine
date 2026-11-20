# Core 与功能包的职责地图

Core 的基础能力按程序集归入 `src/Core/BEngine.Core`、`src/Core/BEngine.Core.Editor` 和 `src/Core/BEngine.Player`，不再作为可安装包。完整模块职责见 [Core/README.md](../Core/README.md)。

```text
Core
├─ Kernel：运行时 DI 与 Editor 包规则
├─ Values：Attributes、Math、BuildCore、Collections、Serialization
├─ Execution：Async、Instrumentation、Foundation、Pooling
├─ Integration：AssetPipeline、Player
└─ Authoring：Compilation、EditorServices

Platform 宿主层（非 package）
├─ Abstractions / Contracts：托管平台与编辑能力契约
├─ DotNet / Desktop / Veldrid：系统服务、桌面窗口、音频和 GPU 适配
├─ Windows / Linux / macOS / Android / iOS / Web：各平台 Host 与对应 Build 模块
├─ Build：各平台发布器的共享实现
├─ 通用 Editor 入口协议及描述驱动的启动器
└─ 原生依赖、桥接资源与目标 SDK 源码导出
```

Core 模块随引擎安装预编译，不持有 `package.yaml`。功能包以 `hostReferences` 引用内置程序集，asmdef 仍参与校验。
功能包和工程代码保持原有程序集名称、资源 GUID 与数据格式。Editor 程序集不进入 Player。

以下列出仍可安装的功能包职责。

Jobs、Reactive、StandardAsset、ArtImport2D、Codex、Cinemachine、Localization、PixelPerfect2D、PostProcessing、PropertyAttributes、ShaderGraph、SpriteShape2D、SpriteSkinning2D、Testing、TiledMap、Timeline 和 VisualScripting 的制作源码已迁至 [CommonPackages](../../CommonPackages/README.md) 工程的 `Assets/Packages`。
下方职责描述仍适用于这些包；它们通过工程内 Package Manager 导出，不再归入 `src/Packages` 或引擎源码解决方案。

## 功能包

```text
编辑与开发工具
├─ EditorWorkbench
│  默认编辑器宿主、停靠和 Hierarchy/Project/Inspector/Scene/Game 等窗口
│  Package Manager/Settings/Preferences/Console 默认界面与工作台图片资源
├─ IMGUI
│  即时 GUI、布局、样式、控件、窗口/Inspector 基类和 GUI 帧呈现
│  解释中立预览描述绘制通用图元，支持额外 UI 预览扩展
├─ NodeEditor
│  通用节点/端口/连接/选择等节点编辑控件，不执行领域图
├─ TrackEditor
│  通用时间尺、轨道、片段、曲线、关键帧与标记编辑控件
├─ Diagnostics
│  性能/内存/远程诊断实现及 Profiler/Frame Debugger 工具
│  诊断协议、鉴权和采样归包；系统 TCP 流通过中立平台能力接入
├─ IDEIntegration
│  开发 IDE 工程、代码导航、调试接入与编辑器集成
├─ Codex
│  编辑器内的助手接入和对应工具界面
└─ Testing
   Edit/Play/Player 测试发现、执行、断言及可选覆盖率
   测试报告/启动/内容路由策略；原生进程、HTTP 监听和默认工具位置通过平台接口接入

资源与绘图
├─ Graphics
│  Texture/Font/Shader/Material/RenderTexture，图片和纹理处理
│  通用绘制命令、连续批次借用/复用/清理、上传/资源缓存、对应格式导入器与构建资源
│  RenderTexture 描述与原生目标生命周期、绑定作用域及逻辑活动目标恢复
│  纹理信息/导入设置、RenderTexture 领域编辑，以及材质/Shader/字体/图像领域预览策略
├─ Rendering2D
│  Camera2D/Sprite/SpriteRenderer/遮罩，场景排序/批处理/裁剪/Gizmos
│  对应场景工具、拾取及领域预览
├─ ArtImport2D
│  PSD/PSB/Aseprite 源格式与稳定生成资源引用
├─ TextureAtlas
│  Runtime：图集数据、格式编解码及运行时使用；Editor：打包、导入、发布编译与编辑工具
├─ ShaderGraph
│  Shader 图数据、编译/变体贡献与节点创作工具
│  通过 Rendering2D 的可选光照表面接口接入 Lighting2D，不直接依赖光照包
├─ Lighting2D
│  2D 光源、遮影及相关渲染贡献者
├─ LineRendering2D
│  线段几何、材质与线渲染组件
├─ PostProcessing
│  后处理 Profile/效果/参数及对应绘制贡献者
├─ PixelPerfect2D
│  像素对齐相机和分辨率策略
└─ UIElements
   保留式 UI、BXML/样式、控件/文本/可访问性/输入与绘制
   UI Builder、HTML/CSS 转换和 SDF 字体领域工具

场景领域功能
├─ Animation
│  动画片段/状态机/混合/控制器和创作工具
├─ Timeline
│  时间线资产、播放、媒体编排和领域轨道工具
├─ Cinemachine
│  2D 虚拟相机、跟随/混合/约束策略
├─ Physics2D
│  刚体/碰撞/接触/过滤/材质与物理调试工具
├─ Navigation2D
│  导航数据、寻路、代理与导航编辑工具
├─ Particles2D
│  粒子仿真、粒子物理集成、几何与渲染
├─ SpriteShape2D
│  精灵形状资产、曲线/轮廓几何和渲染
├─ SpriteSkinning2D
│  骨骼/权重/FFD/IK/约束/重定向及蒙皮绘制
├─ TiledMap
│  瓦片地图、Tilemap 物理集成、绘制和地图创作
├─ Audio
│  音频资源、播放/混音/输出、导入和预览工具
├─ Video
│  视频资源、解码/播放、导入和预览工具
└─ VisualScripting
   游戏逻辑图、变量/事件/条件/动作、执行及领域调试

基础可选能力
├─ AssetBundles
│  AB 格式与增量构建、目录/异步加载、StreamingAssets 和更新/修复
│  不提供某个游戏框架默认流程，不实现通用 Player 平台宿主
│  持有预期内容哈希、长度和准入规则；通用增量 SHA-256 与原生摘要租约由 Platform 提供
├─ Networking
│  网络请求/客户端状态、BTask、超时/内存配额及 HTTP 解析，不负责原生 I/O
├─ Jobs
│  作业、调度、原生集合与编译支持
├─ Reactive
│  响应式值/流与订阅生命周期
├─ TextEditing
│  Unicode 文本选择、光标移动及文本 Undo 的共享逻辑
├─ InputActions
│  输入动作、绑定和对应设置/编辑工具
├─ Localization
│  本地化数据、查找/切换和对应工具
├─ PropertyAttributes
│  属性标记及对应编辑绘制扩展
└─ StandardAsset
   通用样例/便捷资产与组件，不承载底层执行机制

游戏开发框架
└─ Application
   Launcher/GameDomain/GameState/GameService，MVC/Value/State/Pref/事件
   红点/UI/Undo/GameObject 池等业务服务、AOT 游戏流程和模板
   组合引擎与功能包公开能力，不实现平台、编译、发布或 AB 底层能力
   模板默认图片通过共享编辑器资源查询取得，不探测 Core 源码或宿主安装目录
```

## 跨层实现边界证据

Audio 的 waveOut/OpenAL/WebAudio 及设备 PCM 队列已迁至 Platform；包只负责
AudioClip/Source/Listener、格式编解码、混音/DSP、加载策略及能力适配。
Platform 不引用 Audio；无设备时仍静音回退，旧平台工厂通过默认接口方法兼容。
详见 Tools/AudioPlatformOwnership.md。Networking 的原生 Socket/HTTP/WebSocket
和浏览器导入已迁至 Platform.DotNet/Web；包内适配器通过中立宿主接口组合。
请求状态、超时、内存准入、协议解析和 BTask 仍归 Networking。
Networking 的边界由 `Tools/NetworkingPlatformOwnership.md` 记录；网络 I/O 由 Platform 提供，协议和请求状态由 Networking 保留。

Audio 的原生 DSP 已将系统模块加载、符号查询和卸载迁至 Platform.DotNet 的独立
租约；DSP ABI、参数、实例与尾音仍由 Audio 管理。平台不包含任何 DSP 导出名，
实时处理继续使用函数指针，不逐块查询服务。失败释放、真实模块卸载、替换工厂和
Edge WASM 验证见 Tools/NativeLibraryOwnership.md。

Video 的 Media Foundation/COM/D3D11 和 FFmpeg 进程实现已迁至 Platform.DotNet；
Video 保留 Clip/Player、便携帧格式、导入和播放/预算策略，通过中立媒体游标适配。
系统能力、真实解码计数与分配前准入跨接口保留；旧浏览器/移动端委托继续优先。
原生媒体不要求窗口或呈现后端，无窗口导入/构建工具也由 DotNet 注册同一系统解码能力。
验证与 API 说明见 `Tools/VideoPlatformOwnership.md`。

共享图标归属已在本轮迁正；旧标识和卸载行为有独立消费者回归。
没有通过删除 Graphics Runtime 的 Player 品牌资源来“减少混杂”。

IMGUI 的 `BAssetEditor` 已通过中立 Inspector 契约委托领域编辑；纹理的
Sprite/Sampling/Platform Overrides 分组及 RenderTexture 编辑由 Graphics 贡献。
IMGUI 对 Graphics Editor 的工程、asmdef 和包清单依赖同时移除，Graphics 工具保持无界面装载。
共享图片源及其 DI 注册已从 IMGUI 迁入 EditorServices；UIElements 直接使用共享能力，
两个 GUI 保留各自呈现与缓存。三个旧公开类型有包间转发，独立图片消费者不加载任一 GUI
或 Graphics，详见 Tools/EditorImageResourceOwnership.md。
IMGUI 的 AssetPreview 已移除图形领域判断，Graphics 贡献不依赖 GUI 的预览策略；
源文件元数据、类型候选及失效回调由 EditorServices 共享并在程序集退休时清理。
旧 IMGUI 预览图片值类型通过包间转发保持已编译插件兼容；Core 不增加类型转发。
RenderTexture 的通用 Undo 宽高缺口已通过 Core 的中立快照扩展修复；Graphics Editor
贡献尺寸数据，Graphics Runtime 的 Resize 管理目标生命周期。Undo/Redo、值复制、
绑定中恢复、内存预算与贡献者退休的验证见 Tools/ObjectSnapshotExtensions.md。

Graphics 的 RenderBatchBuffer 已接管通用连续区间分组及借用生命周期，Rendering2D
只选择场景顺序并调用公开批次缓冲；正常或异常帧结束都释放命令和批次键中的包类型。
独立绘制生产者不需要加载 Rendering2D/UIElements，也不需要友元访问。
Graphics 已接管 RenderTexture 的资源描述、目标退役、绑定和恢复，Rendering2D
通过公开作用域选择相机目标。动态顶点流及其上传状态也由 Graphics 持有，场景包
只写几何数据和选择复用策略，不再声明 GPU 快照证明。两者均有独立消费者验证。
通用纹理输入路径、图片/BTEX 解码和全 mip 透明度证明归 Graphics；TiledMap 只持有
瓦片缓存、采样和绘制策略。Graphics 显式清理已退役导入程序集的解析回调，独立纹理
消费者不加载场景、瓦片或 UI 实现。验证说明见 Tools/TextureSourceOwnership.md。
领域包对共享编辑实现的访问统一经过公开契约；生命周期、数据所有权与安全公开接口由各包的消费者回归覆盖。
通用 Player 宿主根已迁入 Player 包，AB 只通过 `IPlayerContentWorkflow` 适配内容。
原 AB 公开宿主入口保留为委托门面。资源/日志释放异常不会跳过其他启动租约，
注册归属随内容贡献者退休，而非错误地归属于 Core 包装宿主。
启动日志的写入、捕获订阅和会话清理已迁入 Player 包，AB 仅选择内容相关路径；
独立原生与浏览器消费者使用同一公开日志能力，见 Tools/PlayerLogOwnership.md。
通用 Player 运行元数据已迁至 Core，AB 仅编解码自己的配置节并保留兼容入口；
没有把 AB 设置类型、更新策略或包身份移入 Core，见 Tools/PlayerMetadataOwnership.md。
AB 的格式、索引、加密和内容更新策略仍由 AssetBundles 拥有；平台摘要和通用宿主能力由 Platform/Core 契约提供。
ArtImport2D 已将 Windows 原生 ICC 转换交给 Editor 平台的中立颜色服务；包保留
Photoshop 格式和颜色策略。无窗口消费者、独立 Windows SDK 颜色对照及实际 Editor
验证见 Tools/ArtColorOwnership.md。导入器版本更新以纠正旧 x64 原生结构布局。
内部调用数量本身不等于职责问题数量；依赖图无循环、严格构建和 Runtime/Editor
分离检查与各项所有权验证共同构成本清单的验收依据。

Testing.Editor 已将系统进程、回环 HTTP 监听及默认宿主工具位置移交 Platform，
测试筛选、报告路由、内容响应和 ADB 测试命令仍由 Testing 组合。
独立消费者、真实 Edge、ADB 夹具和实际 Editor/重载验证见 Tools/TestingHostOwnership.md。

UIElements 的 Web FreeType 原生导入、字体句柄及像素写入交给 Platform.Web，
通过中立字体和坐标跨度接口接入；HarfBuzz、双向文本、回退、布局和缓存仍归 UI。
默认 Skia 与显式 FreeType 宿主分别组合，平台不引用 UI 包，见 Tools/WebFontOwnership.md。
