# BEngine 默认 2D 示例

打开 `Project.yaml` 后，编辑器会加载 `Assets/Scenes/Main.scene.yaml`。这个场景是一个可以直接运行和拆解学习的最小 2D 工程，展示资源导入、Sprite 图集、渲染层、相机、粒子、脚本生命周期和输入。

## 直接运行

1. 通过 Hub 打开本目录下的 `Project.yaml`。
2. 等待脚本编译和资源导入完成。
3. 点击 Play，Game 窗口会立即获得一次焦点。
4. 使用 `WASD` 或方向键移动右下角的 Navigation 图标。
5. 停止 Play。Player 位置、旋转动画和粒子状态会立即恢复到播放前状态，运行时数据不会写回场景资源。

如果工程启动失败，Hub 或 Editor 会弹窗显示原因。详细日志位于 `Output/EditorData/Logs`。

## 场景结构

- `Environment`：Background 层上的纯色 SpriteRenderer，组成背景、地面和左右装饰。
- `Atlas Showcase`：Gameplay 层上的四张功能卡片、BEngine 标志和可移动 Player。
- `Foreground Effects`：Foreground 层上的两个 ParticleSystem2D，共用 Spark 图集区域。
- `Inset Camera Content`：只属于 Inset Only 层的独立内容。
- `Main Camera`：渲染 Background、Gameplay 和 Foreground 三层。
- `Inset Camera`：只渲染 Inset Only 层，并通过 `Viewport Rect` 叠加在右上角。

Hierarchy 名称直接说明对象用途。选中任意图标即可在 Inspector 中查看 Sprite、Sorting Layer、Order in Layer 和脚本参数。

## Sprite 与 Texture Atlas

`Assets/Art/Sources` 中的 PNG 由 TextureImporter 以 `Texture Type = Sprite` 导入。每张 Texture 会产生一个 Sprite SubAsset：

- `BEngine.png`：中心品牌标志。
- `Animation.png`：左上角动画图标。
- `Physics2D.png`：右上角物理图标。
- `Navigation2D.png`：下方导航图标和 Player。
- `Spark.png`：粒子贴图。

`Assets/Art/Showcase.atlas.yaml` 保存这些 Sprite 的 GUID 与 Local Identifier。SpriteRenderer 仍然只引用 Sprite，不引用 TextureAtlas；编辑器把生成纹理作为图集的子资源写入 `Library/Artifacts`，运行时会根据图集中的 Sprite 引用自动选择对应纹理和 UV，因此同一图集、Material 与 Shader 的对象可以合批。

在 Project 中选中 `Showcase.atlas` 可以查看和编辑 Sources。添加或删除 Sprite 后重新 Build，即可更新图集 PNG SubAsset。

## 示例脚本

`Assets/Scripts/ShowcaseMotion.cs` 展示 `Start`、`Update`、公开 Inspector 字段、正弦位移和旋转。不同图标使用相同组件，只修改振幅、速度、相位和旋转速度。

`Assets/Scripts/PlayerMover.cs` 展示：

- `Input.GetKey` 读取 WASD 与方向键。
- 归一化斜向输入，保证各方向速度一致。
- 使用 `Time.deltaTime` 进行帧率无关移动。
- 通过 `moveBounds` 将 Player 保持在相机可见范围内。
- `Reset` 为组件提供稳定的默认值，并明确关闭 Edit Mode 执行。

脚本由 `Assets/Scripts/Game.asmdef.yaml` 归入 `Game` 程序集。编辑器扩展示例位于 `Assets/Editor/ProjectMenus.cs`，会在 `Tools/Showcase` 下增加一个带验证方法的菜单项。

## 推荐练习

1. 修改 `ShowcaseMotion` 的公开字段，观察 Inspector 和 Play 镜像中的变化。
2. 将一个图标切换到其他 Sorting Layer，再调整 Camera 的 Culling Mask。
3. 修改 SpriteRenderer 的 Order in Layer，观察卡片背景与图标的前后关系。
4. 在 `Showcase.atlas` 的 Sources 中暂时移除一个 Sprite 并重新 Build，观察它从图集合批退回原始 Texture。
5. 在 Play 中移动或修改对象，然后停止 Play，确认场景文件和组件原值没有变化。

## 启动入口

从已导出引擎启动时运行 `Output/BEngine.bat`，然后在 Hub 中选择 Example。也可以直接运行：

```text
Output/BEgine/BEngine.Editor.exe Example/Project.yaml
```
