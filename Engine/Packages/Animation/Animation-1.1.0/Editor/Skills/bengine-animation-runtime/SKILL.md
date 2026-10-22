---
name: bengine-animation-runtime
description: "Animation 运行时集成与扩展。用于 AnimationCurve, AnimationClip, AnimatorController, AnimatorState, Animator, Animation 的组件代码、生命周期、Unity 风格 API 与平台接口扩展。"
---

# Animation：运行时集成与扩展

## 进入任务

先读 [运行与扩展指南](../../Doc/Animation/RuntimeGuide.md)，再按需要读 [包手册](../../Readme.md) 与 Doc/index.html。检查 `com.bengine.animation` 已在 Package Manager 启用；从 Project 确认目标资源存在，不能仅根据类名猜测完整 Unity 行为。

当前约束：单线程运行时、Fix64 二维逻辑、通过平台服务进行 IO。相关组件：AnimationCurve, AnimationClip, AnimatorController, AnimatorState, Animator, Animation。包源代码在本包 `BEngine.Animation`，编辑器扩展在 `BEngine.Animation.Editor`。

## 操作流程

1. Animator.Play(name) 立即选择状态；CrossFade(name, normalizedDuration) 按源 Clip 长度计算过渡时间。SetFloat/SetInteger/SetBool/SetTrigger 更新参数，IsInTransition(0) 查询实际过渡。applyRootMotion 将根 Transform 的位移和旋转增量应用到对象；deltaPosition/deltaRotation 暴露本次增量。
2. 通过当前场景的组件和 BAsset 取得依赖，使用 Fix64 常量。先实现一个明确输入产生一个状态变化，再组合事件与异步资源。
3. 为停止、取消、资源失效和再次启动设计状态恢复；修改公共 API 后同步示例、文档和序列化字段。
4. 在 Hierarchy 选择相关对象，在 Inspector 核实序列化值；对照 Scene 与 Game 的实际结果，不依赖静态截图推断行为。
5. 点击 Play 完成预期输入，检查 Console 第一条异常；Stop 后再次运行，检查状态、订阅和资源是否清理。
6. 在 Export/Build 产物中重复关键步骤，记录包版本、资源目录版本、平台和结果。

## 诊断依据

- 画面不动：核对 Clip 路径、绑定成员大小写、对象相对路径、组件启用状态与默认状态名称。
- 事件重复或缺失：记录归一化时间和播放方向，检查同一时刻是否在 Clip 内重复定义事件；事件回调调用 Play 会终止旧状态剩余回调。
- CPU 读文件：Clip/Controller 应经 BAsset 缓存加载；热更新激活后缓存代次改变，下次访问重新加载，勿在 Update 手工解析 YAML。

不要通过每帧重新读取文件来解决缓存问题。检查 BAsset 的资源代次、实际引用与更新激活时机；网络操作的完成必须由主线程 Poll 进入运行时。

## 验收与交付

- 让一次 Tick 跨越 2.5 个循环，再反向跨越 2 个循环，确认事件和根位移都连续。
- 对相同两个常量姿态，在过渡一半时检查数值为中间值，在结束时 IsInTransition(0) 为 false。
- 在独立场景和 TickAll 的多场景模式下比较同样输入的状态结果。

提交可复现的场景路径、最小代码、执行过的命令及观察结果。扩展（Extend）使用已有组件、服务注册和接口；UI、Editor 与平台代码按现有装配边界组织，更新新增字段的序列化和说明。

## 已知边界

Animator 当前提供一个基础层，layer 为 0 或默认 -1；不提供 Humanoid、Avatar、BlendTree 或多层混合。根运动只解释本引擎二维根 Transform 曲线。中断正在进行的过渡时，源状态使用当前目标状态，不保存完整混合姿态。
