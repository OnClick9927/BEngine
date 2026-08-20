# BEngine Core

核心目录包含四个入口程序集：

- `BEngine`：纯运行时引擎 API，不包含 PackageManager，也不记录任何具体包。
- `BEngine.Editor`：编辑器宿主、扩展 API、PackageManager 和 Codex。
- `BEngine.Launcher`：工程选择与启动器。
- `BEngine.Player`：通用运行时入口，按导出内容装载扩展程序集。

`Resources` 存放引擎运行时默认资源；`EditorResources` 存放编辑器图标、样式和其他编辑器专用资源。离线网页文档位于 `EditorResources/Doc/index.html`，可从 Package Manager 直接打开；AI Skill 统一放在 `EditorResources/Skills/<skill-name>/`。内置 Codex 会提供 Core Skill，并发现当前工程已启用包中的 Skill；它只传递 `SKILL.md` 路径，相关任务才读取正文。核心不是扩展包，因此本目录没有 `package.yaml`。

四个程序集项目目录内只存放 C# 代码和 `.csproj`。窗口图标等资源也不嵌入程序集：Editor/Launcher 从 `EditorResources` 加载，Player 从 `Resources` 加载。

## 2D 世界与渲染层

BEngine 世界只包含 X/Y 坐标与单一旋转角。`Transform` 使用 `Vector2` 位置和缩放，`Camera2D` 使用正交尺寸；Sprite、ParticleSystem2D 与 UIElements 进入同一排序和合批队列。

Sorting Layer 值只能是 `2^1` 到 `2^63`。低 58 层属于世界，最高 5 层固定属于 UI；编辑器只显示指数和层名。最终顺序由 Layer、Order in Layer、Hierarchy、透明度与提交顺序确定，Material、Shader、Atlas 共同决定相邻项目能否合批。

## Texture Atlas

使用 `Assets/Create/2D/Texture Atlas` 创建 `.atlas.yaml`，再从 `Window/2D/Texture Atlas` 添加 PNG 来源并执行 Build。打包器使用确定性的无旋转 MaxRects，输出 2 的整数次方 PNG，并支持 Padding 与 Extrude 防止边缘采样渗色。

`SpriteRenderer` 和 `ParticleSystem2D` 的 `atlas` 填写 `.atlas.yaml` 路径，`sprite` 填写区域名称；两者会使用生成的归一化 UV 和 pivot。未填写 Atlas 时，`sprite` 可直接填写 PNG 路径。相邻项目只有共享 Material、Shader 和 Atlas 才会合批。

`BEngine` 底层采用 `BEngine.Entities` 的混合 ECS：`Scene` 始终拥有 `World`，`GameObject` 持有带版本的瞬态 `Entity`，数据组件存入稠密/稀疏存储，查询只在结构变化时重建。`GameObject`、`Component`、`MonoBehaviour` 是兼容 Unity 风格 API 的 managed facade，同一实体可同时拥有 `IComponentData` 与传统组件。`Entity` 不写入 YAML，持久身份仍使用 `BObject.Id`；反序列化时会重建 World 映射。

运行时系统通过 `ISystem` / `SystemBase` 和 `SimulationSystemGroup` 调度；旧 `ISceneRuntimeSystem` 由适配器继续支持。动画、Navigation2D、Physics2D、UIElements 和场景渲染均从 `Scene.world` 查询组件，不再逐层扫描 `Scene -> GameObject -> Component` 列表。

有状态服务统一使用 `Microsoft.Extensions.DependencyInjection`：`BEngine` 只依赖 DI Abstractions，Editor、Launcher、Player 分别通过 `AddBEngineEditor`、`AddBEngineLauncher`、`AddBEnginePlayer` 建立组合根。项目是 scope，`World` 会再创建独立的场景 scope，ECS 和兼容运行时系统支持构造器注入。扩展包可实现 `IEngineServiceModule` 注册服务；编辑器为每个动态包建立独立 Provider，卸载包前先释放场景 scope 和包 Provider，避免可回收程序集被根容器持有。

`Application`、`Selection`、`AssetDatabase` 等 Unity 风格静态 API 仅作为兼容外观保留。纯数学、定点运算和无状态 YAML 转换不会为了 IoC 而包装成服务。

此外，`BEngine` 提供完整的 SceneRuntime、MonoBehaviour、RuntimeLifecycle、初始化特性和 YAML 序列化回调。`BEngine.Editor` 提供 EditorApplication、EditorWindow、CustomEditor、ObjectFactory、CompilationPipeline、AssemblyReloadEvents 与资源处理器生命周期。

可导入示例位于 `EditorResources/Examples`：`CoreGettingStarted.bpackage` 展示场景、生命周期、输入、渲染和编辑器扩展，`EcsSystems.bpackage` 展示实体、数据组件、查询与系统调度。两者都可在 Package Manager 的 `BEngine Core` 详情中导入。
