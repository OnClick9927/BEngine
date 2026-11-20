# 动态模块租约与 Audio DSP 的职责

系统动态库的路径解析、加载、符号查询和卸载由 Platform.DotNet 的
`SystemNativeLibraryFactory` 实现。Platform.Contracts 只定义
`IPlatformNativeLibraryFactory` 和可释放的 `IPlatformNativeLibrary`。
平台不持有音频对象、插件 ID、参数，也不解释 `bengine_dsp_*` 符号。

Audio 继续拥有 DSP ABI v1、六个导出名称、格式校验、参数同步、实例和尾音状态。
`AudioDspPlugins.RegisterNative` 的公开入口保持不变，通过运行时服务取得平台租约。
宿主的默认注册和显式 DI 注册均提供该能力，开发者可以替换实现。
DSP 处理仍直接调用已绑定的函数指针；处理每个块时不查询服务或动态库符号。

每次 Load 都返回独立租约。注册拥有一份领域引用，活跃实例也各拥有一份；
注销只禁止新实例，已有实例继续工作，最后一份引用释放时关闭模块。
ABI 不匹配、缺少导出及重复注册都释放本次加载。符号地址是借用值，调用者必须
在释放租约前停止原生调用；它不是可以脱离租约使用的持久句柄。

这与 Core Hosting 的 `INativeLibraryLoader` 不同：后者服务进程期 DllImport
解析及共享缓存；插件有独立、可提前结束的生命周期，不能放进该缓存再假装可以卸载。
Web/iOS 默认平台实现拒绝动态加载，Audio 不再维护操作系统支持名单。
托管 DSP 插件不受这个限制。没有增加 Core 对具体包的引用或类型转发。

验证源码位于 `Development/Tools/Tests/NativeLibraryOwnership`，当前记录位于
`Temp/Engine/Verification/NativeLibraryOwnership_20261114`：

- 使用 MSVC 构建真实 DSP DLL、错误 ABI DLL 和缺失导出 DLL；同一套协议探针分别
  使用上一版和本版引擎运行。包含逐样本对照、参数缩减、独立实例、注册替换、
  失败释放，以及最后一个实例释放前后实际 Windows 模块句柄检查。
- 替换工厂验证 Audio 确实通过中立租约接入。无 Audio/Graphics/Editor 的独立
  平台消费者使用同一能力调用非音频导出，并验证独立租约、重复释放和错误路径。
- 本机 Edge 运行实际 WASM，验证不支持路径以及托管 DSP 的正常处理与释放。
  这不是完整 Snake Web 打包验收。
- Audio Editor、Desktop、Web、Android、iOS 工程严格构建；移动端仅编译。
  另检查公开 API 保留、DLL/PDB 对应、Core/Platform 边界和全部包编译图。

两版暖态 5,000 次 DSP 处理均为 0 B 托管分配。微基准耗时有噪声，不宣称本次
职责迁移带来了帧率或总包体收益；平台契约和实现会增加少量代码。
实际 Editor 和安装文件校验的结果以本轮 Report.md 为准。此项不代表全部包
的语义审查已完成。
