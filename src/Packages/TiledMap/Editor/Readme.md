# BEngine TiledMap

TiledMap 提供稀疏 2D Tilemap、TilePalette YAML、TextureAtlas 导入、可撤销编辑器画笔、
相机剔除与统一 2D 合批。Tile ID 0 表示空单元，正 ID 映射到 Palette 的 TileDefinition。

## 快速开始

1. 打开 Window > General > Package Manager，启用 Tiled Map。
2. 从 Examples 导入 Atlas Palette。
3. 打开 Assets/Examples/AtlasPalette/res/AtlasPalette.scene.yaml。
4. 打开 Window > 2D > Tile Palette，载入示例 tilepalette.yaml。
5. 选择 Atlas Tilemap，在 Palette 窗口选择 Tile 和 Paint/Erase/Flood。
6. 使用 Transform 下拉设置 FlipX、FlipY、Rotate90、Rotate180；点击网格绘制。
7. 保存 Palette 和场景，进入 Play 验证 Camera culling、排序和运行时 TilemapPulse。
8. 停止 Play；运行时 SetTile 和组件修改不会保存到 scene 或 palette asset。

## 完整示例

- Atlas Palette：场景直接序列化八个 TileCell、四项 Palette、旋转/翻转、TilemapRenderer 与 Camera2D。
  展示 2x2 Atlas 切片、TextureAtlas 导入、UV 与批次条件。
- Runtime Painting：场景直接保存 Tilemap、Renderer、Palette 引用、RuntimePainter 与 Camera2D。
  展示 BoxFill、SetTile、TransformFlags 和运行时资源隔离。

两个示例均有独立 asmdef、场景、Palette、脚本和详细 Readme，不是只含代码的空场景。

## 组件与资源

Tilemap 保存 paletteAsset、cellSize、cellGap、tileAnchor 和稀疏 Cell。TilemapRenderer 继承
Renderer2D，配置 sortingLayer、orderInLayer、opacity、material、color、sortOrder 和
cullOutsideCamera。RequireComponent 会保证 Renderer 与 Tilemap 在同一对象。

TilePalette 保存 Name、Atlas、Columns、Rows、Tiles。SliceAtlas 按规则网格生成 UV；
ImportAtlas 从 BEngine TextureAtlas 的 Sprite 名称和归一化 UV 生成 TileDefinition。
TileDefinition 包含 ID、Name、Texture、UV、RGBA Tint 与 HasCollider。

## 编辑器操作

- Assets > Create > 2D > Tile Palette：创建 Palette。Project 右键菜单与顶部 Assets 菜单一致。
- GameObject > 2D Object > Tilemap：创建 Tilemap + TilemapRenderer。
- Window > 2D > Tile Palette：打开绘制窗口。
- Palette 工具栏：New、Save、Reload、Load、Slice Atlas、Import Texture Atlas。
- 画笔：Paint、Erase、Flood、Transform、Box Fill、Clear All。

绘制操作调用 Undo.RecordObject；修改后 SetDirty 并刷新 Scene，因此 Ctrl+Z 可撤销。选中对象时
Scene Gizmos 显示网格与边界，外部 Tilemap 类型可在 Gizmos 菜单单独开关。

## 运行时 API

- GetTile、GetCell、HasTile
- SetTile、ClearTile、ClearAllTiles
- BoxFill、FloodFill、SwapTile
- GetBounds、GetTiles
- CellToLocal、CellToWorld、WorldToCell
- SetPaletteOverride、TryGetPalette、RefreshAllTiles
- TilePalette.SliceAtlas、ImportAtlas、Load、Save

示例：

    map.BoxFill(new TileCoordinate(-5, -3), new TileCoordinate(5, 3), 1);
    map.SetTile(new TileCoordinate(0, 0), 2,
        TileTransformFlags.Rotate90 | TileTransformFlags.FlipX);

## 渲染与合批

Tile 单元遵守统一顺序：Sorting Layer、Order In Layer、Hierarchy、透明度/稳定顺序。
TilemapRenderer.sortOrder 决定单个地图内的单元遍历方向。Camera2D.cullingMask 必须包含世界层，
cullOutsideCamera 会在提交前跳过视口外单元。

Material、Shader、Atlas/Texture 决定能否合批。使用同一 Palette Atlas 和 Renderer Material 的
连续 Tile 可共享 Batch；不同材质、Shader、纹理或排序约束会断批。正确顺序始终优先于 Draw Call 数。

## 如何扩展

Runtime asmdef 引用 BEngine.TiledMap 后，可实现程序化房间、关卡流送和运行时破坏。缓存 Tilemap，
用确定性坐标序列与 BoxFill/SwapTile 批量更新；完成后一次 RefreshAllTiles。临时 Palette 使用
SetPaletteOverride；不要在 Play 中 Save 资源。

Editor asmdef 可扩展 Palette 窗口、导入器或批量命令。所有编辑器修改应先 Undo.RecordObject，
再 EditorUtility.SetDirty 与 SceneView.RepaintAll；资源创建使用 GenerateUniqueAssetPath。

## 排错

- 检查纹理：Atlas 为空或路径不可解析。
- 画笔无效：先选中带 Tilemap 的对象，并选择正 Tile ID。
- Tile 不显示：检查 Palette 定义、Camera Layer、opacity 与 Renderer enabled。
- 合批断开：比较 Material、Shader、Atlas/Texture 和排序连续性。
- 停止 Play 后地图恢复：这是运行镜像隔离；持久修改必须在编辑模式保存。

完整图文流程、字段表、API 与扩展教程见 Editor/Doc/index.html。
