---
name: bengine-tiledmap-runtime
description: "TiledMap 运行时集成与扩展。用于 TilePalette, TileDefinition, Tilemap, TilemapRenderer, TileCoordinate, TileTransformFlags, ITilemapTextureSource 的组件代码、生命周期、Unity 风格 API 与平台接口扩展。"
---

# TiledMap：运行时集成与扩展

## 进入任务

先读 [运行与扩展指南](../../Doc/RuntimeGuide.md)，再按需要读 [包手册](../../Readme.md) 与 Doc/index.html。检查 `com.bengine.tiledmap` 已在 Package Manager 启用；从 Project 确认目标资源存在，不能仅根据类名猜测完整 Unity 行为。

当前约束：单线程运行时、Fix64 二维逻辑、通过平台服务进行 IO。相关组件：TilePalette, TileDefinition, Tilemap, TilemapRenderer, TileCoordinate, TileTransformFlags, ITilemapTextureSource。包源代码在本包 `BEngine.TiledMap`，编辑器扩展在 `BEngine.TiledMap.Editor`。

## 操作流程

1. TilePalette 描述贴图与 TileDefinition 的 atlas 区域。Tilemap 保存坐标与图块，TilemapRenderer 参与场景排序与相机裁剪。ITilemapTextureSource 隔离纹理字节来源；GPU 上传在缓存缺失时执行，资源缓存代次变化会释放旧贴图。
2. 通过当前场景的组件和 BAsset 取得依赖，使用 Fix64 常量。先实现一个明确输入产生一个状态变化，再组合事件与异步资源。
3. 为停止、取消、资源失效和再次启动设计状态恢复；修改公共 API 后同步示例、文档和序列化字段。
4. 在 Hierarchy 选择相关对象，在 Inspector 核实序列化值；对照 Scene 与 Game 的实际结果，不依赖静态截图推断行为。
5. 点击 Play 完成预期输入，检查 Console 第一条异常；Stop 后再次运行，检查状态、订阅和资源是否清理。
6. 在 Export/Build 产物中重复关键步骤，记录包版本、资源目录版本、平台和结果。

## 诊断依据

- 图块边缘出现邻图颜色：检查 atlas UV、间距和边缘填充；不要对像素图使用双线性缩放。
- 热更新后仍是旧图：资源更新必须通过引擎激活目录或 BAsset 缓存失效路径，自定义源更新时也应触发同一机制。
- 地图闪烁或排序错误：确认 TilemapRenderer 的排序层和顺序，与普通 SpriteRenderer 使用一致的层配置。

不要通过每帧重新读取文件来解决缓存问题。检查 BAsset 的资源代次、实际引用与更新激活时机；网络操作的完成必须由主线程 Poll 进入运行时。

## 验收与交付

- 对相机施加小于半个源像素的移动，比较开启吸附前后图块边缘稳定性。
- 以相同路径激活新纹理，确认下一帧替换 GPU 贴图；在大小写敏感资源源上分别测试不同大小写路径。
- 用密集可见地图记录稳态分配；四角与 UV 使用栈内缓冲，顶点 List 保留容量供后续帧复用。

提交可复现的场景路径、最小代码、执行过的命令及观察结果。扩展（Extend）使用已有组件、服务注册和接口；UI、Editor 与平台代码按现有装配边界组织，更新新增字段的序列化和说明。

## 已知边界

瓦片采样固定为 Point/ClampToEdge，适合像素 atlas；没有自动透明边缘扩展或任意纹理过滤切换。旋转/非整数缩放会改变像素覆盖，PixelPerfectCamera 只吸附图块中心。地图渲染仍需处理可见格子数量，避免每帧重建整张 Palette。
