# BEngine Navigation2D

Navigation2D 是 BEngine 的确定性 2D 导航包，提供网格烘焙、A* 路径、Agent 移动、
Obstacle carving、Link 与 Modifier。它只处理 2D 世界，所有位置、距离、速度和时间都使用
Fix64。包依赖 Physics2D，以 Collider 作为主要几何采样来源。

## 快速开始

1. 打开 Window > General > Package Manager。
2. 选择 2D Navigation 并启用；确认 2D Physics 依赖也已解析。
3. 切换到 Examples，在 Navigation Surface And Agent 后点击 Import。
4. 打开 Assets/Examples/NavigationSurfaceAndAgent/res/Navigation.scene.yaml。
5. 选择 Navigation Surface，检查 Size、Cell Size、Agent Radius 和 Layer Mask。
6. 从组件 Context 菜单执行 Bake。
7. 点击 Play，按 Space 切换 Agent 目标；在 Scene 开启 Gizmos，在 Console 检查路径状态。
8. 停止 Play，场景和组件立即回到播放前，运行时重烘焙不会写入 scene.yaml。

## 完整示例

- Navigation Surface And Agent：场景直接包含背景、Surface、Carving Obstacle、Agent 和 Camera2D。
  展示 SamplePosition、SetDestination、remainingDistance 与往返目标。
- Dynamic Rebake：场景直接包含可移动 Obstacle、自动重寻路 Agent、Surface 与 Camera2D。
  按 Space 移动障碍物并调用 UpdateNavigation。

两个示例都有独立 asmdef、运行时脚本、详细示例说明和可直接检查的场景组件，不依赖 Play
临时创建所有主体。Package Manager 支持逐个 Import、状态检查与 Reimport。

## 组件介绍

### NavigationSurface2D

定义 center/size、collectObjects、layerMask、useGeometry、cellSize、agentRadius 和 agentTypeId。
buildOnStart 控制进入运行时是否自动烘焙。组件 Context 菜单包含 Bake 与 Clear；运行时可调用
BuildNavigation、UpdateNavigation 和 RemoveData。

### NavigationAgent2D

speed、acceleration、angularSpeed、stoppingDistance 控制运动；autoBraking、autoRepath、
updatePosition、updateRotation 控制行为；areaMask 与 agentTypeId 控制可用区域。
hasPath、pathPending、pathStatus、remainingDistance、velocity 和 desiredVelocity 可用于游戏逻辑。

### NavigationObstacle2D

支持 Box 与 Circle，使用 center、size 或 radius 描述形状。carving 决定是否阻断网格；
carveOnlyStationary 可表达仅静止时参与 carving 的意图。移动障碍物后应统一触发 Surface 重建。

### NavigationLink2D 与 Modifier

Link 用 startPoint/endPoint 连接两个区域，支持 bidirectional、width、costModifier 与 area。
Modifier2D 可结合 Collider 覆盖 Area；ModifierVolume2D 用 center/size 声明体积区域。

## 运行时 API

- Navigation2D.CalculatePath：计算 source 到 target 的路径。
- Navigation2D.SamplePosition：把任意坐标吸附到最近的可行走点。
- NavigationAgent2D.SetDestination / SetPath：请求或采用路径。
- NavigationAgent2D.ResetPath / Warp：清除路径或瞬移。
- NavigationSurface2D.BuildNavigation / UpdateNavigation / RemoveData：管理网格生命周期。
- NavigationPath2D.corners / status：读取路径节点与完成状态。

示例调用：

    using Nav = BEngine.Navigation2D.Navigation2D;

    var target = Nav.SamplePosition(requested, out var hit, 2)
        ? hit.position
        : requested;
    if (!agent.SetDestination(target))
        Debug.LogWarning("No navigation path.");

## 编辑器操作

通过 Add Component 的 AdvancedDropdown 搜索 Navigation 2D 分类下的 Surface、Agent、Obstacle、
Link、Modifier 和 Modifier Volume。Scene Gizmos 会区分范围、路径、障碍和连接；可在 Gizmos
类型菜单单独关闭外部脚本或某个组件类型。Hierarchy 左侧的可见/可选择开关适合排除遮挡对象。

推荐顺序：先完成 Collider 与 Layer，再放置 Surface，调整范围与网格参数，添加 Obstacle/Modifier，
最后 Bake 并进入 Play 验证。几何、Layer、Agent Radius 或 Modifier 变化后都需要重新烘焙。

## 如何扩展

项目 Runtime asmdef 引用 BEngine.Navigation2D 后，可实现自己的目标选择、队伍行为和重寻路策略。
优先缓存 Agent 与 Surface；用 SamplePosition 过滤输入；使用 pathStatus 和 remainingDistance 驱动
动画/UI；把动态重烘焙集中到场景级管理器，避免多个对象在同一帧重复 Build。

编辑器扩展应放在独立 Editor asmdef。可以通过 MenuItem 添加批量 Bake 命令，通过
OnDrawGizmosSelected 绘制自定义目标/代价区域，并让类型自然出现在 Scene Gizmos 开关列表。
运行时不能写回场景或组件资源；持久化修改必须在非 Play 状态完成。

## 排错

- 无法寻路：检查 Surface 已烘焙、源点与终点可采样、Area Mask/Agent Type 一致。
- Agent 穿墙：检查 Collider 位于 layerMask，Obstacle carving 开启，移动后执行 UpdateNavigation。
- 路径过粗：减小 cellSize；若贴墙则增大 agentRadius。
- 看不到调试图：开启 Scene Gizmos，并启用对应 Navigation2D 组件类型。
- 停止 Play 后修改消失：这是编辑器运行镜像隔离的预期行为。

完整图文、字段表、API 与扩展教程位于 Editor/Doc/index.html。
