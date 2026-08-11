# BEngine 架构

## 分层

```text
Launcher -> ProjectWorkspaceFactory -> ProjectWorkspace -> selected project directory
                                                    |
                                                    +-> Assets/Scripts -> BEngine.dll -> GameScripts.dll
                                                    +-> Assets/Editor  -> BEngine.Editor.dll -> GameEditorScripts.dll
                                                    +-> YAML documents -> BEngine.dll
                                                                            |
Game scripts -> BEngine.dll -> Fix64 world                                   |
                       |                                                     |
                       +-> Rendering namespace ------------------------------+-> Editor / Player
```

`BEngine.dll` 包含全部运行时功能，不引用 `BEngine.Editor.dll`、WinForms 或编辑器 API。游戏世界的坐标、旋转、缩放、时间步和脚本数值使用 `Fix64`。渲染器在提交 GPU 之前才将定点状态转换为 `System.Numerics` 浮点矩阵；浮点值不回写游戏世界。

包使用 `BEngine.<Package>` / `BEngine.<Package>.Editor` 命名空间配对，但运行时源码统一编入 `BEngine.dll`，编辑器源码统一编入 `BEngine.Editor.dll`。完整约定见 [程序集与包边界](ASSEMBLIES.md)。

## 工程边界

`BEngine.Launcher` 通过 `BEngine.ProjectSystem.Editor.ProjectWorkspaceFactory` 创建工作目录，或通过 `ProjectWorkspace.Open` 打开已有工程，并把该工程的 `Project.yaml` 显式传给 Editor。Editor 和 Player 都不会回退到引擎仓库内的默认工程。

运行时 `ProjectWorkspace` 是所有工程路径的唯一入口，只负责打开工程、验证相对路径不能越过工程根目录并维护目录布局。默认场景、脚本、编辑器脚本和程序集定义等创建逻辑只存在于 `BEngine.ProjectSystem.Editor`：

- `Assets`：场景、脚本和后续资源
- `ProjectSettings`：编辑器等工程级 YAML 设置
- `Library`：可重新生成的工程缓存和脚本程序集
- `Logs`：工程运行与编译日志
- `Temp`：可重新生成的临时构建目录

Editor 在窗口创建前把当前目录切换到工程根目录。主窗口、UIElements 面板和 Dock 布局通过 `ProjectSettings/EditorLayout.yaml` 保存；工程目录之外只保留引擎源码和构建产物。

## 工程脚本

`BEngine.ProjectSystem` 命名空间中的编译器扫描 `Assets/Scripts/**/*.cs`，在 `Temp/ScriptBuild` 生成内部 .NET 9 构建项目，将结果输出为 `Library/ScriptAssemblies/GameScripts.dll`，再由 Editor 或 Player 加载。场景 YAML 中保存完整组件类型名，序列化层会从已加载程序集解析工程脚本。

Editor 随后单独扫描 `Assets/Editor/**/*.cs`，生成 `GameEditorScripts.dll`。该程序集引用 `BEngine.Editor.dll` 和可选的 `GameScripts.dll`，用于 `[MenuItem]`、`Selection`、`EditorSceneManager`、`SceneView` 等编辑器扩展。Player 只执行第一阶段，不引用或加载 `BEngine.Editor.dll` 和 GameEditorScripts。

## 光照管线

`BEngine.Rendering` 使用统一 Forward Lit 通道处理 Directional、Point 和 Spot Light。主方向光先渲染深度 Shadow Map，Lit Shader 再合成直接光、Hard/PCF Soft Shadow、三色环境光、Emission 与实时单次反弹 GI。GI 从附近 MeshRenderer 的反射颜色和发光能量生成低频间接光，Light 与 LightingSettings 参数由 Core 定义并通过场景 YAML 同时供 Editor 和 Player 使用。

MenuItem 注册器在两个工程程序集加载后扫描静态方法，将路径树合并进内置菜单。验证方法必须为 `static bool`，执行方法必须为 `static void`；两者均可无参数或接收一个 `MenuCommand`。`CONTEXT/类型/命令` 路径进入对应对象的右键菜单，`MenuCommand.context` 指向被点击对象。扩展异常会进入 Console，而不会终止编辑器主循环。

`CustomEditor` 由 Inspector 自动发现。自定义 Editor 通过 `SerializedObject` 和 `SerializedProperty` 编辑公开字段、公开属性或 `[SerializeField]` 字段，修改进入统一的 Undo/Redo 栈并调用 `SetDirty`。静态 Editor `AssetDatabase` 通过 `IEditorHost` 连接工程级 `.meta` 与 `Library/AssetDatabase.yaml`，资源变动继续分发到 `projectChanged`、EditorWindow 生命周期回调和 `AssetPostprocessor`。

## 编辑器交互

编辑器维护唯一的活动 Scene 和 Selection，并通过 `IEditorHost` 桥接给 `BEngine.Editor` 命名空间。Hierarchy、Scene、Inspector 和资源操作最终都通过同一组选择、修改和 `MarkSceneDirty` 边界，因此自定义菜单与内置操作具有一致行为。

每个主面板由 retained UIElement 树保存状态，并由 DockWorkspace 管理焦点、关闭、拖拽和浮动窗口。Scene 快捷键只在编辑器没有文本输入时执行，避免 `W/E/R/Delete` 误操作字段。

Inspector 和 YAML 序列化共同使用 `ComponentFieldSerializer.GetSerializableMembers`，因此公开字段、公开属性和 `[SerializeField]` 私有字段保持一致。脚本组件通过 `ProjectScriptSourceLocator` 解析回 `Assets/Scripts` 或 `Assets/Editor` 中的源码；优先采用 Unity 的类型名/文件名约定，多类型文件则回退到类型声明匹配。

主窗口使用系统可缩放边框。编辑器工作区通过三条主分隔线和一条 Project 内部分隔线调整尺寸；布局变更采用短延迟自动保存，并在关闭窗口时强制落盘。启动时先读取窗口位置、普通状态尺寸、最大化状态、面板宽高和显隐状态，再创建窗口，因此不会出现启动后被固定尺寸覆盖的问题。`Window > Layouts` 还提供显式保存和恢复默认布局入口。

## 定点数

`Fix64` 使用有符号 Q32.32：

- 高 32 位为整数部分，低 32 位为小数部分
- 乘法和除法使用 `Int128` 中间值
- `Sqrt` 使用整数 Newton 迭代
- `Sin/Cos` 使用固定项数的定点多项式
- YAML 文本使用完整 `decimal` 精度，测试覆盖原始位往返一致性

确定性仍要求不同平台采用相同的脚本执行顺序、输入帧和场景版本。GPU 渲染结果本身不参与确定性模拟。

## 对象和生命周期

每个 `Scene`、`GameObject` 和 `Component` 都有稳定 GUID。`GameObject` 始终拥有一个且仅一个 `Transform`。

运行顺序：

```text
SceneRuntime.Start
  -> Awake (每个行为一次)
  -> Start (每个行为一次)

SceneRuntime.Tick
  -> 0..N FixedUpdate (固定步长累积器)
  -> Update (每个渲染帧一次)
```

运行中新增的脚本会在下一帧依次收到 `Awake` 和 `Start`。禁用组件或非激活层级中的组件不会收到更新。

## YAML 场景

场景文档包含：

- `format` 和 `version`，用于显式迁移
- Scene/GameObject/Component GUID
- 父对象 GUID，而不是对象内存引用
- Transform 定点值
- 组件完整类型名、启用状态和可序列化字段

加载采用两阶段流程：先创建全部对象和组件，再解析父子关系。因此 YAML 中对象顺序不会限制父子引用。找不到脚本类型时使用 `MissingComponent` 保存原始类型名和字段，下一次保存不会破坏数据。

场景保存先写同目录临时文件，再原子替换目标文件，避免中断时留下半份场景。

## 编辑器 Play/Stop

进入 Play 前，编辑器把当前内存场景序列化为 YAML 快照并记录选择对象 GUID。Stop 时丢弃运行态 Scene，从快照重新加载，再按 GUID 恢复选择。这一事务边界与 Infernux 强调的文档/运行时所有权分离一致。

## 扩展方向

已完成的运行时扩展：

- `ISceneRuntimeSystem` 包生命周期和按包启停
- `BEngine.Physics3D` 定点碰撞世界
- `BEngine.Terrain` YAML 高度图、渲染与碰撞
- `BEngine.Navigation` Surface 烘焙和 A* Agent
- `BEngine.Animation` 定点曲线、Clip 和 Controller

推荐按以下顺序继续：

1. 材质、纹理和 glTF 网格导入
2. Prefab YAML 与场景实例覆盖
3. 脚本程序集热重载和字段迁移
4. 音频与发布打包

任何新增数据格式都应包含稳定 GUID、`format`、`version`，并通过 YAML 文档模型跨越编辑器/运行时边界。
