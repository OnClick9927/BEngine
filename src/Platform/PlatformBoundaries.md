# 平台接口与调用入口

业务代码调用 Utility；Utility 通过构造函数注入平台接口；Platform 程序集提供实现。平台接口是给实现方和服务注册使用的 SPI，保持 public 是为了允许外部程序集实现，业务代码不直接获取或调用它。

| 能力 | Core 调用入口 | 平台接口 | 实现所在程序集 |
| --- | --- | --- | --- |
| 文件读写、目录、流、原子替换 | FileUtility | IFileSystem | BEngine.Platform.DotNet |
| 路径规则 | PathUtility | IPathService | BEngine.Platform.DotNet |
| 系统环境、时钟、诊断、运行时数据 | PlatformUtility | IPlatformService | BEngine.Platform.DotNet |
| 应用目录 | ApplicationPathUtility | IApplicationPaths | BEngine.Platform.DotNet |
| 构建目标表与宿主架构推断 | BuildTargetUtility | IBuildTargetCatalog | BEngine.Platform.DotNet |
| 键盘、鼠标、触摸、手柄状态 | Input、InputUtility | IInputService | BEngine.Platform.DotNet；桌面采集在 BEngine.Platform.Desktop |
| 光标状态 | Cursor、CursorUtility | ICursorService | BEngine.Platform.DotNet；原生应用在 BEngine.Platform.Desktop |
| 屏幕状态和分辨率请求 | Screen、DisplayUtility | IDisplayService | BEngine.Platform.DotNet；窗口应用在 BEngine.Platform.Desktop |
| 渲染设置与目标帧率 | QualitySettings、RenderSettingsUtility、Application.targetFrameRate | IRenderSettingsService | BEngine.Platform.DotNet；设备应用在 Desktop / Windows.Editor |
| 后端目录、设备能力、Shader 变体和视口约定 | GraphicsUtility、GraphicsBackendSettings、SystemInfo | IGraphicsPlatformService | BEngine.Platform.DotNet；设备及交换链在 BEngine.Platform.Desktop |
| 输入法 | TextInputUtility | ITextInputService | BEngine.Windows |
| 原生多点触摸窗口 | TouchInputUtility | ITouchInputService、ITouchInputSource | BEngine.Windows；SilkInputSession 每帧转入 Input 状态 |
| 子进程执行和托管 | ProcessUtility、ProcessHostUtility、ProcessHandle | IProcessRunner、IProcessHost、IProcessHandle | BEngine.Platform.DotNet.Editor |
| 等待、线程/进程信息、日志时间、本机提示 | WaitUtility、ExecutionUtility、LogTimestampUtility、HostOutputUtility | 对应 IEditor* 接口 | BEngine.Platform.DotNet.Editor |
| 源文件监视 | SourceMonitorUtility、SourceChangeMonitor | IProjectSourceChangeMonitorFactory、IProjectSourceChangeMonitor | BEngine.Platform.DotNet.Editor |
| 脚本和包程序集加载、卸载 | AssemblyLoadingUtility、AssemblyScope | IAssemblyLoadingService、IAssemblyScope | BEngine.Platform.DotNet.Editor |
| 原生窗口 | NativeWindowUtility、NativeWindow | IEditorNativeWindowFactory、IEditorNativeWindow | BEngine.Windows.Editor |
| 屏幕工作区、原生拖动、菜单、屏幕取色 | DesktopUtility | IEditorDesktopService | BEngine.Windows.Editor |
| 剪贴板、文件对话框 | ClipboardUtility、FileDialogUtility、EditorUtility | IClipboardService、IFileDialogService、IEditorUtilityPlatform | BEngine.Windows.Editor |
| 编辑器图片与文字 | CanvasResourceUtility、EditorTextUtility | IGpuCanvasResourceResolver、IEditorTextRasterizer | BEngine.Windows.Editor |
| 原生 Shader 编译 | ShaderCompilerUtility | IShaderCompiler | BEngine.Windows.Editor |

Core 的两个程序集都以 `net10.0` 编译，不引用具体 Platform 程序集、Windows Forms、Silk.NET 或 Veldrid。窗口、Win32、系统绘图、文件监视、进程执行和 AssemblyLoadContext 实现均位于 Platform。Core 保留平台无关的绘制算法、控件逻辑、数据类型和 RHI 契约。

## 使用方式

```csharp
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;

// 在宿主的组合入口选择具体平台。
var services = new ServiceCollection().AddBEngineWindows();
services.AddSingleton<MyAssetReader>();
using var provider = services.BuildServiceProvider();
var reader = provider.GetRequiredService<MyAssetReader>();

public sealed class MyAssetReader(FileUtility files)
{
    public string Read(string path) => files.ReadAllText(path);
}
```

静态 API 可使用 `FileUtility.Current`，它按当前场景、已附加的运行时容器、平台默认容器解析。优先在可注入类型中使用构造函数注入，避免将 Current 缓存到另一个作用域。`PathUtility`、`PlatformUtility` 等遵循同样的规则。

测试或自定义平台可在服务注册中提供自己的 `IFileSystem`，FileUtility 本身无需修改。接口不包含默认文件读写实现，也不会在找不到服务时偷偷回退到物理磁盘。

编辑器宿主显式初始化 `WindowsEditorPlatform`，再调用 `AddBEngineEditorPlatformServices`。Core 只注册调用入口；具体服务由平台注册贡献提供。无原生窗口的工具可只加载 `BEngine.Platform.DotNet.Editor` 并使用所需的进程、监视和程序集服务。

迁移后的公开参数由 IFileSystem 等 SPI 改为对应 Utility；集成代码需要重新编译。自定义实现仍实现接口，并在宿主中注册。输入法等可选能力只在平台提供实现时可用。

输入、光标、显示、渲染设置和图形设备状态按容器隔离。编辑器 Play Mode、包服务和 Player AOT/热更新阶段通过 ShareBEnginePlatformState 显式共用宿主 Utility，避免重新创建输入状态，或在阶段切换后丢失设备信息。独立宿主仍有独立状态。

Player 包保留自己的 IPlayerInputContext / IPlayerRenderContext 及薄适配器，Escape 关闭游戏等行为由 Player 决定。Silk 设备采集、原生图形窗口选择、OpenGL/Vulkan 创建设备与呈现均在 Platform，不向 Core 增加仅供 Player 调用的协议。

Android 的 AAudio 输出、生命周期暂停/恢复、OpenGL ES 上传与参数绑定、Surface 分辨率上限和实际帧率调度位于 `BEngine.Android` / Android Host。Audio 包通过 `IPlatformAudioOutputFactory` 与可选的 `IPlatformBufferedAudioOutput` 使用输出；不直接调用 AAudio。`AndroidRuntimeSettings` 默认最长渲染边 1440、音频缓冲 240 ms，可由应用在初始化前配置。安全区域包含刘海、系统栏及圆角，并与触摸坐标一起换算到渲染分辨率；UI 是否放大、如何排版由应用决定。

`PlatformAssetBuildPipeline` 只负责独立构建目标 Artifact、验证源文件快照及应用显式平台覆盖。Android / iOS Build 模块注册移动端默认导入策略：Android 纹理上限 1024、iOS 2048，Automatic 普通纹理使用 ETC2、Sprite/UI/Cursor 保留 RGBA32，大于 512 KiB 的音频使用 Streaming。资源的 `platform.Android.*` / `platform.IOS.*` 覆盖在默认策略之后应用；原始 Meta、编辑器 Artifact 和桌面默认行为不变。AOT 主包与 AB 共用该入口。各平台原生发布参数仍由对应 Build provider 负责，业务资源命名、玩法与关卡规则不得进入平台策略。

桌面 Player 的 VSync、目标帧率、窗口尺寸/全屏请求在平台帧开始时应用；编辑器 VSync 由平台窗口应用，浮动窗口仍遵循自身禁止 VSync 的配置。当前交换链只支持 VSync 开/关，vSyncCount 大于 0 均启用；尚未增加按 N 次刷新同步、MSAA 或独占全屏/刷新率切换实现。antiAliasing、反射和阴影质量参数已由平台服务保存，是否生效仍取决于相应渲染器能力，不能把设置了参数当作设备支持。SystemInfo 图形属性来自已创建设备，未知能力返回 false/0，不再硬编码支持标志。

验证入口：`Temp/Validation/PlatformUtilities.cs` 检查内存文件系统注入、嵌套容器隔离、引导服务身份、进程/程序集服务和 Core 边界；`PackageBoundaries.cs` 检查完整包与项目依赖图。`PlatformInputRendering.cs` 检查输入边沿、触摸生命周期、容器隔离和运行阶段共享；`PlatformNativeGraphics.cs` 在隐藏窗口中验证 Vulkan/OpenGL 着色器、像素回读、VSync、帧率和分辨率。

Android Host 使用自适应 Surface 帧率申请（非视频 FixedSource），在目标帧率未设置时使用 60，并在应用更改目标帧率后同步 Surface、View 与 Window 的申请。引擎 APK 声明 game category；不通过系统命令改设备或模拟器配置。OpenGL ES 高频绘制/Uniform/状态调用直接 P/Invoke，状态与参数缓存只由同一 GL 上下文管理，文字测量缓存有界。`bengineProfileFrames` Intent 仅显式启用两秒聚合的 Tick、Render、空闲时间与批次数统计，正常启动不开启采样。
