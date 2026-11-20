# 平台接口与调用入口

业务代码调用 Utility；Utility 通过构造函数注入平台接口；`src/Platform` 中的各平台 Host 提供实现。平台程序集随引擎发布，不作为功能包。平台接口是给实现方和服务注册使用的 SPI，保持 public 是为了允许外部程序集实现，业务代码不直接获取或调用它。

| 能力 | 包提供的调用入口 | 平台接口 | 实现所在程序集 |
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

Core Runtime 仅提供 DI 契约和注册接入；Core Editor 仅提供包描述、版本校验及依赖计划。两者都不引用具体平台、Windows Forms、Silk.NET 或 Veldrid。对象模型、场景和调度位于 Foundation；数学位于 Math；基础宿主服务接口与 PlatformRuntime 位于 `Platform/Abstractions`，窗口和渲染等契约位于 `Platform/Contracts`，Utility 入口仍位于 Foundation；窗口、文件监视、进程执行和 AssemblyLoadContext 实现位于各平台 Host。绘制、控件和 RHI 使用方仍可作为功能包。

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

分发版的编辑器应用由 EditorWorkbench 组合平台服务。通用启动器位于 `Platform/BEngine.Editor.Host`，仅引用同目录的中立 Host.Contracts，按描述文件调用 `IEditorApplicationEntry`，不引用具体包。平台程序集与功能包在 Core Runtime / Core Editor 之后按依赖编译。无原生窗口的工具可组合 DotNet.Editor 适配模块并使用所需的进程、监视和程序集服务。

迁移后的公开参数由 IFileSystem 等 SPI 改为对应 Utility；集成代码需要重新编译。自定义实现仍实现接口，并在宿主中注册。输入法等可选能力只在平台提供实现时可用。

输入、光标、显示、渲染设置和图形设备状态按容器隔离。编辑器 Play Mode、包服务和 Player AOT/热更新阶段通过 ShareBEnginePlatformState 显式共用宿主 Utility，避免重新创建输入状态，或在阶段切换后丢失设备信息。独立宿主仍有独立状态。

`Platform/Contracts` 的 `BEngine.Hosting` 提供窗口、输入、渲染上下文、宿主输出、原生库加载、程序集执行域与启动呈现契约。Silk 设备采集、原生图形窗口选择、OpenGL/Vulkan 创建设备与呈现由平台 Host 实现；.NET 程序集镜像和执行域位于 `Platform/DotNet`。桌面启动 Logo 和进度条绘制在游戏主窗口内，并复用窗口与图形设备进入场景；移动平台和 Web 使用应用内部视图或页面。Player 包负责默认启动组合及发布配套的宿主模板，Application 包只负责游戏框架。

通用诊断的数据、采样接口及分析算法属于 Core，诊断窗口属于 Core Editor。进程/线程采样、远程 TCP 协议实现、卸载通知及 CLR heap 读取分别位于 DotNet / DotNet.Editor；不要求应用安装额外框架。启动画面只在成功绘制并结束呈现后确认进度，不拥有进度计算和动画时长策略。

Android 的平台注册与 AAudio 输出位于 `BEngine.Android`；依赖 Android SDK 的 OpenGL ES 设备、输入采集、Surface、文字栅格化、输入法、无障碍、媒体解码和 APK 资源访问位于 `BEngine.Android.Native`。安全区域包含刘海、系统栏及圆角，并与触摸坐标一起换算到渲染分辨率；UI 是否放大、如何排版由应用决定。APK 挂载前缀、解包筛选、运行时会话创建、启动界面及生命周期策略由应用入口传入或组合，平台实现不固定应用目录布局。

iOS 的平台注册、音频输出与 Metal 设备工厂位于 `BEngine.iOS`，UIKit 窗口、触摸与硬件键盘、手柄、输入法、无障碍和 AVFoundation 媒体解码位于 `BEngine.iOS.Native`；无障碍适配接收 `UIView`，不依赖具体应用视图。设备工厂向上层暴露平台契约，导出的 iOS 运行库包含 Metal 渲染闭包，并排除运行时 Shader 编译器。

Web 视频后端位于 `BEngine.Web/Media`，输入与无障碍位于对应平台项目。`Resources/Web` 保存浏览器 WebGL、音频、HTTP、图像准备、存储、无障碍及文字/视频桥接；平台初始化接收宿主提供的元素和请求回调，不反向导入应用启动脚本。应用启动脚本负责环境下载、内容挂载及帧循环组合，发布时按模块依赖合并平台脚本，保持单个应用载荷。FreeType 与 Brotli 原生实现及生成脚本位于 `Native/Web`，字体静态库、解码器及其许可证随平台能力一起导出。

`BEngine.Platform.DotNet.Editor` 提供 SDK、workload、Android SDK、Java、Xcode 探测和通用 `BinaryDelta`；构建请求、内容布局、裁剪策略和发布流水线由上层构建扩展负责，不进入平台层。

原生移动后端使用 `net10.0-android` / `net10.0-ios` 独立 SDK 项目，源码位于 `Platform/Android/Editor/Native` 和 `Platform/iOS/Editor/Native`，由目标应用构建引用，不加入默认桌面解决方案。导出脚本把这些后端模板复制到 `BuildPlatform`；安装版模板引用 `BuildRuntime/<目标>` 中的 Core DI 与平台适配闭包，不依赖源码树。各平台的 `BEngine.*.Build` 项目与运行时项目同处一个平台目录。

资源构建策略及原生发布参数由分发版的构建扩展提供，平台层只提供设备和操作系统能力。构建扩展可以使用 Core 的导入、文件及进程接口；业务资源命名、玩法与关卡规则不得进入平台实现。

桌面 Player 的 VSync、目标帧率、窗口尺寸/全屏请求在平台帧开始时应用；编辑器 VSync 由平台窗口应用，浮动窗口仍遵循自身禁止 VSync 的配置。当前交换链只支持 VSync 开/关，vSyncCount 大于 0 均启用；尚未增加按 N 次刷新同步、MSAA 或独占全屏/刷新率切换实现。antiAliasing、反射和阴影质量参数已由平台服务保存，是否生效仍取决于相应渲染器能力，不能把设置了参数当作设备支持。SystemInfo 图形属性来自已创建设备，未知能力返回 false/0，不再硬编码支持标志。

验证入口：`Temp/Validation/PlatformUtilities.cs` 检查内存文件系统注入、嵌套容器隔离、引导服务身份、进程/程序集服务和 Core 边界；`PackageBoundaries.cs` 检查完整包与项目依赖图。`PlatformInputRendering.cs` 检查输入边沿、触摸生命周期、容器隔离和运行阶段共享；`PlatformNativeGraphics.cs` 在隐藏窗口中验证 Vulkan/OpenGL 着色器、像素回读、VSync、帧率和分辨率。

Android Host 使用自适应 Surface 帧率申请（非视频 FixedSource），在目标帧率未设置时使用 60，并在应用更改目标帧率后同步 Surface、View 与 Window 的申请。引擎 APK 声明 game category；不通过系统命令改设备或模拟器配置。OpenGL ES 高频绘制/Uniform/状态调用直接 P/Invoke，状态与参数缓存只由同一 GL 上下文管理，文字测量缓存有界。`bengineProfileFrames` Intent 仅显式启用两秒聚合的 Tick、Render、空闲时间与批次数统计，正常启动不开启采样。

## 可替换的呈现能力

平台窗口只负责 OS 窗口、输入、上下文、交换链和呈现时序。场景绘制、启动画面与编辑器界面通过 ISceneRendererFactory、IHostStartupRenderer 和 IEditorFramePresenter 注册。未注册绘图能力的逻辑宿主不创建窗口或 GPU。平台只转发中立的事件和帧数据，不直接创建具体功能实现。

程序集分发描述属于组合层，通用启动器仅解析本地描述并调用 IEditorApplicationEntry。Core 与此目录的源码及生成程序集均由 Development/Tools/Test-PackageBoundaries.ps1 检查；增加平台适配时也必须通过此检查。
