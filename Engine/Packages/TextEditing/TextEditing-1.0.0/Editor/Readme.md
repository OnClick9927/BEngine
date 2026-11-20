# Text Editing

共享的 Unicode 文本编辑基础包，供 UIElements 与 IMGUI 使用，不依赖窗口或渲染器。

- `TextEditingBuffer`：光标、选择、按字素删除、长度限制与有界 Undo/Redo。
- `TextElementUtility`：Unicode 字素簇边界与列位置计算。

命名空间仍为 `BEngine`，使用显式程序集引用的代码需引用 `BEngine.TextEditing`。输入设备状态与文本输入事件仍由 Core 提供。
