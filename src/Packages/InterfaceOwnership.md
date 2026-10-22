# 接口所有权检查

扩展功能由消费方负责。消费包需要控制另一个包的组件时，在消费包内编写适配器并调用被依赖包现有的公开 API。仅为消费包服务的接口、标记和状态不能加入 Core 或被依赖包。

本次扫描了 Engine/src/Core、Engine/src/Packages、Engine/src/Platform 和 Engine/src/Hosts 中的接口声明与跨程序集引用，并检查其调用位置。结果如下。

| 接口或协议 | 处理与依据 |
| --- | --- |
| IMediaPlayback | 删除接口和临时 Media 包。Audio、Video 删除 Timeline 方法与状态；Timeline 依赖 Audio、Video，提供自己的适配器。 |
| ITimelineMediaPlayable / ITimelineMediaAdapter | 由 Timeline 定义、调用。已有组件通过适配器接入，自定义 Timeline 组件仍可直接实现播放会话接口。 |
| IAotStartupFlow、AotStartupSnapshot、AotStartupPhase | 从 Core 移入 Player；它们只描述 Player 的启动、下载确认和进入游戏流程。 |
| IPlayerWindow/InputContext/RenderContext 及工厂、IPlayerHostOutput、IPlayerNativeLibraryLoader | 协议和对应平台适配都移入 Player。Player 自己组合基础平台 API，基础平台注册不再知道 Player。桌面实现仅编入桌面 Player。 |
| IPlayerStartupResolver、PlayerStartupDescriptor | 移入 Player。启动路径与归档解析适配从 AssetBundles 移入 Player，由 Player 消费 AssetBundles 的公开格式。 |
| PlayerBootstrapManifest、RuntimeMetadataDocument、RuntimePackageReferenceData 及序列化器 | 由 AssetBundles / Core 移入 Player。Player 拥有部署格式、包清单、启动配置及版本兼容处理；AssetBundles 仅提供通用归档读写。 |
| IContentBootstrapper、ActivatedContentRelease、ContentEnvironmentKind | 从 AssetBundles 移入 Player。AssetBundles.Editor 的虚拟资源会话只负责准备资源，Player.Editor 在消费侧适配启动协议。 |
| IEditorFrameCaptureView | 删除标记；宿主已依赖 Diagnostics，可直接识别 FrameDebuggerWindow，不需要 Core 定义帧调试协议。 |
| ISpriteReferenceOwner、ISpriteSourceProvider | 保留。Core 的 ComponentFieldSerializer 和 SpriteSourceRegistry 分别消费它们，属于序列化与渲染扩展入口。 |
| IRenderCameraPolicy / Provider、ISceneRenderContributor2D、IScenePostProcessor2D | 保留。Core 的相机和渲染流程调用这些接口，功能包实现基础引擎的扩展契约。 |
| IRuntimeProjectMetadata / IRuntimeWorkspaceReader / RuntimeWorkspaceReaders | 删除。Core 通过 CreateRuntime 接收自身的 ProjectData、ProjectSettingsData；PlayerWorkspace 解析部署文件并保存 Player 元数据，无需 Core 新增消费包专用接口。 |
| NodeCanvasNode/Connection、TrackEditorKey/Clip/Track/Marker/HeaderAction | 从 Core.Editor 移入 UIElements.Editor，与实际使用这些模型的控件一起维护。Animation、Timeline 通过已有 UIElements.Editor 依赖使用。 |
| PlayerAssetDependencyCollector | 移除该专用类型；原通用实现改为 AssetDependencyCollector。AssetBundleBuilder 公开 CollectDependencies / CollectAllAssets 方法，PlayerBuild 自己校验 Scenes In Build 和 AOT 分区。 |
| IEditorPreview、IAssetPreviewProvider、IAssetMoveHandler、IProjectTemplate | 保留。Core 的预览生命周期、资源面板、移动事务和项目创建流程调用它们。 |
| IEditorTool、IEditorCommandLineTool、IEditorDocumentCommandHandler、IEditorShortcutHandler | 保留。它们定义编辑器的工具、命令与快捷键入口；编辑器基础设施或组合宿主负责分发，没有要求运行时组件实现某个工具的协议。 |
| IGraphicsColorReadback 及 RHI 能力接口 | 保留。它们描述图形后端的读回、呈现和资源能力，输入输出均为 RHI 数据；Diagnostics 通过这些基础图形 API 取图。 |
| Audio、Networking、HotUpdate、AssetBundles、PlayerBuild、TiledMap 等包内接口 | 接口与消费流程已在所属包；没有新增供单个消费包使用的 Core 协议。 |

Timeline 临时播放器使用 HideAndDontSave，不修改原组件的媒体资产和播放游标。结束控制时恢复原组件的播放状态及显示内容，并释放临时对象。销毁导演、静音轨道、失去绑定和目标失效均有清理路径。

验证入口：Validation/TimelineMediaAdapters.cs 验证消费方适配和清理；Validation/PackageHostSharing.cs 验证 Player 协议归属、服务组合和共享程序集；Validation/PackageOwnership.cs 验证部署类型、控件模型的程序集归属及旧构建数据读取；Validation/PackageBoundaries.cs 检查依赖环、项目通配引用、清单声明及 Core 反向引用。

迁移保留类型的命名空间与文件格式，不保留旧 DLL 的二进制类型位置。外部脚本需要引用所属包后重新编译：部署读取从 ProjectWorkspace.OpenRuntime 改用 PlayerWorkspace.OpenRuntime，元数据访问改用 workspace.GetRuntimeMetadata()。普通 Project.yaml 仍可由 Core 的 ProjectWorkspace.OpenRuntime 读取。

## 平台设备与 Player 适配

Player 的 IPlayerInputContext / IPlayerRenderContext 仍由 Player 拥有；具体设备采集、窗口 API 选择、图形设备创建及交换链由 Platform.Desktop 提供通用会话，Player 只适配自身生命周期和退出策略。Core 的输入/显示/渲染设置 SPI 服务于通用引擎能力，业务代码通过 Utility 或兼容静态入口访问。

## UIElements

控件和输入路由由 UIElements 包持有，Scene 不保存 UIDocumentInputRouter。Core 的 IEditorWindowContent 是窗口生命周期实际消费的通用内容接口；包通过 UIElementsEditorWindow 提供控件树。BAsset 的克隆负载与 GetImportedRepresentations 是资产流程消费的通用扩展点，Core 不识别具体 UXML 或样式类型。

原 UI 文本栅格接口改为通用 ITextRasterizer/RasterizedImage；Core 编辑器 IMGUI 和 UIElements 均消费它，Windows 在 Platform 中实现，平台程序集不依赖 UI 包。
