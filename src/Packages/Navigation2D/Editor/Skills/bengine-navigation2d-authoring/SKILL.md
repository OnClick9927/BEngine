---
name: bengine-navigation2d-authoring
description: "Navigation2D 资源与场景配置。用于 NavigationSurface2D, NavigationAgent2D, NavigationPath2D, NavigationObstacle2D, NavigationModifier2D, NavigationLink2D, Navigation2D 的场景创建、Inspector 配置、资源导入与示例修改。"
---

# Navigation2D：资源与场景配置

## 进入任务

先读 [运行与扩展指南](../../Doc/RuntimeGuide.md)，再按需要读 [包手册](../../Readme.md) 与 Doc/index.html。检查 `com.bengine.navigation2d` 已在 Package Manager 启用；从 Project 确认目标资源存在，不能仅根据类名猜测完整 Unity 行为。

当前约束：单线程运行时、Fix64 二维逻辑、通过平台服务进行 IO。相关组件：NavigationSurface2D, NavigationAgent2D, NavigationPath2D, NavigationObstacle2D, NavigationModifier2D, NavigationLink2D, Navigation2D。包源代码在本包 `BEngine.Navigation2D`，编辑器扩展在 `BEngine.Navigation2D.Editor`。

## 操作流程

1. 创建 Surface，设置 bounds/cellSize 和障碍层，添加 Collider2D 障碍后执行 Bake。先观察烘焙结果，再创建 Agent。
2. 用 SamplePosition 验证目标落在可行走区域，给 SetDestination 的返回值和 pathStatus 加日志。PathPartial 只能走到已找到的可达点。
3. 用 Modifier 设置区域并用 areaMask 过滤；添加 Link 连接两个区域，重建 Surface 后验证单向/双向和 costModifier。
4. 在 Hierarchy 选择相关对象，在 Inspector 核实序列化值；对照 Scene 与 Game 的实际结果，不依赖静态截图推断行为。
5. 点击 Play 完成预期输入，检查 Console 第一条异常；Stop 后再次运行，检查状态、订阅和资源是否清理。
6. 在 Export/Build 产物中重复关键步骤，记录包版本、资源目录版本、平台和结果。

## 诊断依据

- 目标在障碍中：允许得到 PathPartial；不得把阻挡中的原始目标追加到路径末尾。
- 高速角色越过终点：核对 stoppingDistance、autoBraking 与每步位移限制，运动应被当前拐点距离截断。
- 障碍移动后仍走旧路：更新障碍并 BuildNavigation；autoRepath 监听烘焙代次，不能代替烘焙。

不要通过每帧重新读取文件来解决缓存问题。检查 BAsset 的资源代次、实际引用与更新激活时机；网络操作的完成必须由主线程 Poll 进入运行时。

## 验收与交付

- 分别测试 mask=0、仅某区域、全区域和不可达目标，检查返回值与 pathStatus。
- 设很大 speed 和较大 Tick，确认不会越过拐点，在终点速度归零。
- 修改传给 SetPath 的原始 corners 后，确认 Agent 的路径不改变；重烘焙后检查 autoRepath。

提交可复现的场景路径、最小代码、执行过的命令及观察结果。资源必须可经 Package Manager 导入；保留 GUID、asmdef 和 res 目录，示例说明应指向真实场景。

## 已知边界

采用离散格网而非 Unity 多边形 NavMesh。Agent 半径参与局部避让，格网净空取决于 Surface 烘焙配置。局部避让是轻量的二维排斥，不是 ORCA，也不保证密集队列无死锁。不同 avoidance quality 当前不提供不同求解器。不要把区域编号直接当掩码，使用 1UL << area。
