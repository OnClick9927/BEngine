# 本机 Unity 的边界参照

字体边界另核对安装中的 `UnityEngine.TextCoreFontEngineModule`：公开
`UnityEngine.TextCore.LowLevel.FontEngine.LoadFontFace`、`TryGetGlyphIndex` 等入口，
同时存在独立的 TextCoreTextEngineModule。BEngine 将原生字体句柄/栅格化交给平台，
UI 继续持有排版和布局策略；这里仅参考模块与能力入口的分层，不声称采用 Unity
内部的字体库或原生实现。验证证据见 Tools/WebFontOwnership.md。

本轮读取本机 `E:/Program/unity/Editor/2022.3.62f2/Editor/Data`，Unity.exe 产品版本为 `2022.3.62f2c1_92e6e6be66dc`。参照材料为安装中的程序集元数据、BuiltInPackages/package.json 和 asmdef；没有修改 Unity 安装，也没有把元数据检查当作原生实现源码分析。

编辑快照另核对本机 UnityEditor.CoreModule 的 Undo.RecordObject，以及运行模块中
RenderTexture 的 Create/Release。BEngine 的通用快照机制位于 Core Editor，资源特有的
恢复操作由资源包贡献；此处参考编辑机制与资源生命周期的归属，不推断 Unity 的
原生 Undo 快照实现。对应验证见 Tools/ObjectSnapshotExtensions.md。

## 可直接核对的归属

模块程序集 `UnityEngine.CoreModule` 包含 Object、Component、GameObject、Transform，以及 Camera、Texture、Material、Shader、Sprite；`UnityEngine.AssetBundleModule` 包含 AssetBundle；`UnityEngine.IMGUIModule` 包含 GUI/GUILayout。Unity 还携带兼容用的整体程序集，检查记录同时保留了这些类型记录。

`UnityEditor.CoreModule` 包含 AssetDatabase、BuildPipeline、PackageManager.Client、Undo、Selection，以及 EditorWindow/EditorGUILayout。这说明包管理、资产与构建机制不应由某个游戏开发框架包承载；它并不要求 BEngine 将所有窗口界面放回最小 Core。

安装内 `com.unity.modules.assetbundle`、`com.unity.modules.imgui` 分别声明模块身份。`com.unity.shadergraph` 14.0.12 依赖 `com.unity.render-pipelines.core` 14.0.12 和 `com.unity.searcher`；SRP Core、Shader Graph 的 Runtime/Editor/Tests 通过不同 asmdef 分开。`com.unity.ugui` 声明其 UI/IMGUI 模块依赖。功能包使用基础图形和编辑能力属于正常关系，依赖多本身不构成职责混杂。

## 在 BEngine 中如何使用这一参照

```text
Core / Platform
  对象与场景执行、调度、服务机制、通用资源协议
  工程、资产数据库、编译、包管理、通用构建流程
  通用 Player 会话、窗口接入、阶段切换、启动与退出协调
  平台窗口、原生 API、GPU 后端和平台构建适配
功能包
  各自领域的数据、算法、资源编解码与工具
  通过 Core 协议接入，通过公开 API 使用其他领域能力
Application
  基于这些能力的游戏框架、业务开发模板与流程组合
```

BEngine 保留已经确认的最小 Core、Graphics/Rendering2D/IMGUI/EditorServices 拆包方式；Unity 的内置模块不等同于第三方源码包，不能只按目录名字照搬。检查重点是实现是否归属于其能力提供者、扩展点能否由另一个实现接入、数据与生命周期是否有清楚的所有者，以及 Runtime/Editor/Tests 是否分离。

本轮已把 AB 中的通用 Player 执行、桌面窗口与阶段清理迁到 Core，内容包通过 `IPlayerRuntimeContent` 提供资源和内容事务。检查更新、确认和进入游戏的自动验证留在内容策略层。Application 不因这次迁移获得新的引擎机制。

AB 的图形资源专用解码已通过通用资源编解码契约迁回所属包，AB Runtime 不再引用 Graphics / Rendering2D。场景 2D 相机、几何、排序缓存和批次组织已归 Rendering2D；Graphics 保留通用绘制命令和设备贡献者注册，UIElements 通过独立绘制契约工作。

通用命令的连续区间分组、可复用批次及借用清理归 Graphics，场景排序策略仍归
Rendering2D。该边界让非场景绘制者通过公开 API 使用通用批次，而不需要加载场景包。
这基于领域与共享机制的划分，不声称已读取或复现 Unity 的原生批处理算法。

RenderTexture 的资源描述、原生目标复用/退役和绑定作用域归 Graphics；Rendering2D
只选择相机目标、视口和后处理策略。作用域恢复由资源所有者管理，平台仍仅实现
原生 GPU 目标切换，不感知图形包。此处参考资源能力与场景策略的划分，未声称复现
Unity 原生 RenderTexture 的内部生命周期或并发模型。

动态网格的 CPU 流、上传状态和快照证明归 Graphics，场景投影与几何键归
Rendering2D。独立绘制生产者可复用公开的 GraphicsMeshUploadStream；它不需要
场景或 UI 包。此处沿用共享绘图能力与场景策略的边界，不声称实现了 Unity
原生 Mesh 数据上传算法或相同的性能特征。

本机 UnityEngine.CoreModule 的 Texture2D 与 ImageConversionModule 的 ImageConversion
提供通用图片/纹理能力，而非由 Tilemap 工具实现图片解码。BEngine 相应将通用路径读取、
BTEX/图片解码和 alpha 证明统一在 Graphics，TiledMap 保留瓦片策略。此处参考能力归属，
不声称采用 Unity 内部的图片解码器或导入缓存实现。

功能 Editor 对完整默认工作台的 16 处直接依赖已解除：共享资产定位和工程设置迁入 EditorServices；场景工具、拾取与 Gizmo 迁入 Rendering2D Editor。默认窗口、停靠与默认宿主保留在工作台。该方向对应 Unity 共享编辑 API 与上层工具分开的原则，保留 BEngine 最小 Core 的既定边界。

共享绘制采样不再写死场景/UI 阶段；Graphics 只提供通用采样机制，具体阶段由 Rendering2D / UIElements 各自声明。这遵循机制与领域实现分开的原则，不声称与 Unity 内部采样实现相同。

默认 AOT 游戏策略已从 AB 迁至 Application，Core/Platform 通过中立启动控制协议接入。剩余历史友元访问及整体职责核查完成前，不能将整个架构清理标为结束。详细验证记录位于工作区 Temp/Engine/Verification/ResourceCodecBoundary_20261111、GraphicsSceneBoundary_20261111、EditorAuthoringBoundary_20261111 及 RenderDiagnosticBoundary_20261111。

AOT 工作流迁移和无 Application 内容验证见 Temp/Engine/Verification/AotWorkflowBoundary_20261111；公开 API 迁移说明见 Development/Tools/AotWorkflowBoundary.md。

通用独占工程工具会话归 Core Editor，默认工作台只组合工程策略与公开会话 API。
该会话管理工程锁、包激活、Runtime/Editor 初始化和退出清理；不选择游戏框架或创建窗口。
仅清单解析与隔离脚本编译不改变当前 Runtime 包状态。呈现后端按能力请求解析，
无窗口编译和导入不强制提供原生 UI 或 Shader 编译后端。说明见 Development/Tools/ProjectToolSession.md。

TextureAtlas 的发布编译策略与注册归 Editor，Runtime 保留格式编解码与消费。
本机 UnityEditor.CoreModule 的 SpriteAtlasUtility.PackAtlases / PackAllAtlases 也属于
Editor；这是职责边界参考，未照搬其内部实现。编辑器编译贡献随所属 Editor 程序集退役，
数据兼容与独立 Runtime 消费验证见 Development/Tools/AtlasBuildOwnership.md。

本机 UnityEngine.AudioModule 含 AudioClip、AudioSource、AudioSettings，音频作为
独立领域模块是合理边界。BEngine 的 Audio 保留音频领域，原生设备、浏览器接口和
硬件时钟由 Platform 提供；Core 及 Platform 只暴露中立能力，不感知 Audio 包。
此处核对的是本机托管类型元数据，未读取 Unity 原生音频实现或照搬其内部缓冲算法。
Windows 真实设备、Edge WebAudio 与包独立消费者的验证见 Tools/AudioPlatformOwnership.md。

本机 UnityEngine.AudioModule 的 AudioSettings 也公开 GetSpatializerPluginNames 和
SetSpatializerPluginName，音频插件选择属于音频领域。BEngine 保留 Audio DSP 的
领域协议，同时将通用动态模块租约交给平台；这里只核对本机公开托管元数据，
不推断 Unity 私有动态库加载或 DSP ABI 实现。验证见 Tools/NativeLibraryOwnership.md。

本机 UnityEngine.UnityWebRequestModule 的 UnityWebRequest、DownloadHandler、
UploadHandler 分别包含 39、5、4 个原生绑定方法。BEngine 将领域请求和宿主原生
传输区分：Networking 管理请求工作流，Platform 提供 Socket、HTTP、WebSocket
与浏览器能力。本项只借鉴本机托管元数据体现的边界，不声称读取或复现 Unity
原生网络实现。真实回环网络及 Edge 验证见 Tools/NetworkingPlatformOwnership.md。

本机 UnityEngine.VideoModule 的 VideoPlayer、VideoClip 分别包含 88、13 个原生绑定方法。
这里核对的是托管类型元数据，未读取 Unity 原生媒体实现。BEngine 相应将视频领域 API
与平台容器解码分开：Video 保留资源及播放策略，Platform 提供原生媒体游标和外部工具。
真实 MP4 新旧解码对照、实际 Editor 及宿主中立接口验证见 Tools/VideoPlatformOwnership.md。

通用 Player 的宿主组合、窗口执行、验证和退出机制属于引擎执行基础，不能要求
每个替代内容提供者重新实现。BEngine 将该宿主根迁入 Core Player，AB 通过中立的
`IPlayerContentWorkflow` 贡献内容与部署策略，原有公开入口保持为兼容门面。
这里沿用共享执行机制与领域策略分开的原则；不声称已读取 Unity 原生 Player
启动器源码。独立 Core 消费、实际 Edge WASM 和 Editor 验证见 Tools/PlayerHostOwnership.md。

本机 CoreModule XML 同时提供 `Application.logMessageReceived` 和
`logMessageReceivedThreaded`。BEngine 将通用启动日志捕获、写入与释放统一在 Core Player，
内容包仅选择与自身部署格式相关的路径。这里只借鉴引擎日志与内容策略的归属，
不声称读取 Unity 的原生日志写入实现。验证说明见 Tools/PlayerLogOwnership.md。

本机 UnityEditor.CoreModule XML 提供 PlayerSettings.companyName/productName 和
BuildPipeline.BuildPlayer。BEngine 相应将共用的 Player 配置与元数据格式放在 Core，
内容包只解释自身配置节。此处借鉴公共职责边界，不推断 Unity 原生 Player 元数据
格式或实现细节。旧版字节兼容和独立 Core 消费验证见 Tools/PlayerMetadataOwnership.md。

本机 CoreModule XML 公开 Networking.PlayerConnection 的连接状态与消息接口，
ProfilerModule 独立提供性能分析接口。本轮仅借鉴连接能力与诊断领域分开的职责原则，
将 Diagnostics 直接创建的系统 TCP 流迁入 Platform，诊断协议和采样仍由可选包拥有；
不声称复现 Unity 原生连接协议或内部传输实现。验证见 Tools/DiagnosticsStreamOwnership.md。

本机 UnityEditor.CoreModule XML 的 BuildPipeline.BuildPlayer / BuildPlayerOptions / 
WaitForPlayerConnection 由引擎提供通用运行和构建能力。BEngine 的 Testing 包保留
测试协议及流程，系统进程、HTTP 监听和已安装宿主工具查询通过中立平台接口接入；
这里不推断 Unity 测试包的私有协议或原生实现。验证见 Tools/TestingHostOwnership.md。

本机 UnityEditor.CoreModule 的 EditorGUIUtility 公开 IconContent、FindTexture 和 Load
等共享图片查询 API。这里只核对已安装托管元数据，不推断 Unity 私有 GUI 加载实现。
BEngine 将共享图片源与服务注册归 EditorServices，各 GUI 使用中立数据实现自己的呈现。
旧编译扩展、无 GUI 消费与实际 Editor 验证见 Tools/EditorImageResourceOwnership.md。
