# 资产 Inspector 的职责归属

纹理导入设置、纹理信息和 RenderTexture 控件由 Graphics Editor 提供。
IMGUI 不再引用 Graphics Editor；Graphics Editor 也不依赖 IMGUI 或默认工作台，
后台导入、资源构建和 Shader 工具可以继续独立装载。

## 扩展方式

EditorServices 的 `EditorAssetAdapter.Inspector` 可以提供
`IEditorAssetInspector`。包负责资产领域行为，界面宿主通过
`IEditorInspectorGUI` 提供现有控件。没有提供处理的区段继续使用通用
SerializedObject Inspector。`EditorInspectorResult.Changed` 独立表达变更，
不依赖 GUI.changed 在同一帧中是否已经为 true。

Inspector 以最具体的资产类型优先查找，并缓存命中和未命中。
原有 ReadOnly 和 Representation 接口仍只匹配确切类型。
注册时捕获提供者；需要替换提供者时重新 Register。
注册、查询和失效沿用编辑器主线程调用约定。
程序集退出时同时清理注册调用方、适配器实现、资产类型和 Inspector 实现，
并释放派生资产类型的查询缓存，避免持有已经退休的可卸载程序集。

## 数据与兼容

- 不移动任何 Runtime 类型，不改资产数据格式、GUID 或项目设置。
- 保留纹理字段顺序、Sprite 条件字段及导入设置 Apply/Revert 路径。
- RenderTexture 的尺寸仍使用 Resize，参数变更仍记录 Undo 并标脏。
- 旧 `RenderTextureEditor` 类型和 OnInspectorGUI 方法保留为转接入口；
  自动选择现在使用通用 BAssetEditor，并委托给 Graphics 的 Inspector。
- Script Class 信息由 EditorServices 的脚本适配器提供。
- EditorServices 增加公开中立契约，Core 和 Platform 不增加具体包依赖。

## 验证与已知后续项

`Test-AssetInspectorOwnership.ps1` 使用独立进程验证无界面装载、
领域字段、缓冲编辑、Undo/Redo、提供者生命周期和稳定查询分配；
将迁移前后真实 IMGUI 绘制命令逐项比较，覆盖普通纹理、无尺寸纹理、
RenderTexture、脚本和通用资产。Editor 回归另外覆盖正式导入与 Apply/Revert。

迁移前已有一个限制：通用 Undo 恢复 RenderTexture 的可序列化渲染和采样参数，
不会恢复具有 internal setter 的宽高。验证保留新旧快照作为依据；
后续应由 Graphics 通过通用快照扩展恢复资源尺寸，不能在 Core 添加 RenderTexture 分支。

IMGUI 的 AssetPreview 仍包含图形领域预览策略，尚需继续检查归属。
本轮通过不代表整个引擎和全部包已完成语义审查。
