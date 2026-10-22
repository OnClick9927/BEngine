# BEngine TiledMap

详细的生命周期、分包更新、平台扩展、行为限制与验证步骤见 [运行与扩展指南](Doc/RuntimeGuide.md)。任务 Skills 位于 `Editor/Skills`。

TiledMap 提供稀疏 2D Tilemap、TilePalette YAML、TextureAtlas 导入、可撤销编辑器画笔、
相机剔除与统一 2D 合批。Tile ID 0 表示空单元，正 ID 映射到 Palette 的 TileDefinition。

## 快速开始

1. 打开 Window > General > Package Manager，启用 Tiled Map。
2. 从 Examples 导入 Atlas Palette。
3. 打开 Assets/Examples/AtlasPalette/res/AtlasPalette.scene.yaml。
4. 打开 Window > 2D > Tile Palette，载入示例 tilepalette.yaml。
5. 在 Active Tilemap 下拉选择 Atlas Tilemap；在调色板中拖选单个或矩形瓦片笔刷。
6. 选择 Paint，在 **Scene 视图**中绘制地图；中键平移、滚轮缩放。
7. 保存 Palette 和场景，进入 Play 验证 Camera culling、排序和运行时 TilemapPulse。
8. 停止 Play；运行时 SetTile 和组件修改不会保存到 scene 或 palette asset。

## 完整示例

- Atlas Palette：场景直接序列化八个 TileCell、四项 Palette、旋转/翻转、TilemapRenderer 与 Camera2D。
  展示 2x2 Atlas 切片、TextureAtlas 导入、UV 与批次条件。
- Runtime Painting：场景直接保存 Tilemap、Renderer、Palette 引用、RuntimePainter 与 Camera2D。
  展示 BoxFill、SetTile、TransformFlags 和运行时资源隔离。

两个示例均有独立 asmdef、场景、Palette、脚本和详细 Readme，不是只含代码的空场景。

## 组件与资源

Tilemap 保存 palette 资源引用、cellLayout、cellSize、cellGap、tileAnchor 和稀疏 Cell。TilemapRenderer 继承
Renderer2D，配置 sortingLayer、orderInLayer、opacity、material、color、sortOrder 和
cullOutsideCamera。RequireComponent 会保证 Renderer 与 Tilemap 在同一对象。

TilePalette 保存 Name、Atlas、Columns、Rows、Tiles。SliceAtlas 按规则网格生成 UV；
ImportAtlas 从 BEngine TextureAtlas 的 Sprite 名称和归一化 UV 生成 TileDefinition。
TileDefinition 包含 ID、Name、Texture、UV、RGBA Tint 与 HasCollider。
还支持 Rules 和 Animation，在 Palette 的默认 Inspector 中展开 Tiles 编辑。规则按顺序匹配原始邻格 ID：指定 X/Y 偏移和 Any/Same/Different/Empty/Occupied/TileId 条件，首个匹配规则选择 OutputTileId。动画按 TileId/Duration 帧列表循环，使用引擎 Time.time；暂停时停止推进。规则输出仅解析一次，再播放输出瓦片的动画，不递归执行规则。

规则和动画只改变外观，保留原始 cell ID、变换和 HasCollider。启用 `com.bengine.tilemap-physics2d` 后，在 Tilemap 对象上添加 TilemapCollider2D 即可生成碰撞；增删瓦片和修改碰撞标记会自动刷新，无间隔矩形相邻格可合并。使用 Assets > Import Tiled Map 可导入 TMX/TMJ。

### 等距与六边形网格

Inspector 的 cellLayout 支持 Rectangle、Isometric、HexagonPointTop、HexagonFlatTop；旧场景默认保持 Rectangle。cellSize 表示轮廓的包围尺寸，cellGap 增加格子间距。默认 tileAnchor=(0.5,0.5)；矩形原点格中心为半个 cellSize，其他布局原点格中心为 (0,0)。改变锚点会同时调整图块、拾取和 Scene 网格，CellToWorld/WorldToCell 保持对应。

六边形使用轴坐标 (q,r)。hexSideRatio 范围 0..1，默认 0.5；PointTop 的中心为 ((q+r/2)*stride.x, (1+hexSideRatio)*r*stride.y/2)，FlatTop 为 ((1+hexSideRatio)*q*stride.x/2, (r+q/2)*stride.y)。该比例统一用于渲染、拾取、轮廓及碰撞；Tiled hexsidelength 会换算为此值。Flood 使用六个相邻格；矩形和菱形使用四个。Box Fill 的范围始终是整数坐标范围。

渲染裁切到对应的菱形/六边形轮廓，UV 按轮廓的矩形包围范围映射；Scene 拾取、网格轮廓和桥接碰撞使用同一形状。支持父级旋转、镜像和非等比缩放。非矩形地图按格子本地高度/横坐标排序；桥接碰撞保留各格多边形，不把它们合并成矩形。

### Prefab 笔刷

在 Tile Palette 的 Prefab Brush 选择 Prefab，使用 Paint、Erase 或 Box Fill 在 Scene 制作场景对象，无需给地图分配 Palette。实例放在 Tilemap 子级，保留 Prefab 来源，支持旋转/翻转、拖动补点和整笔 Undo/Redo；重复涂同一格不会产生重复对象。清空 Prefab Brush 回到瓦片绘制。

实例的 TilemapPrefabInstance 记录格子和来源，保存场景再打开仍可擦除。只擦除当前 Tilemap 直接子级中有该标记的对象；不会删除普通场景子对象。支持 Pick、Move、Box Fill、已有对象连通区域的 Flood 和多 Prefab 印章；改变地图布局会按记录的格子重排实例，支持 Undo。

### Tiled 对象层

TMX/TMJ 可导入瓦片层、分块/压缩数据、外部 tileset、翻转、动画及组偏移。也支持 staggered 菱形布局、非重复图片层、gid 瓦片对象和 JSON/XML 外部模板；模板保留实例覆盖属性，并按地图 tileset 重映射 GID。常用对象层转换为 TiledObject，保留 ID、className、矩形/椭圆/点/多边形/折线、旋转和自定义属性，可用于出生点、区域和路径；Scene 绘制轮廓，Inspector 可编辑并保存。对象坐标按 pixelsPerUnit 换算，默认 100。

Tileset 的 objectgroup 保存为 TileDefinition.CollisionPaths，保留多边形、折线、矩形和椭圆近似，不再替换成整格碰撞。瓦片层及瓦片对象均应用自身缩放、翻转和旋转；YAML 保存、克隆、重导入保留这些路径。自定义形状不参与整格相邻合并。尚不支持重复图片层、text/capsule 对象及六边形 60/120 度旋转，遇到它们会明确报错。

当前拒绝等距对象投影、tile-GID 对象、模板和文本对象，失败时回滚已创建的根对象。对象轮廓不会自动生成 Physics2D Collider，需按项目规则转换。tileset 的碰撞存在标记会保留，尚未转换其全部自定义碰撞形状。

## 编辑器操作

- Assets > Create > 2D > Tile Palette：创建 Palette。Project 右键菜单与顶部 Assets 菜单一致。
- GameObject > 2D Object > Tilemap：创建 Tilemap + TilemapRenderer。
- Window > 2D > Tile Palette：打开绘制窗口。
- Palette 对象选择器选择现有资源，Create 创建，Save 保存到 Assets 下的 `.tilepalette.yaml`。
- Active Tilemap 选择绘制目标；Ping 定位 Hierarchy、Eye 切换 Scene 可见性、Frame 居中目标、New 创建 Tilemap + Renderer。
- Edit 锁定/解锁调色板本身。锁定时拖选笔刷；解锁时可修改 Atlas、规则切片、导入 Sprite Atlas、瓦片碰撞属性及调色板布局。
- Select / Move / Paint / Box Fill / Pick / Erase / Flood 工具同时支持鼠标按钮和快捷键；地图编辑实际发生在 Scene 视图。

绘制操作调用 Undo.RecordObject；修改后 SetDirty 并刷新 Scene，因此 Ctrl+Z 可撤销。选中对象时
Scene Gizmos 显示网格与边界，外部 Tilemap 类型可在 Gizmos 菜单单独开关。

## 运行时 API

- GetTile、GetCell、HasTile
- SetTile、SetTiles（批量提交并只序列化一次）、ClearTile、ClearAllTiles
- BoxFill、FloodFill、SwapTile
- GetBounds、GetTiles
- CellToLocal、CellToWorld、LocalToCell、WorldToCell、GetNeighbors、GetCellLocalOutline
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

## 新增示例

示例工程 Main → Tile Regions & Replacement：FloodFill 填充连通区域，SwapTile 批量替换并恢复地图。

## 编辑器工作流

窗口采用 Unity 的 Tile Palette / Active Tilemap / Scene 绘制流程：

1. 双击 TilePalette，或点击 Tilemap Inspector 的 Open Tile Palette。
2. 通过 Palette 选择已有调色板；也可以 Create，打开 Edit，选择 Atlas Texture、Columns / Rows 后 Slice Atlas into Tiles，再 Save。
3. 设置 Active Tilemap。切换场景对象不必更换当前笔刷；目标使用另一调色板时，选择 Use Target Palette 或 Assign Palette。
4. 在锁定的调色板网格中拖出矩形选区；这是笔刷图案及擦除范围。随后在 Scene 中按下并拖动绘制。
5. Select 在 Scene 中框选瓦片，Move 拖动已选区域，Delete 删除选区。Box Fill 将当前矩形图案重复铺满拖出的区域；Pick 吸取场景区域后切回 Paint。
6. 工具快捷键与 Unity 一致：S 选择、M 移动、B 画笔、U 矩形填充、I 吸取、D 擦除、G 洪泛填充；`[` / `]` 旋转，Shift + `[` / `]` 水平/垂直翻转。右键或 Shift 临时擦除，Esc 退出绘制。
7. 中键平移、滚轮缩放保留 Scene 导航，不修改瓦片。地图位置、旋转、非均匀缩放参与鼠标到格子的坐标换算；地图或格子跨度为零时禁止绘制。
8. 调色板 Edit 开启后，同一组工具编辑的是调色板布局；关闭 Edit 恢复挑选笔刷。Save 保存调色板资源；保存场景保存地图。每笔绘制、选区移动、矩形填充各作为一次 Undo / Redo。

Scene 显示网格、选择边界及画笔覆盖范围；未旋转地图的单次预览不超过 64 个瓦片时还显示半透明纹理。旋转地图和更大的笔刷显示准确的变换后边界。

支持矩形、等距、六边形网格以及 BEngine 的规则瓦片和 Prefab 笔刷；这些资源与 API 不承诺兼容 Unity GridBrush/RuleTile。调色板是 BEngine TilePalette 资源。Flood Fill 使用单个瓦片，可填充已有地图范围内的同类瓦片或空洞，不会填充无限空平面。地图范围之外的空白位置应使用 Paint 或 Box Fill。

矩形笔刷最多 4096 格，单次编辑最多 65536 格，调色板布局最多 65536 格。超过范围会提示并保留已有内容；运行时 BoxFill/FloodFill 另有一百万格上限。批量提交、序列化、渲染及拾取依据实际数据长度设置循环保护预算，避免正常地图触发默认 1000 次脚本循环限制。

参考工作流：[Unity 6 Tile Palette window reference](https://docs.unity3d.com/6000.0/Documentation/Manual/tilemaps/tile-palettes/tile-palette-editor-reference.html)。
