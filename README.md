# BEngine

Rendering backend and GPU UI contracts: [docs/RENDERING_RHI.md](docs/RENDERING_RHI.md).

Codex 编辑器集成的安装、登录与使用方法见 [docs/CODEX.md](docs/CODEX.md)。

BEngine 是一个基于 .NET 9 的轻量 3D 游戏引擎原型。运行时状态使用 Q32.32 定点数，游戏代码采用 Unity 风格组件与生命周期 API；项目设置、编辑器设置和场景数据统一保存为 YAML。

当前版本已经具备独立的工程管理器、Unity 风格编辑器、Play/Stop 工作流、工程脚本编译和独立 Player。架构参考了 [Infernux](https://github.com/ChenlizheMe/Infernux) 的文档/运行时分层、稳定对象身份和 Play/Stop 恢复思路，实现技术栈为 C#/.NET 9 + Silk.NET/OpenGL。

## 快速开始

前置条件：Windows 10/11 和 .NET 9 SDK。

双击根目录的 `BEngine.bat`。每次启动先执行增量构建，再打开工程管理器，源码更新后不会继续运行旧编辑器：

启动构建在隐藏后台运行，不再保留 CMD 窗口；输出写入 `%LOCALAPPDATA%\BEngine\LauncherBuild.log`。编辑器最小化后进入 Windows 托盘，双击托盘图标恢复，右键可打开 `Editor 状态` 或退出。`窗口 > Editor 状态`（`Ctrl+Shift+L`）可查看 Editor、运行时脚本编译、编辑器脚本编译和启动构建日志。

运行时 UI 与编辑器统一使用 `BEngine.UIElements`。通过 `窗口 > UI Builder` 设计 `*.ui.yaml`，场景中的 `UIDocument` 负责加载和渲染；旧 UGUI 包已移除。详见 [UIElements](docs/UIElements.md)。

1. 选择一个工作目录。
2. 目录中已有 `Project.yaml` 时点击 `Open Project`。
3. 新目录填写工程名后点击 `Create Project`。

编辑器不会再默认打开引擎仓库中的固定示例。选中的目录就是工程根目录，场景、脚本、设置、缓存和日志全部写在该目录内。

工程管理器会记住最后一次成功创建或打开的工程。下次启动时会自动填入该目录和工程名；这项全局启动偏好以 YAML 保存到 `%LOCALAPPDATA%\BEngine\LauncherSettings.yaml`。

命令行构建和测试：

```powershell
dotnet restore BEngine.sln
dotnet build BEngine.sln
dotnet test ..\BEP\EngineTests\BEngine.EngineTests.sln
```

工程管理器：

```powershell
./scripts/run-editor.ps1
```

独立 Player 需要显式指定工程目录或 `Project.yaml`：

```powershell
./scripts/run-player.ps1 -ProjectPath ./Examples/Starter
```

## 工程目录

新工程使用下列结构：

```text
MyGame/
|-- Project.yaml
|-- Assets/
|   |-- Editor/
|   |   `-- ProjectMenus.cs
|   |-- Scenes/
|   |   `-- Main.scene.yaml
|   `-- Scripts/
|       `-- Rotator.cs
|-- ProjectSettings/
|   |-- EditorSettings.yaml
|   `-- EditorLayout.yaml
|-- Library/                 # 工程脚本程序集和导入缓存
|-- Logs/                    # 编辑器与脚本编译日志
|-- Temp/                    # 临时构建文件
`-- .gitignore
```

`Library`、`Logs` 和 `Temp` 是工程本地生成内容，默认不会提交到 Git。路径解析会拒绝通过 `..` 或绝对路径把工程数据写到工作目录之外。

## 编辑器

编辑器按 Unity 的常用布局组织：顶部菜单和变换工具栏，左侧 `Hierarchy`，中间 `Scene/Game`，右侧 `Inspector`，底部 `Project/Console`。

- 当前聚焦面板使用青绿色边框和顶部 `Focus` 状态标识
- Inspector 使用 Unity 风格两列属性、标题栏启用开关和组件三点菜单；脚本组件的 `Script` 行可双击打开源码
- 拖动面板之间的分隔条可调整 Hierarchy、Inspector、Project/Console 和 Project 目录树尺寸
- 主窗口位置、大小、最大化状态、面板尺寸和显隐状态自动保存到当前工程
- `Window > Layouts`：立即保存布局或恢复 Unity 风格默认布局
- `Q/W/E/R`：视图、移动、旋转、缩放工具
- Scene 工具栏：Local/Global 坐标、Snap 吸附、快速 Transform 编辑和 Frame
- Scene 工具栏 `Camera`：设置编辑器相机的位置、旋转、FOV、裁剪面、移动速度、Shift 加速倍率、观察灵敏度、背景色、Skybox 和 Grid；当前视口尺寸及宽高比会同步显示
- `F`：聚焦选中对象；`Ctrl+D`：复制；方向键：按吸附值移动；`Delete`：删除
- Scene 视图右键 + `W/A/S/D/Q/E`：移动编辑器相机，`Shift` 加速
- 顶部 Play/Pause/Step：运行、暂停和单步执行
- `GameObject`：创建空对象、Cube、Plane、Light、Camera 和 `Environment > Skybox`
- `Component` / `Add Component`：添加引擎组件或工程脚本
- 双击 Project 中的 `*.scene.yaml`：打开场景
- Hierarchy、Scene、Inspector、Project 和 Console 均提供对应的右键操作

进入 Play 前会生成内存 YAML 快照；Stop 时恢复该快照，运行状态不会污染编辑场景。

Scene Camera 会随编辑器视口尺寸自动更新投影宽高比，其参数和最后观察位置保存在当前工程的 `ProjectSettings/EditorLayout.yaml`，重新打开工程后恢复。

### 天空盒

新工程和 Starter 示例默认包含程序化天空盒，不依赖外部贴图。天空盒使用定点数保存天顶、地平线、地面颜色，以及曝光、太阳强度、太阳尺寸和地平线宽度；方向光决定太阳方向。可在 Skybox 组件的 Inspector 中实时调整，所有字段随场景统一写入 YAML。

Camera 的 `Clear Flags` 支持 `Skybox`、`Solid Color`、`Depth Only` 和 `Don't Clear`。场景没有 Skybox 组件时使用 `RenderSettings.skybox`，将其设为 `null` 可关闭全局天空盒。

### 光照与 GI

- `GameObject > Light` 支持 Directional、Point 和 Spot，最多 8 盏实时光参与同一帧渲染
- Directional Light 支持 Hard Shadow 和 3x3 PCF Soft Shadow；分辨率、距离、强度及 Bias 均可配置
- `Window > Rendering > Lighting` 打开或创建当前场景的 `LightingSettings`，可设置天空/地平线/地面环境光、实时光数量和阴影质量
- Realtime GI 使用轻量单次反弹：附近可贡献 GI 的 MeshRenderer 将直接光和 Emission 作为间接光反射到其他表面，支持距离、强度和逐物体收发开关
- MeshRenderer 提供 Metallic、Smoothness、Emission、Cast/Receive Shadows、Contribute/Receive GI 和 GI Contribution

Light、LightingSettings 和 MeshRenderer 的全部光照参数使用定点数并随场景写入 YAML。当前轻量渲染路径的实时阴影作用于主 Directional Light；Point 和 Spot 提供实时直接光及 GI 能量贡献。

## Unity 风格脚本

工程启动时，`Assets/Scripts/**/*.cs` 会被编译到工程自己的 `Library/ScriptAssemblies/GameScripts.dll`，Editor 和 Player 随后加载该程序集。

```csharp
using BEngine;

namespace Game;

public sealed class MoveForward : MonoBehaviour
{
    public Fix64 speed { get; set; } = 5;

    public override void Update()
    {
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }
}
```

可读写的公开属性会写入 YAML 组件 `fields`。私有字段可以添加 `[SerializeField]`。找不到的脚本会恢复为 `MissingComponent`，原类型名和字段仍会保留。

## 编辑器扩展和 MenuItem

编辑器扩展放在 `Assets/Editor/**/*.cs`，启动时单独编译为 `Library/ScriptAssemblies/GameEditorScripts.dll`。该程序集引用 `BEngine.Editor.dll`；Player 不会加载这两个编辑器程序集。

`MenuItem` 用法与 Unity 一致，菜单路径支持多级目录、验证函数和优先级：

```csharp
using BEngine;
using BEngine.Editor;

public static class LevelTools
{
    [MenuItem("Tools/Level/Frame Selection", false, 100)]
    private static void FrameSelection()
    {
        Debug.Log($"Selected: {Selection.activeGameObject?.name}");
        SceneView.FrameSelected();
    }

    [MenuItem("Tools/Level/Frame Selection", true)]
    private static bool ValidateFrameSelection() => Selection.activeGameObject is not null;
}
```

扩展代码可通过 `Selection.activeGameObject` 读取或修改选择，通过 `EditorSceneManager.activeScene` 获取场景，修改后调用 `EditorSceneManager.MarkSceneDirty()`。

常用 Unity 风格扩展 API 已包含 `EditorWindow`、`Editor`/`CustomEditor`、`SerializedObject`/`SerializedProperty`、`Undo`、`EditorUtility`、`GenericMenu`、`TypeCache`、静态 `AssetDatabase`、`AssetImporter` 和 `AssetPostprocessor`。所有编辑器窗口、Inspector 和 Property Drawer 都通过 `BEngine.UIElements` 构建；序列化属性修改会自动进入 Undo 栈并标记场景为已修改。

```csharp
[CustomEditor(typeof(Rotator))]
public sealed class RotatorEditor : BEngine.Editor.Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        serializedObject.Update();
        var property = serializedObject.FindProperty("degreesPerSecond")!;
        var field = new FloatField(property.displayName);
        field.SetValueWithoutNotify(property.floatValue);
        field.valueChanged += value =>
        {
            property.floatValue = value;
            serializedObject.ApplyModifiedProperties();
        };
        root.Add(field);
        return root;
    }
}

[MenuItem("CONTEXT/Transform/Reset Local Transform")]
private static void ResetTransform(MenuCommand command)
{
    if (command.context is not Transform transform) return;
    Undo.RecordObject(transform, "Reset Transform");
    transform.localPosition = Vector3.zero;
    transform.localEulerAngles = Vector3.zero;
    transform.localScale = Vector3.one;
    EditorUtility.SetDirty(transform);
}
```

## YAML 文件

引擎持久化数据统一使用 YAML：

- `Project.yaml`：工程名、窗口、资源路径、启动场景和固定时间步
- `ProjectSettings/EditorSettings.yaml`：上次打开的场景
- `ProjectSettings/EditorLayout.yaml`：编辑器窗口位置、尺寸和面板布局
- `*.scene.yaml`：对象、层级、Transform、组件和字段

普通对象统一通过 `YamlUtility` 转换，不需要直接依赖 YamlDotNet：

```csharp
using BEngine.Serialization;

var yaml = YamlUtility.Serialize(myObject);       // object -> string
var data = YamlUtility.Deserialize<MyData>(yaml); // string -> object

YamlUtility.Save(myObject, "Data.yaml");
var saved = YamlUtility.Load<MyData>("Data.yaml");
```

`Save` 使用同目录临时文件和原子替换，避免写入中断后留下不完整 YAML。

`YamlProjectSerializer` 属于运行时，只读写 `Project.yaml`；编辑器设置、布局、偏好和 Launcher 状态统一由 `BEngine.Serialization.Editor` 中的 `YamlEditorSerializer` 读写。新工程及默认场景、脚本和程序集定义由 `BEngine.ProjectSystem.Editor.ProjectWorkspaceFactory` 创建，运行时 `ProjectWorkspace` 只负责打开工程、路径校验和目录布局。

.NET 的 `.csproj/.props` 是构建系统内部格式，不属于引擎数据序列化；`global.json` 仅用于选择 .NET 9 SDK。

可直接选择示例工程 [Examples/Starter/Project.yaml](Examples/Starter/Project.yaml) 进行体验。

## 代码结构

| 项目 | 职责 |
| --- | --- |
| `BEngine` | 聚合全部运行时包：Core、YAML、工程系统、渲染、UI、物理、地形、寻路和动画 |
| `BEngine.Editor` | 聚合全部编辑器 API、包编辑器代码、Unity 风格工作流和桌面 UI |
| `BEngine.Launcher` | 创建/打开工程的桌面入口 |
| `BEngine.Player` | 独立游戏运行时 |

包源码仍按 `BEngine.<Package>` / `BEngine.<Package>.Editor` 目录和命名空间组织，但只生成 `BEngine.dll` 与 `BEngine.Editor.dll`。引擎测试位于工程外的 `E:\Project\_BZP\BEP\EngineTests`。

更详细的边界和数据流见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

## 当前边界

这是可运行的轻量内核，不是 Unity 功能量级的成品。当前已包含轻量 3D 物理、地形、AI Navigation 与动画包；尚未包含音频、完整模型/纹理导入、预制体、脚本热重载和发布打包。四个 Gameplay 包的用法见 [docs/GAMEPLAY_PACKAGES.md](docs/GAMEPLAY_PACKAGES.md)。后续扩展仍应遵守两个约束：确定性游戏状态留在定点核心中；所有引擎持久化数据使用带版本号的 YAML 文档。
