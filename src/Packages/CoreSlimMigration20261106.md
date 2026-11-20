# Core 第二轮精简与实现迁移

日期：2026-11-06。本轮在图形、场景绘制、IMGUI、默认工作台与诊断已拆包的基础上，继续清除 Core 中的工具策略和具体实现。未改动 Example、Snake 的业务源码。

## 职责调整

1. **默认编辑器命令**：Core Editor 的宿主契约改为 `EditorCommandId`，不再声明工作台菜单、组件、相机和资源操作枚举。具体枚举及分派由 EditorWorkbench 提供；窗口文档命令保留在 IMGUI，避免反向依赖工作台。
2. **默认图标策略**：图标常量、资源类别默认图标和组件回退图标迁入 Graphics Editor。Core Editor 只保留注册、查找、缓存和程序集卸载清理。Core 对象类型不再标注内置图标路径；已有自定义图标属性仍有效。
3. **外部 IDE 打开方式**：VS Code、Codium、Visual Studio、Rider 的识别、命令参数和 PATH 查找迁入 IDEIntegration。Core Editor 优先调用已注册集成，否则使用系统文件关联打开。
4. **图形和音频算法**：`GraphicsMeshUploadCache` 归 Graphics；`GraphicsUniformValueCache`、`TimestampedPcmReader` 归 DotNet 平台实现。Core 不再携带对应缓存与 PCM 时间线算法。
5. **运行时与创作工作区**：Core Runtime 的 `RuntimeWorkspace` 只描述运行所需项目数据和路径，打开时不创建目录、不改写引擎版本。`ProjectWorkspace`、脚本程序集清单、`ScriptAssemblyStore` 留在 Core Editor，负责 Library、编译产物和创作目录。
6. **可选诊断实现**：原生内存记录字典、嵌套性能采样栈、调用栈和线程信息收集迁入 Diagnostics。Core 只保留记录器接口、可释放注册、轻量计数和采样入口。记录器卸载后，存活句柄不再保留或调用包实现。
7. **渲染器专属计数**：Sprite 几何复用、裁剪、Quad 生成等七项统计由 Rendering2D 定义。Core 使用固定容量的 `SceneRenderCounters` 数值快照和共享布局描述，不为每帧创建字典或数组；Diagnostics 读取新的通用载荷。
8. **YAML 实现**：新增基础支持程序集 `src/Serialization/BEngine.Serialization.Yaml`，承载 YamlDotNet、定点数转换器、文本计划及 YAML→BDAT 编译。它不是引擎扩展包，Core 不反向引用它。Core 保留二进制读取器、`ITextDataCodec` 和格式中立的序列化元数据。
9. **包创作工具**：源码包模板生成、归档导出与导入事务迁入 EditorWorkbench。Core Editor 保留包解析、依赖、编译、包管理机制、归档接口和协议数据。默认工作台仍提供 Package Manager 和真实归档实现。

## API 与数据迁移

- Runtime 代码使用 `RuntimeWorkspace`；需要创作目录、资产数据库及脚本编译产物的 Editor 代码使用派生的 `ProjectWorkspace`。Application Runtime 已迁移。
- 宿主命令使用 `EditorCommandId.From(command)` 传递；具体命令类型来自 IMGUI 或 EditorWorkbench。命令 ID 按类型和枚举值缓存，调用时不拼接字符串。
- 新代码可使用 `[SerializationIgnore]`、`[SerializationMember(Alias=..., Order=...)]`。默认 YAML 支持同时识别旧的 YamlDotNet 属性。旧场景、Prefab、GUID、Sprite 子资源标识和 YAML/BDAT 数据格式保持不变。
- `YamlUtility` 保留文本、文件原子写入和二进制自动识别入口。自定义 YAML 转换器通过 `RegisterConverter<T>(T)` 注册，由文本实现验证具体接口；泛型参数标注接口及公开方法保留，避免完整裁剪删除通过中立接口注册的转换器能力。没有文本实现时，文本操作明确报告不支持，二进制读取仍可使用。
- 默认 DotNet 平台和 Editor 自动初始化 YAML 支持，并显式共享文本实现及转换器契约，避免 Play/热更新域重复加载造成类型身份冲突。
- 原生内存与 Profiler 的详细记录需要 Diagnostics 或自定义记录器；未安装记录器时入口不建立详细记录。已有采样订阅与启用接口保留。
- 七个旧渲染专属统计属性改由 `RendererCounters` 表达，键由 Rendering2D 定义。仓库内诊断窗口及统计聚合已迁移，JSON 导入导出已覆盖回归。
- 包创作类型移动后保留命名空间和类型名，外部工具须重新编译并声明 EditorWorkbench 依赖。模板生成不再额外依赖 Roslyn，C# 命名空间各段使用合法的逐字标识符形式。
- 包源码编译指纹升级为 `v15`，程序集归属变化后旧缓存失效。六阶段编译顺序及引用指纹驱动的增量跳过机制保持。

## 测量

比较本轮修改前后同配置 Release 普通构建，不使用更早版本或不同 publish 参数混比。

- Core：892,928 → 867,328 字节，减少约 2.87%。
- Core Editor：1,069,568 → 1,009,152 字节，减少约 5.65%。
- Core Runtime 源码：22,587 → 21,780 行；Core Editor：25,564 → 23,595 行。统计只用于描述实现迁出量。
- 未加载 Diagnostics 记录器时，250,000 次原生内存登记/调整/释放，七轮中位耗时 27.1368 → 1.8478 ms，线程分配量 38,000,000 → 0 字节。此结果仅代表该关闭诊断的微基准，不代表整个引擎提速比例。
- 100,000 次缓存命令 ID 查找、100,000 次渲染计数快照均为 0 字节线程分配。
- 九组旧 YAML、Fix64 边界值与元数据案例的文本和编译后 BDAT 与修改前逐字节一致。

这些变化主要减少必载 Core 的职责和关闭诊断时的开销。默认宿主仍包含 YAML 支持；实现迁入其他程序集不能等同于完整默认 Player 包体同比缩小。

## 验证记录

验证根目录：`Temp/Engine/Verification/CoreSlim20261106`。Snake 独立副本、产物和日志位于 `Temp/Snake/Verification/CoreSlim20261106`，中间文件位于 `Temp/Snake/Temp/CoreSlim20261106`。

- Release 解决方案编译：0 警告、0 错误。
- Core 独立二进制宿主：不加载 YAML、图形、窗口和诊断实现，通过。
- 无图形宿主：15 项生命周期、BTask、场景克隆/保存/销毁检查通过。
- 资源回归：21 项旧场景、Prefab、Sprite 子资源 GUID、材质及真实 AB 异步加载检查通过。
- Editor/平台单项：54 项命令、IDE 参数、目录、归档、模板、序列化、诊断卸载、统计 JSON、GPU 缓存、PCM 和图标卸载检查通过。
- 实际导出 Editor：两次 Play Mode、六轮、300 次场景验证、1,776 次交互，0 错误。
- 最新 Windows Player：50 个场景、296 次交互，0 错误；验证使用独立持久数据目录，不写入开发工程或覆盖游戏存档。
- 最新 Web Player：本机 Edge 完成 50 个场景、296 次交互，游戏错误、脚本异常及非预期传输错误均为 0，并检查渲染截图。随包 AB 的可选 `latest.yaml` 探测及生命周期取消的 fetch 单独保留记录，不误计为缺失资源或传输故障。
- Snake Windows Player：20 个关卡逐个加载并返回，最长加载 2,168.796 ms，超过 10 秒的关卡为 0，真实存档未修改。这是本次运行结果，不是拆分前后性能对照。
- Snake Web Player：本机 Edge 验证 20 个关卡逐个加载并返回，最长加载 4,285.600 ms，超过 10 秒的关卡为 0；脚本及网络错误为 0，真实存档未修改。
- Android Native 编译：0 警告、0 错误。iOS 原生运行不在本 Windows 机器的验证范围内。
- 源码及导出程序集边界：932 个源文件、74 个包程序集身份、21 个引擎程序集检查，无 Core/Platform 具体包依赖违规。
- 正式引擎已重新导出至 `Engine/Engine/BEgine/BEgine-0.1.0`，Hub 正常关闭后已重新启动。正式产物再次通过 932 个源文件、74 个包程序集、21 个引擎程序集的边界检查。

最终结果索引：验证根目录中的 `FinalVerification.json`；Windows/Web 的原始结果和日志均保留，测试失败的中间尝试也未覆盖。使用 `-SkipIDEPlugins` 保留现有 IDE 插件，本轮未修改插件实现。

测试过程中发现并修复了导出源码编译时的 Roslyn 隐式引用、Play 域中的 YAML 转换器契约重复加载，以及完整裁剪删除自定义转换器接口的问题；宿主类型身份检查已加入回归，并用真实裁剪后的 Windows Player 验证。测试资源目录按平台精确同步，避免旧 Web 内容混入 Windows 验证。
