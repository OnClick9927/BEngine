# BEngine Audio

Audio 是面向 BEngine 2D 项目的跨平台音频包，提供 WAV 导入、`AudioClip`、`AudioSource`、
`AudioListener`、软件混音和可替换输出后端。默认后端动态加载 OpenAL；系统没有 OpenAL 时自动退化为
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

`AudioClip` 是 `BAsset`。Project 导入 `.wav` 时由 `AudioImporter` 解码 PCM 8/16/24/32 位或
IEEE Float 32 位 WAV。Inspector 显示样本帧数、声道数、采样率、时长与加载状态。导入设置包含
Force To Mono、Normalize、Load In Background 和 Preload Audio Data。

`AudioSource` 是播放组件，持有 `AudioClip` 的 ObjectField 引用。可调整 Volume、Pitch、Stereo Pan、
Priority、Loop、Mute 和 Play On Awake，并调用 Play、Pause、UnPause、Stop、PlayDelayed、PlayOneShot。
这是 2D 声音模型：Stereo Pan 只控制左右声道，不读取 Transform 距离。

`AudioListener` 是场景监听器标记。全局静音/暂停由静态 `AudioListener.volume` 和
`AudioListener.pause` 控制。场景中通常保留一个 Listener；包不会加入 3D listener velocity、doppler
或距离曲线。

## 运行时 API

- `AudioClip.Create` 创建可写 PCM Clip；`GetData`、`SetData` 以样本帧偏移读写数据。
- `AudioClip.Load(path)` 读取 WAV；重载可选择 forceToMono 与 normalize。
- `AudioSource.Play`、`PlayDelayed`、`PlayOneShot`、`Pause`、`UnPause`、`Stop` 控制播放。
- `AudioSource.time` 与 `timeSamples` 可定位播放游标；`isPlaying` 查询状态。
- `AudioOutput.SetBackend` 可安装平台专用 `IAudioOutput`；`ResetToDefault` 恢复 OpenAL/Null 选择。
- `RuntimeAssetCodecRegistry` 让 Resources 和 AssetBundle 从字节恢复 AudioClip，而不依赖 Editor。

示例：

```csharp
var source = GetComponent<AudioSource>();
source.volume = Fix64.Parse("0.75");
source.panStereo = Fix64.Parse("-0.25");
source.PlayDelayed(Fix64.Parse("0.15"));
source.PlayOneShot(hitClip, Fix64.Parse("0.5"));
```

## Editor 操作

将 Project 内的 WAV 拖到 AudioSource 的 Audio Clip ObjectField 可赋值；拖拽过程中只有类型匹配且
光标位于字段内才显示可接受状态。字段右侧选择按钮可在可搜索的 Assets/Scene TreeView 里选择对象。
单击对象字段会 Ping 并展开 Project 父级，双击才会改变 Selection。所有 Inspector 修改使用 Undo，
可用 Ctrl+Z/Ctrl+Y 或顶部 Undo 历史列表回退。

通过 `GameObject > Audio > Audio Source` 或 Inspector 的 Add Component 添加音源；通过
`GameObject > Audio > Audio Listener` 添加监听器。锁定 Inspector 后可在 Project 拖入资源而不改变
当前 Inspector。Console 用于查看后端加载/解码错误，Profiler 可观察 Runtime Audio Mix 样本。

## 如何扩展

运行时 asmdef 引用 `BEngine.Audio` 后，可实现音乐管理器、音效池、交叉淡入淡出或流式解码。
新输出设备实现 `IAudioOutput`，在初始化阶段调用 `AudioOutput.SetBackend`。实现必须接受交错 float PCM，
不得在每帧 Mix 热路径分配托管内存；设备丢失时应释放句柄并允许回退。

编辑器扩展放入 Editor asmdef。自定义格式可实现新的 `AssetImporter`，并在加载时注册
`RuntimeAssetCodecRegistry` 解码器；注册应可随包卸载清理。自定义 Inspector 要使用 ObjectField、Undo、
`EditorUtility.SetDirty`，并遵循 Play 模式不保存资源的规则。

## 限制与排错

- 当前内建导入器只支持未压缩 WAV，不支持 MP3、AAC、Ogg 或流式磁盘解码。
- 听不到声音时检查系统 OpenAL、AudioListener.pause、Source mute/volume、Clip 引用和场景运行状态。
- WAV 导入失败时在 Console 查看格式、位深、块长度和采样率错误；原文件不会被修改。
- 多个音源按 Priority 稳定混音；削波在输出前限制到 `[-1, 1]`。
- 该包只实现 2D 音频；不要把 Transform 距离、3D spatial blend 或 doppler 当作现有能力。

完整截图、字段说明、操作流程和扩展契约见 `Editor/Doc/index.html`。
