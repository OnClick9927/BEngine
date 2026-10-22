# 逐帧与定位事件

`VideoPlayer.SeekFrame(index)` 精确定位帧，保留播放/暂停状态；自动 Prepare，非法索引报错。`StepForward()` 暂停后前进一帧，只有 isLooping 为 true 时才回到首帧。

`frameReady(player, frameIndex)` 在新展示时间的画面提交后触发；`seekCompleted(player)` 在 SeekFrame 完成后触发。Inspector 的 Next Frame 按钮可在 PlayMode 逐帧检查。原始容器使用 `frameRateExact`；可变帧率的帧索引为名义值，应使用 `time` 定位。详见 [解码与时间语义](StreamingAndDecoding.md)。

示例工程：Main → Video Frame Stepping。Next Frame、First Frame、Middle Frame 和 Play 分别控制步进、定位与播放，底部显示事件计数。
