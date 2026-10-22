# 包边界

Core 只包含场景、对象、生命周期、数学、BTask、输入、基础渲染/RHI、基础资源与序列化、编辑器窗口/Inspector/Undo。Core 的两个程序集都不引用包程序集或具体平台程序集。平台操作通过 Utility 和注入的接口调用，具体实现位于 Engine/src/Platform，详见 [平台边界](../Platform/PlatformBoundaries.md)。后续候选见 [Core 可拆包功能](CorePackageCandidates.md)。

| 包 | 所有权与依赖 |
| --- | --- |
| UIElements | 控件、布局、样式、UXML/UCSS、UIDocument、UI 渲染和输入、资产导入与调试器、保留模式窗口适配和节点／轨道控件及其数据模型；仅依赖 Core |
| Animation | Animation、Animator、资产、运行时状态、编辑器与预览；编辑器依赖 UIElements |
| ShaderGraph | 2D 无光照图资产、GLSL 生成、RHI 渲染组件；运行时仅依赖 Core，编辑器复用 UIElements 的 NodeCanvas 端口与连线 |
| Timeline | 轨道、播放、媒体编排与窗口；依赖 Animation、Audio、Video，编辑器额外依赖 UIElements；音视频适配器由 Timeline 实现 |
| Coroutines / Reactive | 各自的完整 API 与实现 |
| Pooling | 对象池、租借、预热、借出限额、缓存裁剪、统计及集合池；仅依赖 Core |
| Networking | HTTP/TCP/UDP/WebSocket、接口、客户端、平台传输与注册 |
| AssetBundles | 下载、缓存、校验、归档、资源提供者、构建和设置；运行时依赖 Networking，构建依赖 HotUpdate |
| HotUpdate | 托管代码协议、执行器、释放与代码收集；只依赖 Core |
| Player | Player 生命周期、启动/AOT/内容准备协议、部署元数据及序列化、平台窗口适配、热更新与资源组合、编辑器 Play 接入；依赖 AssetBundles、HotUpdate |
| PlayerBuild | 构建、发布、平台设置与 AOT 模板；依赖 AssetBundles、Player、UIElements（默认 AOT 界面模板） |
| TextureAtlas | 图集类型、YAML、解析与缓存、Importer、构建、预览、Inspector、文件移动 |
| TiledMap | 地图、调色板与编辑器；依赖 TextureAtlas |
| Diagnostics | 运行时采样、编辑器采样、GPU 捕获、预览、Profiler 与 Frame Debugger |
| Audio | 音频组件、输出接口、Windows/OpenAL 实现、预览与导入 |
| Video | 视频导入与播放；依赖 Audio |
| Physics2D / Navigation2D / Cinemachine / PostProcessing | 各自的运行时、编辑器、效果与资源 |
| Particles2D / LineRendering2D / Lighting2D / PixelPerfect2D | 各自组件、模拟、渲染、Shader 与编辑器扩展 |
| UIBuilder / HtmlUIConverter | 各自编辑器工具、设置和外部库；依赖 UIElements.Editor |
| Codex / PropertyAttributes | 各自工具、资源和扩展属性 |

桌面编辑器组合入口位于 Engine/src/Hosts/BEngine.Editor.Host，依赖 PlayerBuild 和 Diagnostics；这两个包在当前桌面宿主中是必需包。Core 编辑器基础库可独立编译。Player 位于 Packages/Player。导出仍提供 BEngine.Editor.exe 与 BEngine.Player.exe，启动器兼容源码构建的 BEngine.Editor.Host.exe。

包通过通用服务贡献、资产预览/移动处理、Sprite 源解析、YAML 转换器和编辑器仪器接口接入。Player 自行读取部署格式，通过 Core 的 CreateRuntime 公共方法创建工作区。卸载时清理包持有的注册。宿主直接使用的包程序集复用默认加载上下文，避免同一类型被加载两次；普通功能包继续使用可回收上下文。

package.yaml 与 asmdef 声明包依赖；hostReferences 仅用于平台宿主程序集，不允许用它绕过包依赖。包的运行时不能依赖编辑器程序集。源码编译包含依赖闭包的程序集引用。

验证：运行 Temp/Validation/PackageBoundaries.cs 检查所有包清单、asmdef、项目依赖环（含通配符）和 Core 反向引用；运行 Temp/Validation/PackageOwnership.cs 检查消费方类型归属和部署兼容性。迁移保留公开类型命名空间；使用旧 API 的脚本须启用并引用所属包后重新编译，部署读取入口见 [接口所有权检查](InterfaceOwnership.md)。AssetImporter 的包标签通过 AssetBundleImporterExtensions 提供，通用扩展设置由 Core 保存。
