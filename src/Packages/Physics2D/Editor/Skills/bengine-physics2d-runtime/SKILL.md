---
name: bengine-physics2d-runtime
description: "Physics2D 运行时集成与扩展。用于 Rigidbody2D, BoxCollider2D, CircleCollider2D, Collider2D, Physics2D, RaycastHit2D 的组件代码、生命周期、Unity 风格 API 与平台接口扩展。"
---

# Physics2D：运行时集成与扩展

## 进入任务

先读 [运行与扩展指南](../../Doc/RuntimeGuide.md)，再按需要读 [包手册](../../Readme.md) 与 Doc/index.html。检查 `com.bengine.physics2d` 已在 Package Manager 启用；从 Project 确认目标资源存在，不能仅根据类名猜测完整 Unity 行为。

当前约束：单线程运行时、Fix64 二维逻辑、通过平台服务进行 IO。相关组件：Rigidbody2D, BoxCollider2D, CircleCollider2D, Collider2D, Physics2D, RaycastHit2D。包源代码在本包 `BEngine.Physics2D`，编辑器扩展在 `BEngine.Physics2D.Editor`。

## 操作流程

1. Rigidbody2D.simulated=false 同时退出积分、碰撞和空间查询；重新启用保留原速度。力与速度应从 FixedUpdate 管理。Raycast/Overlap 查询使用引擎二维向量和 LayerMask。Collider 的触发事件和接触回调仍在主线程执行。
2. 通过当前场景的组件和 BAsset 取得依赖，使用 Fix64 常量。先实现一个明确输入产生一个状态变化，再组合事件与异步资源。
3. 为停止、取消、资源失效和再次启动设计状态恢复；修改公共 API 后同步示例、文档和序列化字段。
4. 在 Hierarchy 选择相关对象，在 Inspector 核实序列化值；对照 Scene 与 Game 的实际结果，不依赖静态截图推断行为。
5. 点击 Play 完成预期输入，检查 Console 第一条异常；Stop 后再次运行，检查状态、订阅和资源是否清理。
6. 在 Export/Build 产物中重复关键步骤，记录包版本、资源目录版本、平台和结果。

## 诊断依据

- 停止的刚体仍挡路：确认设置的是 simulated=false，而不是仅把速度设为零。
- 快物体穿透：减小固定步长、检查 Collider 厚度及当前支持的碰撞检测模式；不要在 Update 直接跳过障碍移动。
- 多场景重置物理时间：用 SceneRuntime.TickAll 推进多个运行时，共享单次全局帧推进。

不要通过每帧重新读取文件来解决缓存问题。检查 BAsset 的资源代次、实际引用与更新激活时机；网络操作的完成必须由主线程 Poll 进入运行时。

## 验收与交付

- 将 simulated=false 的物体放在射线中，确认它不参与查询且位置不变化。
- 重新启用后验证再次参与碰撞；观察 Stop/销毁后没有遗留接触。
- 相同固定步长和输入运行两次，比较位置与速度的 Fix64.RawValue，而不是格式化小数。

提交可复现的场景路径、最小代码、执行过的命令及观察结果。扩展（Extend）使用已有组件、服务注册和接口；UI、Editor 与平台代码按现有装配边界组织，更新新增字段的序列化和说明。

## 已知边界

这是独立二维定点物理实现，不能假设与 Unity Box2D 数值轨迹相同。刚体与查询的参数使用 Fix64。大量密集碰撞体仍会提高接触求解成本；按场景范围拆分并限制活动物体数量。业务确定性依赖固定输入顺序和一致步长。
