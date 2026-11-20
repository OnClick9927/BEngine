# UIElements

保留模式 UI 包：UIDocument、VisualElement、控件、布局、BXML/样式、输入路由及 RHI 渲染，同时包含 UI Builder、HTML/CSS Converter 和 SDF Font 编辑工具。

复杂文字依赖 Pooling，按字体运行时实例复用逻辑文本段、拼接缓冲与同步栅格提交暂存，重入调用各自持有工作区。归还清除引用并限制保留容量；缓存布局、字形数组和输出像素仍独立拥有，字体运行时销毁时释放池。工作区与字体后端在其拥有者线程使用。

启用 com.bengine.ui-elements 即可使用 UI 工具。运行时程序集为 BEngine.UIElements，编辑器程序集为 BEngine.UIElements.Editor。Core 只保留通用资产和窗口内容接口；通用节点／轨道控件及数据模型分别由 Node Editor、Track Editor 编辑器专用包提供。UI Builder 与 HTML 转换器均由本包的编辑器程序集提供，HTML 解析依赖不会进入运行时程序集。

工具入口统一位于 `Window/UIElement`：UI Builder、HTML Converter、Samples、Event Debugger 和 SDF Font。命令行 `--convert-html <source.html> [destination.bxml]` 也由本包提供。

自定义保留模式编辑器窗口请继承 UIElementsEditorWindow 或 UIElementsEditorWindow<TModel,TController>；IMGUI 窗口继续继承 EditorWindow。

完整交互示例位于仓库 Example/Project，主菜单包含 UI 控件、布局、窗口排序、表单、滚动、页签与进度场景。Examples 页签提供 Runtime HUD 和 Controls Gallery 的导入包。

Controls Gallery 示例还需要启用 Reactive。UI Builder 入门与 HTML Converter 入门示例也可从本包的 Examples 页签导入。

[控件与样式文档](Doc/UIElements/Readme.md)

[滚动列表性能与重绘接口](Doc/ScrollingPerformance.md)：布局快照、可见范围索引、滚轮/触摸边界及大型列表使用方式。

运行时输入与渲染共享按场景缓存的 UIDocument 层级列表；层级变化时重建，显隐与排序每次收集时检查，保留同层同序文档的层级先后关系。稳定场景不再逐帧遍历非 UI 对象或创建文档排序数组。触点有效性按根元素布局、捕获和文档状态变化重新检查，取消事件使用可复用快照，支持回调中移除控件或替换触点。所有处理仍在运行时主线程执行。

[SDF 字体制作、描边及运行时多点触摸](Doc/SdfAndTouch.md)：包含字体资产窗口、UI Builder 样式、触点捕获/取消、Windows Game View 和平台验收边界。

`.bxml`、`.bss` 和 `.bcss` 已注册为 Project 资源类型；双击 `.bxml` 会在 UI Builder 中打开。文件模板由
`Assets/Create/UI Toolkit` 创建，AssetDatabase 重导入会刷新对应的 VisualTreeAsset/StyleSheet。

运行时可访问性由 `UIDocument.GetAccessibilitySnapshot` 提供语义树，内置控件会推断 role、名称、值、
状态、范围和屏幕边界。元素可设置 `accessibilityRole`、`accessibilityName`、
`accessibilityDescription`、`accessibilityValue`、`accessibilityHidden`、`accessibilityLiveRegion`、
`focusable` 和 `tabIndex`；这些字段支持 UXML 与 UI YAML 往返。`PerformAccessibilityAction` 处理聚焦、
激活、增减值，`Announce` 与 live region 产生事件。平台接入通过 `UIAccessibility.bridge` 实现
`IUIAccessibilityBridge`；默认桥接把语义快照交给 `PlatformAccessibility`，Windows UIA、Web ARIA、Android AccessibilityNodeProvider 和 iOS VoiceOver 适配器负责系统呈现，并把动作排回游戏线程。

通用 NodeCanvas 已移至 `com.bengine.node-editor`，支持带 ID、标题和颜色的输入/输出端口，PortConnectionRequested 返回规范化的源节点/端口及目标节点/端口。画布继续支持不带端口的状态机节点；allowEditing=false 时保留导航和选择，禁止移动、连线。公开命名空间保持不变。

TextField.maxLength 默认 -1（无限制），非负值限制 UTF-16 长度，并保留完整字素簇。输入、粘贴、选区替换、程序赋值和绑定赋值均使用该约束；交互式替换只截断插入内容，保留选区外文本。UXML 使用 `max-length`，UI Builder Inspector 可设置长度、占位符、只读、密码和滚动到底。`.ui`、`.bxml` 及 VisualTreeAsset 克隆保留这些文本属性与 maskCharacter。长度限制不代替应用的数据合法性校验。

自定义 UXML 控件通过 UxmlFactoryRegistry.Register 注册，返回的 IDisposable 用于注销，可回收程序集卸载时会自动清理。工厂读取属性并创建新的控件，序列化回调返回自定义属性；name、class、style、binding-path、子控件等由通用序列化器处理。UXML、UI YAML、VisualTreeAsset.Create/Instantiate 与资产克隆均保留自定义状态。推荐带 XML 命名空间的标签，避免跨包重名：

```csharp
public sealed class HealthBar : VisualElement { public int health { get; set; } }
// 在工程/包初始化阶段注册，在生命周期结束时 Dispose。
var registration = UxmlFactoryRegistry.Register<HealthBar>("{Game.UI}HealthBar",
    attrs => new HealthBar { health = int.Parse(attrs.GetValueOrDefault("health", "100")) },
    view => new Dictionary<string, string> { ["health"] = view.health.ToString() });
// <UXML xmlns:game="Game.UI"><game:HealthBar name="hp" health="75" /></UXML>
```

同名标签或同类型的重复注册会报错，不能替换内置控件。工厂自行重建内部子树时设置 serializeChildren=false，避免保存后重复创建子节点。自定义控件自动出现在 UI Builder 的 Project Library 中，可搜索并双击插入。UxmlFactoryOptions 可以设置 DisplayName、Category、Tooltip、DefaultAttributes，或者使用 ShowInLibrary=false 隐藏控件。工厂注销或程序集卸载后，打开的 Library 会刷新。默认属性也会应用通用 name/style/binding 等属性，插入后的控件支持 Undo/Redo 与 UXML/UI YAML 保存。
