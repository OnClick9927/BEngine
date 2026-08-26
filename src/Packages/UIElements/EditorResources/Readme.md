# BEngine UIElements

UIElements 是 BEngine 的可选保留模式 UI 包，提供 UIDocument、VisualElement 树、UXML、USS、
UI Builder、HTML Converter、运行时输入与数据控件。UI 使用 2^59 至 2^63 的五个专用层，
永远显示在世界层之上，并可按 Layer/Order 与 UI 层 Particle 穿插。

## 快速开始

1. 打开 Window > General > Package Manager，启用 UIElements。
2. 在 Examples 中导入 Runtime HUD。
3. 打开 Assets/Examples/RuntimeHud/res/UIElements.scene.yaml。
4. 选择 Runtime HUD Document，检查 UIDocument.sourceAsset、Scale With Screen Size、
   Reference 1280×720、UI Sorting Layer 与 Sorting Order。
5. 点击 Play；若 Game 窗口已打开，编辑器会立即聚焦它。
6. 点击 Advance Progress 与 Reset，观察 ProgressBar、TreeView 和 Status。
7. 在 Game 工具栏切换分辨率并展开 Statistics。
8. 停止 Play；运行时 UI 树和组件改值不会保存到 Scene、UXML 或 USS。

## 完整示例

- Runtime HUD：场景直接保存 UIDocument、RuntimeHudController、世界背景、Camera2D、UXML 与 USS。
  Button 更新 ProgressBar、重建 TreeView 并改变状态文字。
- Controls Gallery：场景直接保存 UIDocument、ControlsGalleryBootstrap、Camera2D 和控件资源。
  展示 TextField、SearchField、Toggle、Slider、Dropdown、Color、Foldout、ScrollView 和 Button。

两个示例的 sourceAsset 使用真实导入路径，不是在 Play 时从一个空 Bootstrap 创建全部 UI。

## UIDocument 组件

sourceAsset 指向 UXML/BEngine UI 资源；sortingLayer 和 sortingOrder 控制统一 2D 顺序；
material、atlas 决定渲染与合批；interactable 决定指针派发；scaleMode 可选 ConstantPixelSize 或
ScaleWithScreenSize；referenceWidth/referenceHeight 定义参考分辨率。OnEnable 自动 Reload。

UI 合批与世界一致：Material、Shader、Atlas 决定是否兼容；正确的 Layer、Order、Hierarchy 和
透明顺序优先。UI Document 和 VisualElement 只能使用五个 UI 层。

## 控件

- 文本/输入：Label、TextField、IntegerField、FloatField、SearchField、Toggle、Slider、
  DropdownField、ColorField
- 命令/工具栏：Button、Toolbar、ToolbarButton、ToolbarMenu、ToolbarSearchField、ToolbarSpacer
- 容器/数据：VisualElement、Box、Foldout、ScrollView、ListView、TreeView、TreeViewItem
- 展示：Image、ProgressBar

TreeView 支持展开、选择、刷新、滚动、重命名和拖放，并提供 canRenameItem、validateRename、
canStartDrag、canDrop 策略。ListView/TreeView 都支持 SetSelectionWithoutNotify。

## 编辑器操作

- Assets > Create > UI Toolkit > UI Document：创建 UXML。
- Assets > Create > UI Toolkit > Style Sheet：创建 USS。
- Window > UI Builder：可视化编辑树与样式。
- Tools > UIElements > HTML Converter：转换 HTML。
- Assets > Convert HTML to UIElements：从选中 HTML 转换。
- Add Component > UI Toolkit > UI Document：把 UI 加入场景。

Project 右键与顶部 Assets 菜单共用 MenuItem 注册，因此外部包增加的 UI 资产命令会同时出现。

## 运行时 API

VisualElement 提供 Add/Insert/Remove/Clear、Q<T>、class list、styleSheets、SetEnabled 与 visible。
UIDocument 提供 Reload、SetVisualTree、DispatchPointerDown 和 rootVisualElement。Button 使用 clicked；
字段使用 valueChanged；ListView/TreeView 使用 selectionChanged。

    var root = document.rootVisualElement;
    var progress = root.Q<ProgressBar>("Progress");
    var advance = root.Q<Button>("Advance");
    if (progress is not null && advance is not null)
        advance.clicked += () => progress.value = Math.Min(100, progress.value + 10);

需要避免递归通知时使用 SetValueWithoutNotify。长期事件订阅应在 OnDisable 对称解除。

## UXML 与 USS

UXML 使用 UXML 根、Style src、VisualElement 和内置控件标签；name 用于 Q 查询，class 用于 USS。
USS 当前覆盖尺寸、min-size、margin/padding、flex、align/justify、overflow、font、foreground/
background/border 等 Style 字段。相对 Style 路径以 UXML 所在目录为基准。

## 如何扩展

Runtime asmdef 引用 BEngine.UIElements 后，可继承 VisualElement 或现有控件，以组合方式 Add 子节点，
用 class 暴露样式，用事件暴露语义。通过 UIDocument.SetVisualTree 注入程序化树，或在 UXML 中放
占位 VisualElement，加载后将自定义控件插入。

当前 UXML 解析器公开支持内置控件集合，没有公开的自定义标签注册 API；不要假设未知标签会自动
实例化。编辑器扩展放入 Editor asmdef，可创建 Builder 辅助窗口、转换器或 Asset Preview。
所有 asset 写入只能发生在非 Play 状态。

## 排错

- 空 UI：检查 sourceAsset、UXML 根和 Style src。
- Q 返回 null：检查 name 大小写、类型和 Reload。
- Button 不响应：检查 interactable、sortingOrder、enabled 与 Game 焦点。
- 分辨率溢出：使用 ScaleWithScreenSize、min-size、flex、ScrollView。
- 批次过多：比较 Material、Shader、Atlas 与排序连续性。
- 停止 Play 后状态恢复：这是运行镜像隔离的预期行为。

完整图文流程、组件字段、API、扩展与限制见 EditorResources/Doc/index.html。
