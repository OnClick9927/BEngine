# Core 职责精简：运行配置、部署、存储与诊断

本轮将六类具体策略移出 Core Runtime，保留无界面场景执行、资源解码注册、运行配置与宿主接口。未改动场景、Prefab、资源 GUID 或示例项目的业务逻辑。

## 职责归属

- **脚本源码资产**：`Script`、源码文本解码和原有 `MonoScript` 位于 Core Editor。通过 `RuntimeAssetCodecRegistry` 注册，不在 Core 资源加载器中按 Script 类型分支。
- **用户设置存储**：Core 保留 `PlayerPrefs` API、序列化和 `IPlayerPrefsStorage` 契约。DotNet Platform 提供 `FilePlayerPrefsStorage`，负责文件名、目录与原子替换；Web Platform 使用原来的 localStorage 键。读取不会创建存储目录。
- **原生窗口策略**：菜单鼠标退出、窗口帧调度与帧决策位于 Windows Editor。Core Editor 保留中立窗口契约。
- **渲染统计**：Core 保留通用帧统计与无集合分配的 `SceneRenderCounters`。Rendering2D 定义自身计数器名称、布局和访问扩展；Diagnostics 与平台日志通过通用快照聚合。
- **部署兼容**：部署清单、构建目标清单、随包地址及旧目录读取适配位于 DotNet Platform。Core 的窗口呈现仅接收 `HostPresentationOptions` 与 `BuildTargetDescriptor`，不读取发布目录和清单。
- **项目配置**：完整 `ProjectData`、`ProjectSettingsData`、YAML 文件适配及编译宏校验位于 Core Editor。Core Runtime 仅保留 `RuntimeProjectData`、`RuntimeSettingsData` 和通用校验。编辑器应用设置时刷新运行快照，供 Play Mode 使用。

## API 迁移

- 编辑器仍使用 `ProjectWorkspace.Open` 和 `ProjectRuntimeSettings.LoadAndApply`。作者数据通过 `ToRuntime()` 生成独立快照，不把编译宏、语言和源码目录带入 Player。
- 逻辑宿主使用 `RuntimeWorkspace.CreateRuntime`。旧的未打包项目目录由 Platform 的 `LegacyProjectWorkspace.Open` 读取；Core 不再提供 `OpenRuntime` 文件约定。
- `WindowScenePresentation` 接收解析完成的 `HostPresentationOptions`。平台适配负责从实际部署信息生成它；`IHostRenderContextFactory.Create` 使用中立构建目标描述。
- 自定义 PlayerPrefs 后端实现 `IPlayerPrefsStorage.Identity`、`Read(PlayerPrefsScope)` 和 `Write(PlayerPrefsScope, string)`；身份及作用域变化会使 Core 的读取缓存失效。
- 原先 `SceneRenderStatistics` 中的具体 2D 字段改为 Rendering2D 提供的访问扩展，例如 `GetCoreCollectMilliseconds()`。整数和时间计数器均通过通用快照导出，JSON 保留全部 16 个槽位和数值表示。
- Application 的 Runtime / Editor 源码编译清单显式声明部署契约宿主引用；Portable 构建模板同步更新。Core 和 Platform 均不引用具体包。

## 数据与增量编译

项目 YAML 的既有字段保持不变。旧程序集限定类型名由通用类型解析机制映射到当前拥有者，无需 Core 指向新程序集的类型转发。

运行时元数据升级到文档版本 3，继续读取旧版项目和设置段。新输出不再写入只供编辑器使用的字段；文件哈希、长度、格式与截断校验继续生效。标准测试样本由 727 字节减为 603 字节。

包源码编译指纹升级到 v17，以使程序集归属变化前的缓存失效。后续仍沿用 Core RT → Core Editor → 包 RT → 包 Editor → 项目 RT → 项目 Editor 的依赖及增量编译机制。

## 验证记录

本轮临时安装、验证工程、日志和测量结果位于 `Temp/Engine/Verification/CoreSlim4_20261107`；Snake 验证位于 `Temp/Snake/Verification/CoreSlim4_20261107`，中间文件使用对应的集中 Temp 路径。

最终测试与导出结果详见上述目录中的 `Report.md`、各测试 `Result.json` 和导出日志。保留失败日志，避免把仅编译成功视为已完成实际运行验证。
