# Video 与平台媒体解码边界

Video 负责 VideoClip、VideoPlayer、便携 `.bvideo` 编解码、资源导入、播放事件和
解码器/画面预算。Graphics 和 Audio 分别提供纹理与音轨领域能力。

Platform.Contracts 声明 `IPlatformVideoDecoder`、`IPlatformExternalVideoDecoder`、
游标、解码模式、能力及分配前尺寸回调。Platform.DotNet 实现 Windows Media
Foundation/COM/D3D11 和可选 FFmpeg 进程、管道、PTS 索引及退出，不要求窗口/呈现后端。
它们不引用 Video 或任何具体包。Video 不引用具体平台实现，也不声明原生导入。

DotNet 在 Windows 默认注册系统解码器，其他桌面系统选择可选 FFmpeg，保留显式服务覆盖。
Web/移动端原有 PlatformVideoDecoding 委托优先级不变。没有系统容器解码能力时，
Portable Frames 仍可用；Current 根据实际宿主能力返回结果。

公开 VideoDecoder、FfmpegVideoFrameReader、FfmpegVideoAudioReader 的业务调用方式保留。
具体原生实现的程序集归属改变，重新编译宿主；不在 Core 添加指向包的转发。
PlatformVideoFrameReader/PlatformVideoAudioReader 公开为中立适配器，构造时检查空值。
旧游标通过默认接口成员保持可用；真实 CanSeek、解码/硬件计数、索引数量可由宿主提供。

内存输入在宿主 Open 调用期间借用，原生流在返回前建立自身所需的副本。
尺寸回调先进行 Video 预算准入，避免 FFmpeg 在准入失败后继续分配和索引。
AudioSampleReader 仍负责有限数和幅度归一化，平台 PCM 基类不重复该领域策略。

验证源码：Development/Tools/Tests/VideoPlatformOwnership。所有产物在工作区
Temp/Engine/Verification/VideoPlatformOwnership_20261114。
补充的无窗口回归及最终分发见 Temp/Engine/Verification/VideoHeadlessOwnership_20261114。
Windows 以真实 MP4 比较软件解码、实际 PTS、RGBA、PCM、往返 Seek、HTTP 和内存输入；
FFmpeg 管道使用真实子进程协议样本，不宣称可选 FFmpeg 编解码器已安装或真机移动端已播放。
四个平台托管构建、实际 Editor 和依赖边界分别记录，不能替代完整 Example/Snake Player 验收。

本项只是全部 44 包职责审查中的一项，不将无循环依赖或严格构建视为语义审查已完成。
