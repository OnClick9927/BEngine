# Node Editor

编辑器专用的节点画布包，依赖 UIElements.Editor。启用 `com.bengine.node-editor` 后，编辑器程序集可引用 `BEngine.NodeEditor.Editor`；它不会加入 Player 的运行时程序集。

保留原有 API：`BEngine.Editor.UIElements.NodeCanvas`、`NodeCanvasGeometry` 和 `BEngine.Editor.NodeCanvasNode`、`NodeCanvasPort`、`NodeCanvasConnection`。已有使用方无需修改 using，预编译的扩展程序集需重新编译以引用新程序集。

画布接收节点和连接的数据快照，提供端口、连线、选择、框选、拖动、平移、缩放与上下文交互。`PortConnectionRequested` 返回源/目标节点与端口 ID，`allowEditing = false` 保留导航和选择并禁止移动和连线。资产修改、Undo/Redo、保存与运行时图执行由使用方负责。

通过 `UIElementsEditorWindow` 或 `EditorWindow.uiElementContainer.SetRootVisualElement(...)` 承载画布。宿主窗口应在失焦时结束拖动，并在资产切换时更新内容。Animation、Shader Graph 和 Visual Scripting 复用此包。
