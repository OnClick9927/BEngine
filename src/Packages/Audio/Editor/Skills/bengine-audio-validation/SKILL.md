---
name: bengine-audio-validation
description: "Audio 排查与交付验证。用于 AudioClip, AudioSource, AudioListener, AudioOutput, IAudioOutput 的性能分析、行为回归、资源分包更新和播放器验收。"
---

# Audio：排查与交付验证

## 进入任务

先读 [运行与扩展指南](../../Doc/RuntimeGuide.md)，再按需要读 [包手册](../../Readme.md) 与 Doc/index.html。检查 `com.bengine.audio` 已在 Package Manager 启用；从 Project 确认目标资源存在，不能仅根据类名猜测完整 Unity 行为。

当前约束：单线程运行时、Fix64 二维逻辑、通过平台服务进行 IO。相关组件：AudioClip, AudioSource, AudioListener, AudioOutput, IAudioOutput。包源代码在本包 `BEngine.Audio`，编辑器扩展在 `BEngine.Audio.Editor`。

## 操作流程

1. 播放后静音若干帧，确认输出为零而 timeSamples 增加。
2. 仅播放 OneShot 时检查 isPlaying=true，Pause 后为 false，UnPause 后继续，Stop 清理所有声部。
3. 用假的 IAudioOutput 记录样本长度与饱和范围，再在目标平台听测左右声道。
4. 在 Hierarchy 选择相关对象，在 Inspector 核实序列化值；对照 Scene 与 Game 的实际结果，不依赖静态截图推断行为。
5. 点击 Play 完成预期输入，检查 Console 第一条异常；Stop 后再次运行，检查状态、订阅和资源是否清理。
6. 在 Export/Build 产物中重复关键步骤，记录包版本、资源目录版本、平台和结果。

## 诊断依据

- 无声：检查 AudioListener.pause/volume、Source.mute/volume、组件 active 状态及平台 IAudioOutput 是否为空输出。
- 暂停后提示音仍响：检查是否用了不同 Source；同一 Source 的 Pause 应暂停所有声部。
- 长期静音后恢复从旧位置播放：记录 timeSamples；mute 应持续推进，Pause 才保持位置。

不要通过每帧重新读取文件来解决缓存问题。检查 BAsset 的资源代次、实际引用与更新激活时机；网络操作的完成必须由主线程 Poll 进入运行时。

## 验收与交付

- 播放后静音若干帧，确认输出为零而 timeSamples 增加。
- 仅播放 OneShot 时检查 isPlaying=true，Pause 后为 false，UnPause 后继续，Stop 清理所有声部。
- 用假的 IAudioOutput 记录样本长度与饱和范围，再在目标平台听测左右声道。

提交可复现的场景路径、最小代码、执行过的命令及观察结果。比较修正前后的状态或像素值，区分首次加载与稳态性能；失败必须保留日志，未运行的平台标记为未验证。

## 已知边界

提供二维声像、WAV/MP3/Ogg 解码、独立压缩音频流、Mixer Group/快照/发送，以及 EQ、滤波、失真、Chorus、Delay、Compressor 和卷积混响。AudioClip.Create(stream:true) 需要 PCM 回调和可选定位回调；每声部独立 reader 可使用 AudioClip.CreateStream。PCM/DSP 使用 float，播放时间和参数使用 Fix64；音频硬件时钟不作为游戏逻辑时钟。提供托管插件工厂和原生 C ABI v1 DSP 接口；动态库需随目标宿主部署，Web/iOS 使用编译进宿主的静态适配。不提供 3D 空间化。
