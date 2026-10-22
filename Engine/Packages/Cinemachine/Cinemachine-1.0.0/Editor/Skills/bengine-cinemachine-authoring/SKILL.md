---
name: bengine-cinemachine-authoring
description: "Cinemachine 2D 资源与场景配置。用于 CinemachineBrain, CinemachineCamera, CinemachineBlendDefinition, CinemachineConfiner2D, CinemachineImpulseSource, CinemachineImpulseListener 的场景创建、Inspector 配置、资源导入与示例修改。"
---

# Cinemachine 2D：资源与场景配置

## 进入任务

先读 [运行与扩展指南](../../Doc/RuntimeGuide.md)，再按需要读 [包手册](../../Readme.md) 与 Doc/index.html。检查 `com.bengine.cinemachine` 已在 Package Manager 启用；从 Project 确认目标资源存在，不能仅根据类名猜测完整 Unity 行为。

当前约束：单线程运行时、Fix64 二维逻辑、通过平台服务进行 IO。相关组件：CinemachineBrain, CinemachineCamera, CinemachineBlendDefinition, CinemachineConfiner2D, CinemachineImpulseSource, CinemachineImpulseListener。包源代码在本包 `BEngine.Cinemachine`，编辑器扩展在 `BEngine.Cinemachine.Editor`。

## 操作流程

1. 在输出 Camera2D 同一对象添加 CinemachineBrain；新建独立对象添加 CinemachineCamera，避免两个组件反复写同一 Transform。
2. 把角色 Transform 赋给 follow；从零 damping 开始检查取景，再调节 deadZone、trackedObjectOffset 和 damping。
3. 创建第二台虚拟相机并改变 priority，设置 Brain.defaultBlend；检查初次激活直接取景，后续切换产生过渡。需要震动时再配置 Source/Listener 的层。
4. 在 Hierarchy 选择相关对象，在 Inspector 核实序列化值；对照 Scene 与 Game 的实际结果，不依赖静态截图推断行为。
5. 点击 Play 完成预期输入，检查 Console 第一条异常；Stop 后再次运行，检查状态、订阅和资源是否清理。
6. 在 Export/Build 产物中重复关键步骤，记录包版本、资源目录版本、平台和结果。

## 诊断依据

- 相机跳回：检查多个 Brain 或游戏脚本是否同时写同一 Camera2D。
- 切换不发生：检查目标是否 enabled 且 active、priority 是否高于当前目标。
- 像素图仍模糊：在输出相机启用 PixelPerfectCamera，纹理设 Point；不要把阻尼后的浮点渲染结果再次非整数缩放。

不要通过每帧重新读取文件来解决缓存问题。检查 BAsset 的资源代次、实际引用与更新激活时机；网络操作的完成必须由主线程 Poll 进入运行时。

## 验收与交付

- 初次激活检查没有从原点缓慢插入；随后切换在中点和终点检查相机位置。
- 混合期间两台参与相机 IsLive 都为 true；无可用相机时 activeVirtualCamera 应清空。
- 以不同 Game 窗口尺寸检查跟随、相机切换、震动和像素网格。

提交可复现的场景路径、最小代码、执行过的命令及观察结果。资源必须可经 Package Manager 导入；保留 GUID、asmdef 和 res 目录，示例说明应指向真实场景。

## 已知边界

这是面向本引擎 Camera2D 的独立实现，不提供 Unity Cinemachine 的 3D Body/Aim 管线、Timeline 或完整扩展生态。Confiner 约束相机中心点，不保证整个可视矩形位于多边形内。混合源使用切换时的状态快照。PixelPerfectCamera 会在最终渲染阶段吸附相机，过小的震动可能被像素网格吞掉。
