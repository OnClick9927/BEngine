# BEngine 2D Showcase

默认场景 `Assets/Scenes/Main.scene.yaml` 展示核心 2D 工作流：

- 世界层级、Order in Layer、Hierarchy 与透明物体排序。
- `Assets/Art/Showcase.atlas.yaml` 中的 Sprite/Particle 图集合批。
- `Assets/Art/Sources/*.png` 直接通过 TextureImporter 的 `Texture Type = Sprite` 使用；示例不创建独立 Sprite YAML。
- 主相机与右上角观察相机的优先级、视口、Clear Mode 和层级筛选。
- Play 模式下的旋转、浮动、双粒子发射器，以及方向键/WASD 控制的导航图标。

Scene 视图初始中心为 `(0, 0)`、Size 为 `6`。修改 Play 中的对象不会写回场景资源。

## 启动与故障提示

从 `Output/BEngine.bat` 打开 Hub 并选择 Example。Hub 会保留到 Editor 完成首帧并报告 Ready；如果托管初始化失败、原生进程崩溃或启动超时，Hub 会重新显示并弹窗说明原因，同时给出本次故障实际使用的日志路径。

直接运行 `Output/BEgine/BEngine.Editor.exe Example/Project.yaml` 时，可捕获的启动异常由 Editor 自己用原生弹窗报告。通常先检查 `Output/EditorData/Logs/EditorBootstrap.log`；Editor 来不及生成该日志的原生崩溃或 Hub 启动超时会记录到 `Output/EditorData/Logs/EditorStartup.log`。

编辑器布局恢复时只恢复页签与逻辑焦点，原生窗口初始化完成前不会调用原生 Focus；实际窗口焦点在首帧之后交接。
