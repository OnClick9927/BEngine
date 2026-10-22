# Video

视频包支持原始容器播放和便携帧资源，依赖 Audio 包输出声音。

新视频默认 **Portable Frames**，导入后的 `.bvideo` 可由 Windows、Linux、macOS、Web、Android 和 iOS 运行时直接读取。Windows 编辑器没有配置 FFmpeg 时会使用 Media Foundation 转码；其他桌面编辑器从 `PATH` 或 `BENGINE_FFMPEG_PATH` 查找 ffmpeg/ffprobe。

**Original Container** 保留文件、分辨率、帧时间和音轨，Windows 使用 Media Foundation 按需解码，无须安装 FFmpeg。H.264 MP4/MOV 已有软件及 D3D11 路径验证；其他容器/编码取决于系统解码器，导入时会检查能否打开。已有 `.bvideo` 和随包 VideoPlayback 示例无需外部解码工具即可播放。

添加 **Video/Video Player**，设置 Clip 和 Target Renderer。Source 选择 **Url** 时可以填写运行时文件路径或 HTTP(S) 视频地址。调用 `Prepare`、`Play`、`Pause`、`Stop`，设置 `time` 跳转；`Close` 释放解码器和画面并恢复此前的 Sprite。禁用/销毁也释放。Play On Awake、Is Looping、Playback Speed、Volume、Mute 和播放事件继续可用。

Decode Mode 提供 Automatic、Software、Hardware Required。`decoderBackend` 和 `hardwareDecodedFrames` 报告实际输出路径；要求硬件而未收到解码器 GPU 表面时返回错误。每个播放器和 Inspector 预览使用独立游标。

`VideoPlaybackCapabilities.Current` 报告当前平台是否支持 Portable Frames、Original Container、HTTP 源和硬件解码。详见 [原始容器、视频流和硬件解码](Doc/StreamingAndDecoding.md) 与 [逐帧定位](Doc/Extensions.md)。
