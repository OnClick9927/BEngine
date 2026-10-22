---
name: bengine-postprocessing-authoring
description: "Post Processing 资源与场景配置。用于 PostProcessLayer, PostProcessVolume, PostProcessProfile, ColorAdjustments, Bloom, Vignette, ParameterOverride 的场景创建、Inspector 配置、资源导入与示例修改。"
---

# Post Processing：资源与场景配置

## 进入任务

先读 [运行与扩展指南](../../Doc/RuntimeGuide.md)，再按需要读 [包手册](../../Readme.md) 与 Doc/index.html。检查 `com.bengine.postprocessing` 已在 Package Manager 启用；从 Project 确认目标资源存在，不能仅根据类名猜测完整 Unity 行为。

当前约束：单线程运行时、Fix64 二维逻辑、通过平台服务进行 IO。相关组件：PostProcessLayer, PostProcessVolume, PostProcessProfile, ColorAdjustments, Bloom, Vignette, ParameterOverride<T>。包源代码在本包 `BEngine.PostProcessing`，编辑器扩展在 `BEngine.PostProcessing.Editor`。

## 操作流程

1. 在输出相机添加 PostProcessLayer，新建全局 Volume 并赋一个 Profile；先仅调整 colorAdjustments.saturation，以便清楚比较开关效果。
2. 将另一个 Volume 设为局部，设置 center/radius/blendDistance；移动 volumeTrigger 检查过渡，不要只改变渲染相机可见层。
3. 最后逐个启用 bloom 与 vignette，在明暗区域检查颜色、边缘和开关成本。像素艺术优先保持 bloom 强度为零，再按美术需求增加。
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

提交可复现的场景路径、最小代码、执行过的命令及观察结果。资源必须可经 Package Manager 导入；保留 GUID、asmdef 和 res 目录，示例说明应指向真实场景。

## 已知边界

API 接近 Unity Post Processing Stack 的 Layer/Volume/Profile 模型，不包含完整 URP/HDRP Volume 系统。局部范围是二维圆形，没有任意 Collider 体积。Bloom 是有限采样邻域效果，不是多层 HDR 金字塔。GPU 后端必须支持 RenderTargets 与 SampledTextures；Vulkan 已实现离屏附件、嵌套目标恢复和格式相关管线缓存。
