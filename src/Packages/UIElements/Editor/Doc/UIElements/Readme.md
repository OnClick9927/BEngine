# BEngine UIElements

UIElements 是 BEngine 的独立保留模式 UI 包（com.bengine.ui-elements），提供 UIDocument、VisualElement 树、UXML、USS、
Event Debugger、运行时输入与数据控件。UI Builder 和 HTML Converter 由各自包提供。UI 使用具有稳定内建身份的 UI Sorting Layer，
并可按 Layer/Order 与同层 Particle 穿插。该 Layer 的自然编号会随工程排序变化，不能写死数字。

## 快速开始

1. 打开 Window > UIElement > Samples，确认内置编辑器工具可用。
2. 从 UIElements Examples 导入 Runtime HUD。
3. 打开 Assets/Examples/RuntimeHud/res/UIElements.scene.yaml。
4. 选择 Runtime HUD Document，检查 UIDocument.visualTreeAsset、Scale With Screen Size、
   Reference 1280×720、UI Sorting Layer 与 Sorting Order。
5. 点击 Play；若 Game 窗口已打开，编辑器会立即聚焦它。
6. 点击 Advance Progress 与 Reset，观察 ProgressBar、TreeView、虚拟化 ListView 和 Status。
7. 在 Game 工具栏切换分辨率并展开 Statistics。
8. 停止 Play；运行时 UI 树和组件改值不会保存到 Scene、UXML 或 USS。

## 完整示例

- Runtime HUD：场景直接保存 UIDocument、RuntimeHudController、世界背景、Camera2D、UXML 与 USS。
  Button 更新 ProgressBar、重建 TreeView/ListView；ClickEvent 冒泡和 schedule.EveryFrames 展示主线程事件/调度。
- Controls Gallery：场景直接保存 UIDocument、ControlsGalleryBootstrap、Camera2D 和控件资源。
  展示 TextField、SearchField、Toggle、Slider、Dropdown、Color、Foldout、ScrollView 和 Button，
  并演示 binding-path/UIBinding、BObservable、Debounce 与可释放订阅。

两个示例的 visualTreeAsset 引用真实导入资源，不是在 Play 时从一个空 Bootstrap 创建全部 UI。

## UIDocument

UIDocument 通过 `visualTreeAsset` 引用 UXML 资源，Inspector 支持拖入和选择资源。
使用 `enabled` 或 `GameObject.SetActive` 控制显示，`sortingLayer` 和 `sortingOrder` 控制绘制及输入顺序。
同一运行环境中的叠加场景共享输入路由；禁用、销毁或失去焦点时取消悬停、捕获和键盘焦点。
窗口分组、模态和导航行为由项目代码实现。`visualTreeChanged` 可用于重建后的事件绑定。

```csharp
var document = gameObject.GetComponent<UIDocument>();
document.visualTreeAsset = BAsset.Load<VisualTreeAsset>("Assets/Art/UI/Inventory.bxml");
document.Reload();
document.enabled = true;
document.sortingOrder = 10;
```

## 控件

- 文本/输入：Label、TextField、IntegerField、FloatField、SearchField、Toggle、Slider、
  DropdownField、ColorField
- 命令/工具栏：Button、Toolbar、ToolbarButton、ToolbarMenu、ToolbarSearchField、ToolbarSpacer
- 容器/数据：VisualElement、Box、Foldout、ScrollView、ListView、TreeView、TreeViewItem
- 展示：Image、ProgressBar

TreeView 支持展开、选择、刷新、滚动、重命名和拖放，并提供 canRenameItem、validateRename、
canStartDrag、canDrop 策略。ListView/TreeView 都支持 SetSelectionWithoutNotify。

## 编辑器操作

UI Builder 底部的 Edit / Preview 切换编辑与交互预览。Preview 使用独立的文档副本，支持按钮、
Toggle、滑块拖动、文本输入和滚轮；退出后恢复编辑内容，不将预览中的值写回 UXML。
预览只执行控件交互，不运行场景脚本。Tab / Shift+Tab 切换控件焦点，空格或回车激活按钮和 Toggle。

| 快捷键 | 操作 |
| --- | --- |
| Ctrl+P / Esc | 切换预览 / 退出预览 |
| Ctrl+Z | 撤销 |
| Ctrl+Y / Ctrl+Shift+Z | 重做 |
| Ctrl+S / Ctrl+Shift+S | 保存 / 另存为 |
| Ctrl+N / Ctrl+O | 新建 / 打开 UI 文档 |
| Ctrl+C / Ctrl+X / Ctrl+V | 复制 / 剪切 / 粘贴元素到选中节点 |
| Ctrl+D / Delete | 创建元素副本 / 删除元素 |
| F2 | 重命名选中元素 |
| Alt+Up / Alt+Down | 调整同级元素顺序 |

编辑文本时，复制、剪切和粘贴作用于文本。撤销历史属于各自的 UI Builder 窗口，恢复元素属性、
层级、样式和选中项；回到最近保存的版本后，文档恢复为 Saved。

ScrollView 的子元素始终受滚动视口及祖先裁剪范围限制，包括绝对定位元素、文字和鼠标命中区域。
嵌套 ScrollView 到达滚动边界后，滚轮会继续传递给外层 ScrollView。

- Assets > Create > UI Toolkit > UI Document：创建 UXML。
- Assets > Create > UI Toolkit > Style Sheet：创建 USS。
- Window > UIElement > UI Builder：可视化编辑树与样式。
- Window > UIElement > Event Debugger：记录事件、回调和传播路径。
- Window > UIElement > HTML Converter：转换 HTML。
- Window > UIElement > Samples：浏览并操作编辑器控件状态样例。
- Assets > Convert HTML to UIElements：从选中 HTML 转换。
- Add Component > UI Toolkit > UI Document：把 UI 加入场景。

Project 右键与顶部 Assets 菜单共用 MenuItem 注册，因此外部包增加的 UI 资产命令会同时出现。

## 编辑器图与轨道画布

Core 编辑器程序集提供 `BEngine.Editor.UIElements.NodeCanvas` 和 `BEngine.Editor.TrackEditorCanvas`。
NodeCanvas 接收节点与连接的数据快照，处理选中、框选、拖动、平移、缩放和上下文操作，具体资产修改与 Undo 由宿主窗口执行；内置 Animator 窗口使用它编辑 Controller。
TrackEditorCanvas 接收轨道、片段、关键帧和标记，提供时间尺、曲线、播放指针、片段移动与修剪；Animation 和 Timeline 窗口共用这个画布。
两个画布都可通过 `EditorWindow.uiElementContainer.SetRootVisualElement(...)` 放入编辑器窗口，宿主应在窗口失焦时结束拖动，并在资源切换后刷新画布内容。

UI Builder 使用独立的 Library、Hierarchy、UIElement Container Viewport 和 Inspector；各面板可滚动。
工具栏支持 New/Open/Save、Undo/Redo、复制、排序和删除，Viewport 分辨率跟随 Game。每个 Builder 窗口拥有独立
WindowId、Model、Controller、文档历史和选择状态，可通过 EditorWindowRegistry 访问指定实例。

## 运行时 API

VisualElement 提供 Add/Insert/Remove/Clear、Q<T>/Query<T>、class list、styleSheets、SetEnabled、visible、
BringToFront/SendToBack、bindingPath 和 schedule。UIDocument 提供 Reload、SetVisualTree、
DispatchPointerDown 和 rootVisualElement。Button 使用 clicked；字段使用 valueChanged；ListView/TreeView
使用 selectionChanged。

    var root = document.rootVisualElement;
    var progress = root.Q<ProgressBar>("Progress");
    var advance = root.Q<Button>("Advance");
    if (progress is not null && advance is not null)
        advance.clicked += () => progress.value = Math.Min(100, progress.value + 10);

需要避免递归通知时使用 SetValueWithoutNotify。长期事件订阅应在 OnDisable 对称解除。

RegisterCallback<TEvent> 支持 TrickleDown 捕获和默认冒泡，StopPropagation/StopImmediatePropagation
控制传播。root.Bind(data) 根据 binding-path 创建双向 UIBinding；外部数据变化后调用 Refresh，销毁时
Dispose。schedule.Execute(action).Every/EveryFrames 始终由 BTaskScheduler 的 Editor/Player Update 驱动。

Event Debugger 通过 UIEventDiagnostics 的只读 traceEmitted 流观察派发，不改变事件顺序；可以按
UIDocument/事件类型/搜索文本过滤，暂停、清空并限制日志容量。每条记录包含目标路径、捕获/目标/冒泡阶段、
处理器和公开事件属性。诊断订阅器异常会被隔离，不会中断运行时 UI。

## UXML 与 USS

UXML 使用 UXML 根、Style src、VisualElement 和内置控件标签；name 用于 Q 查询，class 用于 USS。
USS 当前覆盖尺寸、min-size、margin/padding、flex、align/justify、overflow、font、foreground/
background/border 等 Style 字段。相对 Style 路径以 UXML 所在目录为基准。

## 如何扩展

工程 asmdef 自动引用 BEngine 核心后，可继承 VisualElement 或现有控件，以组合方式 Add 子节点，
用 class 暴露样式，用事件暴露语义。通过 UIDocument.SetVisualTree 注入程序化树，或在 UXML 中放
占位 VisualElement，加载后将自定义控件插入。

当前 UXML 解析器公开支持内置控件集合，没有公开的自定义标签注册 API；不要假设未知标签会自动
实例化。编辑器扩展放入 Editor asmdef，可创建 Builder 辅助窗口、转换器或 Asset Preview。
所有 asset 写入只能发生在非 Play 状态。

## 排错

- 空 UI：检查 visualTreeAsset、UXML 根和 Style src。
- Q 返回 null：检查 name 大小写、类型和 Reload。
- Button 不响应：检查 interactable、sortingOrder、enabled 与 Game 焦点。
- 分辨率溢出：使用 ScaleWithScreenSize、min-size、flex、ScrollView。
- 批次过多：比较 Material、Shader、Atlas 与排序连续性。
- 停止 Play 后状态恢复：这是运行镜像隔离的预期行为。

完整图文流程、组件字段、API、扩展与限制见 Editor/Doc/index.html。
