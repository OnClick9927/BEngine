---
name: bengine-postprocessing-runtime
description: "Post Processing 运行时集成与扩展。用于 PostProcessLayer, PostProcessVolume, PostProcessProfile, ColorAdjustments, Bloom, Vignette, ParameterOverride 的组件代码、生命周期、Unity 风格 API 与平台接口扩展。"
---

# Post Processing：运行时集成与扩展

## 进入任务

先读 [运行与扩展指南](../../Doc/RuntimeGuide.md)，再按需要读 [包手册](../../Readme.md) 与 Doc/index.html。检查 `com.bengine.postprocessing` 已在 Package Manager 启用；从 Project 确认目标资源存在，不能仅根据类名猜测完整 Unity 行为。

当前约束：单线程运行时、Fix64 二维逻辑、通过平台服务进行 IO。相关组件：PostProcessLayer, PostProcessVolume, PostProcessProfile, ColorAdjustments, Bloom, Vignette, ParameterOverride<T>。包源代码在本包 `BEngine.PostProcessing`，编辑器扩展在 `BEngine.PostProcessing.Editor`。

## 操作流程

1. PostProcessLayer 挂在 Camera2D 上，volumeTrigger 默认为相机 Transform，volumeLayer 使用引擎 LayerMask。Volume 的 priority、weight、blendDistance 与 isGlobal 控制叠加。每个效果参数的 value 和 overrideState 分别决定数值及是否参与覆盖。
2. 通过当前场景的组件和 BAsset 取得依赖，使用 Fix64 常量。先实现一个明确输入产生一个状态变化，再组合事件与异步资源。
3. 为停止、取消、资源失效和再次启动设计状态恢复；修改公共 API 后同步示例、文档和序列化字段。
4. 在 Hierarchy 选择相关对象，在 Inspector 核实序列化值；对照 Scene 与 Game 的实际结果，不依赖静态截图推断行为。
5. 点击 Play 完成预期输入，检查 Console 第一条异常；Stop 后再次运行，检查状态、订阅和资源是否清理。
6. 在 Export/Build 产物中重复关键步骤，记录包版本、资源目录版本、平台和结果。

## 诊断依据

- 效果不出现：检查 Layer enabled、Volume enabled/weight、层掩码、Profile.active 与参数 overrideState。
- 游戏变糊：先关闭 Bloom 对比原图；Point 纹理加 PixelPerfectCamera 只能避免采样缩放模糊，不能消除主动添加的后处理扩散。
- 屏幕全黑：检查平台 RHI 支持和着色器资源打包，验证离屏纹理后再检查最终合成。

不要通过每帧重新读取文件来解决缓存问题。检查 BAsset 的资源代次、实际引用与更新激活时机；网络操作的完成必须由主线程 Poll 进入运行时。

## 验收与交付

- 对同一场景捕获关闭/启用调色的帧，检查中心像素实际改变且没有黑屏。
- 局部 Volume 外、边缘和内部各取样，检查权重连续且只影响选中的层。
- 重复改变窗口尺寸并启用/禁用效果，检查目标尺寸、管线格式和资源释放。

提交可复现的场景路径、最小代码、执行过的命令及观察结果。扩展（Extend）使用已有组件、服务注册和接口；UI、Editor 与平台代码按现有装配边界组织，更新新增字段的序列化和说明。

## 已知边界

API 接近 Unity Post Processing Stack 的 Layer/Volume/Profile 模型，不包含完整 URP/HDRP Volume 系统。局部范围是二维圆形，没有任意 Collider 体积。Bloom 是有限采样邻域效果，不是多层 HDR 金字塔。GPU 后端必须支持 RenderTargets 与 SampledTextures；Vulkan 已实现离屏附件、嵌套目标恢复和格式相关管线缓存。
