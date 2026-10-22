# Tilemap Physics 2D

启用 `com.bengine.tilemap-physics2d`，它依赖 TiledMap 和 Physics2D。在 Tilemap 对象上添加 Physics 2D/Tilemap Collider 2D。调色板的 Collider 勾选现在决定实际碰撞；关闭的 Tile 和空格子不会产生碰撞。

组件直接生成本地形状，不生成临时场景对象，不写回场景文件。地图增删、Palette 碰撞标记、布局、尺寸、间距和锚点变化后自动重建。矩形布局相邻无间距、无旋转的格子按行合并，减少窄相位形状数量；`mergeAdjacentCells` 可关闭合并。等距布局生成菱形，PointTop/FlatTop 生成六边形，保持真实轮廓，不做矩形合并。图块旋转和父 Transform（包括镜像、非等比缩放）参与几何变换。可作为 Trigger，也可附加 Rigidbody2D 移动整个地图。

Physics2D 保持独立于 Tilemap；TiledMap 保持独立于 Physics2D，桥接包拥有两者的适配逻辑。
