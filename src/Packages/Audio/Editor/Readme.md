# BEngine Audio

## 混音快照

Assets/Create/Audio/Mixer Snapshot 创建快照，groups 指定 Mixer Group、Volume 和 Mute。场景添加 AudioMixerController，将其赋给 AudioSource Inspector 的 Mixer Controller；输出组仍由 Output Mixer Group 指定。

`controller.TransitionTo(snapshot, duration)` 平滑切换音量。中途切换从当前音量继续；未出现在新快照的组回到资源原始音量。父组音量参与相乘，资源 mute 始终有效。每个控制器持有独立状态，不会写回组或快照资源；控制器只作用于同一场景的 AudioSource。MonoBehaviour.Update 自动推进，手动调用 Advance 适用于不运行场景的测试，不应与自动推进重复。

压缩格式、流式读取、后台/预加载及 Mixer Group 效果/发送见 [流式音频与混音](Doc/StreamingAndMixer.md)。

详细的生命周期、分包更新、平台扩展、行为限制与验证步骤见 [运行与扩展指南](Doc/RuntimeGuide.md)。任务 Skills 位于 `Editor/Skills`。

Audio 是面向 BEngine 2D 项目的音频包，提供 WAV/MP3/Ogg Vorbis 导入、`AudioClip`、`AudioSource`、
`AudioListener`、DSP 混音和可替换输出后端。Windows 优先使用 waveOut，失败时尝试 OpenAL；系统没有可用后端时退化为
静音输出，场景和游戏逻辑仍可运行。该包没有任何 3D 声场、空间距离衰减或环绕声依赖。

## 快速开始

1. 打开 `Window > General > Package Manager`，选择 Audio 并点击 Import。
2. 在包的 Examples 页导入 Audio Getting Started。
3. 在 Project 打开 `Assets/Examples/AudioGettingStarted/res/Audio.scene.yaml`。
4. 在 Hierarchy 选择 `Music Source`，在 Inspector 确认 Audio Clip、Volume、Pitch、Stereo Pan、
   Loop 与 Play On Awake。
5. 打开 Scene、Game 和 Console，点击 Play。按 Space 暂停/继续，按 Up/Down 调节音量，按 Left/Right
   调节声像，按 R 从头播放。
6. 点击 Stop 后，运行时的播放游标和临时参数不会写回场景。

示例包含真实场景、WAV、运行时脚本、asmdef 和操作说明。Audio Getting Started 展示循环播放，
One Shot Mixer 展示两个短音效、多 voice 混音、独立增益与左右声像。音源组件、监听器和脚本均已保存
到场景，导入后无需手动组装。

## 资源与组件

`AudioClip` 是 `BAsset`。`AudioImporter` 支持 PCM 8/16/24/32 位、IEEE Float 32 位 WAV，以及 MP3 和 Ogg Vorbis。
Inspector 显示样本帧数、声道数、采样率、时长、加载状态和内存量。导入设置包含 Load Type、
Force To Mono、Normalize、Load In Background 和 Preload Audio Data；这些策略保存在导入产物中供 Player 使用。

`AudioSource` 是播放组件，持有 `AudioClip` 的 ObjectField 引用。可调整 Volume、Pitch、Stereo Pan、
Priority、Loop、Mute 和 Play On Awake，并调用 Play、Pause、UnPause、Stop、PlayDelayed、PlayOneShot。
这是 2D 声音模型：Stereo Pan 只控制左右声道，不读取 Transform 距离。

`AudioListener` 是场景监听器标记。全局静音/暂停由静态 `AudioListener.volume` 和
`AudioListener.pause` 控制。场景中通常保留一个 Listener；包不会加入 3D listener velocity、doppler
或距离曲线。

## 运行时 API

- `AudioClip.Create` 创建可写 PCM Clip；`GetData`、`SetData` 以样本帧偏移读写数据。
- `AudioClip.Load(path)` 读取支持的格式；Load Type 重载可选择 Streaming，其他重载可选择 forceToMono 与 normalize。
- `AudioSource.Play`、`PlayDelayed`、`PlayOneShot`、`Pause`、`UnPause`、`Stop` 控制播放。
- `AudioSource.time` 与 `timeSamples` 可定位播放游标；`isPlaying` 查询状态。
- `AudioOutput.current` 可安装平台专用 `IAudioOutput`；`ResetToSystemDefault()` 恢复默认设备选择。
- `RuntimeAssetCodecRegistry` 让 Resources 和 AssetBundle 从字节恢复 AudioClip，而不依赖 Editor。
- `AudioLoadingSettings.usePlatformDecoding` 默认启用。在 Web 上，后台解压的编码音频可使用浏览器异步解码，随后按块提交 PCM；保持导入的采样率、声道与帧数。格式不支持或结果不匹配时回退到托管解码。设为 `false` 可强制使用托管解码。Streaming 与同步加载策略不变。
- 加载阶段可用 `using var loading = FrameWorkBudget.EnterLoading(token);` 为协作准备分配有界帧预算；Web PCM 普通帧每块最多 128 KiB，加载作用域内最多 256 KiB，保持完整声道帧。作用域支持嵌套、取消与幂等释放；退出后恢复原游戏帧预算，不改变音频采样率、音量或回收策略。
- `await source.PreparePlaybackAsync(clips, context)` 在有界协作步骤中准备音频及声源的 Mixer 路由；两条独立 Reader 使用主声音和 One-shot 共用的混音路径，丢弃每步最多 128 帧 PCM，不向设备提交，不修改实际播放状态，不需要临时播放后等待固定游戏帧。声源在准备期间必须保持启用、场景激活及路由稳定；Streaming 不会因此全量解码，取消会释放准备 Reader 和 DSP 缓冲。

示例：

```csharp
var source = GetComponent<AudioSource>();
source.volume = Fix64.Parse("0.75");
source.panStereo = Fix64.Parse("-0.25");
source.PlayDelayed(Fix64.Parse("0.15"));
source.PlayOneShot(hitClip, Fix64.Parse("0.5"));
```

## Editor 操作

将 Project 内的 AudioClip 拖到 AudioSource 的 Audio Clip ObjectField 可赋值；拖拽过程中只有类型匹配且
光标位于字段内才显示可接受状态。字段右侧选择按钮可在可搜索的 Assets/Scene TreeView 里选择对象。
单击对象字段会 Ping 并展开 Project 父级，双击才会改变 Selection。所有 Inspector 修改使用 Undo，
可用 Ctrl+Z/Ctrl+Y 或顶部 Undo 历史列表回退。

通过 `GameObject > Audio > Audio Source` 或 Inspector 的 Add Component 添加音源；通过
`GameObject > Audio > Audio Listener` 添加监听器。锁定 Inspector 后可在 Project 拖入资源而不改变
当前 Inspector。Console 用于查看后端加载/解码错误，Profiler 可观察 Runtime Audio Mix 样本。

## 如何扩展

运行时 asmdef 引用 `BEngine.Audio` 后，可实现音乐管理器、音效池、交叉淡入淡出或流式解码。
新输出设备实现 `IAudioOutput`，在初始化阶段设置 `AudioOutput.current`。实现必须接受交错 float PCM，
不得在每帧 Mix 热路径分配托管内存；设备丢失时应释放句柄并允许回退。

编辑器扩展放入 Editor asmdef。自定义格式可实现新的 `AssetImporter`，并在加载时注册
`RuntimeAssetCodecRegistry` 解码器；注册应可随包卸载清理。自定义 Inspector 要使用 ObjectField、Undo、
`EditorUtility.SetDirty`，并遵循 Play 模式不保存资源的规则。

## 限制与排错

- 当前支持 WAV、MP3、Ogg Vorbis，未包含 AAC、Opus、FLAC。磁盘 Streaming 不保留完整编码或 PCM 数据；AssetBundle 加载仍保留压缩字节。
- 听不到声音时检查系统设备、AudioListener.pause、Source mute/volume、Clip 引用、Mixer 路由诊断和场景运行状态。
- WAV 导入失败时在 Console 查看格式、位深、块长度和采样率错误；原文件不会被修改。
- 多个音源按 Priority 稳定混音；削波在输出前限制到 `[-1, 1]`。
- 该包只实现 2D 音频；不要把 Transform 距离、3D spatial blend 或 doppler 当作现有能力。

完整截图、字段说明、操作流程和扩展契约见 `Editor/Doc/index.html`。

## 新增功能与示例

详见 [扩展功能](Doc/Extensions.md)，示例工程 Main 主菜单提供对应入口。

## 编辑器工作流

选择 AudioClip 或 AudioSource，Inspector 的 Editor Preview 提供 Play/Pause/Stop、循环、时间定位和独立预览音量。无需运行场景；关闭 Inspector、切换资源、开始 Play 会释放预览输出，预览不改写场景组件。
