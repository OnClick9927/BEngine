# 原始容器、视频流和硬件解码

## 当前跨平台实现

Windows 使用 Media Foundation；Linux/macOS 使用已安装的 FFmpeg。Android 使用 MediaCodec，iOS 使用 AVAssetReader，并通过带时间戳的 PCM reader 接入 AudioSource 混音器。各平台的格式能力仍取决于系统解码器。移动端代码已完成宿主编译，真机播放与音画同步仍须在目标设备验收。

Web 宿主提供 `await VideoClip.LoadAsync(url)`、`await VideoClip.FromBytesAsync(bytes, "clip.mp4")` 和 VideoPlayer.Url 的异步容器加载。浏览器 video/canvas 在主线程轮询 RGBA，暂停跳转会继续轮询，seekCompleted 在实际跳转完成后触发。每个 reader 有独立游标；关闭或销毁会释放元素、Blob URL 和音轨游标。同步原始容器资源加载请在 Web 改用上述异步入口，或继续导入便携帧。

Web MP4 通过 HTTP Range 读取容器索引和音频数据，视频元素直接播放 URL；服务器不支持 Range 时仅接受最多 64 MiB 的完整下载。MP4 sample table 提供真实平均帧率；可变帧率的 `SeekFrame` 仍按名义帧率换算，应以秒数定位。AAC-LC 音轨由 WebCodecs 在浏览器主线程异步分段解码，资源预热最多两秒 PCM、每个音频游标最多四秒 PCM，不按影片总时长分配缓存。暂时无 PCM 时声部等待；源音轨存在但解码器不支持时明确报错。暂不支持 fragmented MP4、WebM 音轨、字幕、DRM、直播与自适应码率。浏览器色彩处理与 CPU 解码在色彩标签缺失或色度边界可能有差异。

`asyncDecoding` 在引擎线程的后续帧执行准备和读取，合并连续 Seek 请求，只保留最近请求及一幅完成画面；不会创建视频工作线程。原生同步解码器的单次读取仍可能阻塞该帧。自定义宿主须推进 `BTaskScheduler.Main`。

FFmpeg 采用真实帧 PTS 索引定位可变帧率内容，重复读取同一帧复用单帧缓冲，不重复启动进程。

## 制作与兼容

新视频默认使用 Portable Frames，生成可在 Windows、Linux、macOS、Web、Android 和 iOS 运行时读取的 `.bvideo` 内容。Windows 编辑器在找不到 FFmpeg 时自动使用 Media Foundation 转码；Linux/macOS 编辑器可从 `PATH` 或 `BENGINE_FFMPEG_PATH` 查找无 `.exe` 后缀的 ffmpeg/ffprobe。Original Container 原样保存视频文件，所有音轨、分辨率和帧时间保留；当前播放器选择首个视频和首个音频流，该模式由目标宿主提供原生解码。转换参数只在 Portable Frames 模式显示并生效。旧的 maxWidth/frameRate/importAudio 设置仍按 Portable Frames 解释，旧 BVID0001 资源仍可读。导入、Apply/Revert 和资源引用沿用 AssetDatabase；运行时 codec 可以从磁盘文件或 AssetBundle 提供的内容加载。

便携模式最多 2048×2048、120 fps、72000 帧、512 MiB，转换临时原始帧最多 4 GiB。原始容器按解码后的画面预算限制为每帧 RGBA 64 MiB、单边最多 8192、帧率最多 240、有限正时长最多 48 小时；含音轨时还受 AudioClip 的有符号 32 位帧数限制。按系统编解码器能力决定具体格式，不承诺安装 Windows 就能播放所有 WebM/HEVC/AV1 文件。

## 运行时读取

```csharp
var clip = VideoClip.Load("D:/Movies/intro.mp4");
using var cursor = clip.OpenReader(VideoDecodeMode.Automatic);
var rgba = new byte[cursor.width * cursor.height * 4];
cursor.ReadFrame(1.5, rgba);
// cursor.timestamp 是选中画面的实际展示时间。
// 完成后销毁自己创建的 clip；场景资源引用由资源系统管理。
```

`VideoClip.Load` / `LoadNative` 也接受 HTTP(S) 地址。系统媒体源负责网络读取与缓存；BEngine 不先把整个网络文件下载成 PCM/帧数组。`VideoDecoder.Open` 可直接取得独立 reader，`OpenAudio` 取得首个音轨的流式 PCM reader。同步 API 在调用线程准备、读取和跳转；VideoPlayer.asyncDecoding 和 LoadAsync 提供异步准备。网络延迟或长距离同步解码会阻塞调用，也不支持未知时长直播流、DRM、字幕或自适应码率选择。

磁盘和 HTTP Clip 的 `encodedMemoryBytes` 为 0；资源包接口交付完整字节，因此资源包 Clip 保留压缩容器，每个原生内存流还会持有自己的压缩副本。每个视频 reader 保存当前帧、前看帧和一块解码缓冲；音轨保存当前/前看 PCM 包，不创建完整音轨。不是 GPU 零复制：D3D11 解码表面读回后在 CPU 上转换 NV12，再上传到引擎纹理。

播放器和预览直接使用 `Texture.FromRgba32` / `SetPixelData`，避免将整幅视频转换成 Fix64 Color 数组。此 API 复制调用者的字节；Apply 发布纹理版本，支持 GetPixel、PNG、克隆和只读保护。

## 时间、音频和画面

使用实际 presentation timestamp 选择不晚于目标时间的画面，并前看一个解码样本。远跳转先定位，再解码到目标；向后跳转重置解码状态。`frameRateExact` 保留 30000/1001 等帧率，旧 `frameRate` 返回取整值。`frame`、`frameCount` 和 `SeekFrame` 按名义帧率计算；可变帧率视频应使用秒数 `time` 与 reader.timestamp，帧序号不保证对应容器的精确样本序号。

音轨转换为 48 kHz 双声道 PCM，按采样时间填充空隙/裁掉重叠。AAC 起始预滚可能与正式包拥有相同 PTS，通过前看保留后者，避免播放编码延迟；随机跳转先解码一段预滚以恢复滤波器状态。音量、静音和倍速交给 AudioSource，跟随场景时钟，不是独立硬件音频主时钟。

支持 BT.601/BT.709、全范围/视频范围 NV12，按可见区域排除宏块填充；90/180/270 度方向元数据用于还原画面。暂不包含 HDR 色调映射、隔行去交错或播放途中切换分辨率。读失败由播放器 error/errorReceived 报告；修复来源后可再次 Prepare。

## 硬件路径和释放

Automatic 尝试创建独立 D3D11 视频设备和 DXGI manager，失败后回退软件解码。Software 禁用 DXVA。Hardware Required 要求实际收到 IMFDXGIBuffer 解码表面；计数不是仅由配置开关推算。D3D11 硬件验证路径仅实现 Windows；其他平台按宿主能力解码，便携帧仍用 CPU 解压。`VideoPlaybackCapabilities.Current` 可在运行时查询当前平台对 Portable Frames、Original Container、HTTP 源和硬件解码的支持；`ForPlatform` 可供构建检查提前验证目标平台。

所有 reader 应在创建它的引擎线程使用和 Dispose。关闭播放器/预览、禁用/销毁组件、销毁 Clip 会关闭对应原生 reader，释放 COM/MF/D3D 资源。`VideoDecoder.activeNativeSessions` 可用于检查生命周期。Pause 保留准备好的资源；Close 完全释放，之后 Play 会重新打开。

实现依据：[Source Reader](https://learn.microsoft.com/en-us/windows/win32/medfound/source-reader)、[DXGI manager](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-source-reader-d3d-manager)、[旋转元数据](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-mt-video-rotation)。ABI 声明来自 Windows SDK；运行时没有捆绑 FFmpeg。

验证程序 `Temp/Validation/VideoEnhancements` 使用原创动画/音轨与 FFmpeg 独立 RGBA/PCM 参考，覆盖软件/D3D11、非对齐尺寸、方向、往返跳转、音轨、HTTP、重新导入、编辑预览、资源释放和 Vulkan/OpenGL 读回。验证工具只生成测试样本。
