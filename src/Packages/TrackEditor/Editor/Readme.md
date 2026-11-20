# Track Editor

编辑器专用的轨道画布包，依赖 UIElements.Editor。启用 `com.bengine.track-editor` 后，编辑器程序集可引用 `BEngine.TrackEditor.Editor`；它不会加入 Player 的运行时程序集。

保留 `BEngine.Editor` 下的 `TrackEditorCanvas`、`TrackEditorTrack`、`TrackEditorClip`、`TrackEditorKey`、`TrackEditorMarker` 和 `TrackEditorHeaderAction` API。已有使用方无需修改 using，预编译的扩展程序集需重新编译以引用新程序集。

画布支持时间尺、播放指针、轨道、片段、关键帧、曲线切线和标记，以及选择、拖动、修剪、混合区间和视图交互。使用方通过 `SetContent` 提供内容，订阅事件修改自己的数据；资产、Undo/Redo、播放调度和保存由使用方负责。

通过 `UIElementsEditorWindow` 或 `EditorWindow.uiElementContainer.SetRootVisualElement(...)` 承载画布。宿主窗口应在失焦时结束手势，并在资产切换时更新内容。Animation 和 Timeline 复用此包。
