# BEngine UIElements

`com.bengine.ui-elements` 是编辑器与运行时共用的保留模式 UI 包。UI 文档保存为工程内的 `*.ui.yaml`，场景使用 `BEngine.UIElements.UIDocument` 加载，运行时布局计算采用 `Fix64`。

## UI Builder

在编辑器打开 `窗口 > UI Builder`。窗口左侧编辑层级，中间显示实时预览，右侧编辑尺寸、Flex、颜色、边距和样式类。保存后文件默认写入 `Assets/UI/New UI.ui.yaml`，在 Project 窗口双击 `*.ui.yaml` 也会直接打开 UI Builder。

## 运行时

```csharp
using BEngine.UIElements;

var uiObject = scene.CreateGameObject("UI Document");
var document = uiObject.AddComponent<UIDocument>();
document.sourceAsset = "Assets/UI/Main.ui.yaml";

var button = document.rootVisualElement.Q<Button>("StartButton");
button!.clicked += StartGame;
```

运行时、Game 窗口和编辑器扩展使用同一组 `VisualElement`、`Label`、`Button`、`TextField`、Flex 样式与 YAML 文档，不再依赖 Canvas、RectTransform 或旧 UGUI 事件系统。
