# Audio 与平台音频能力

Audio 保留 AudioClip/AudioSource/AudioListener、WAV/MP3/Vorbis 编解码、加载与
流式播放策略、混音器、DSP 和可替换的领域输出。系统设备不是游戏框架或音频资产
的实现；waveOut/OpenAL 位于 Platform.Desktop，WebAudio 和浏览器原生解码位于
Platform.Web，Android/iOS 继续使用各自平台设备。

## 公开接入

- 平台实现 `IPlatformAudioOutputFactory`，注册到现有运行时服务。带
  `PlatformAudioOutputOptions` 的重载携带采样率、通道和缓冲长度；默认实现兼容
  已有双整数工厂。打开设备仍是惰性的，不因加载平台程序集就创建声卡资源。
- `IPlatformBufferedAudioOutput` 暴露容量、队列和背压，提交提示用默认接口方法
  兼容已有设备。只有明确实现 `IPlatformIndependentAudioOutput` 的设备允许独立混音。
  Web 不声明此能力；Audio 不再根据操作系统名单决定是否创建混音线程。
- `IPlatformAudioDecoderFactory` 返回有状态、可取消和释放的解码操作。
  浏览器输入在工厂返回前由实现复制，异步持有者不借用调用者内存。
- Audio 的 `PlatformAudioOutputFactory` 将这些中立能力适配成 `IAudioOutput`，
  转发诊断、背压与释放；未提供设备时使用原有静音行为。用户仍可直接安装自定义
  `AudioOutput.current`，无需实现平台后端。

`WindowsAudioOutputFactory`、`OpenAlAudioOutputFactory` 等旧领域工厂不再由 Audio
导出。平台调用方改用 `DesktopAudioOutputFactory.Shared`；游戏调用方使用
`AudioOutput` 或 `PlatformAudioOutputFactory.Shared`。公共设备 PCM 队列从
`BEngine.Audio.BufferedStereoPcmStream` 迁至 Desktop 程序集的
`BEngine.Platform.BufferedStereoPcmStream`，需要重新编译直接调用它的开发工具。
没有向 Core 增加指向包的类型转发。

AudioClip/Source/Listener 和 Mixer 数据类型身份、字段及资产格式保持不变。
DSP 插件的 `bengine_dsp_*` 是领域协议，仍属于 Audio；它不负责打开系统音频设备。
动态库句柄的加载、符号查询和卸载已进一步交给中立平台租约，Audio 保留 DSP ABI、
参数及实例引用管理；进程期 DllImport 缓存不承担插件卸载，见 NativeLibraryOwnership.md。
`AudioClip.BuildImportedBytes` 同时服务公开 Runtime 加载和 Editor 导入，不能仅因
名称包含 Build 就移入 Editor。原生解码大小、采样率和开关策略由 Audio 决定。

## 生命周期

平台不持有 Audio 对象。领域适配器负责释放获得的设备；退出、重置和所属包程序集
退休都关闭输出。Windows 保留原来的预备块池、平滑尾音、释放失败重试和设备计数。
Web 保留单线程队列以及现有 JS 桥，释放输出或解码会移除原生句柄。

## 验证

记录位于 `Temp/Engine/Verification/AudioPlatformOwnership_20261114`：

- 五个拥有者工程严格公开 API 构建通过：Web、Desktop、Android、iOS、Audio Editor。
  移动端仅编译，没有标为设备运行通过。
- 旧/新设备队列 256 帧样本与计数逐项一致；暖态 512 轮队列操作分配均为 0 B。
- 平台独立消费者 18 项：真实 Windows 设备、容量、诊断、输入和释放。
- Audio/AB 独立消费者 33 项：真实资源编解码、原生异步状态、取消/失败回退、
  配置、背压、旧工厂和程序集退休。暖态 1000 次适配器提交分配为 0 B。
- 本机 Edge 19 项：实际 WebAudio 消费、WAV 解码、单声道与增益、取消、释放；
  该浏览器探针不引用 Audio/Graphics/Editor。不是完整 Snake Web 运行验收。
- Mono.Cecil 检查领域字段、API 迁移和 Core/Platform 程序集引用；生产编译器检查
  44 个包、72 个编译节点，边界脚本检查 Core/Platform 无具体包引用。

Snake 专项记录在 `Temp/Snake/Verification/AudioPlatformOwnership_20261114`：
链接现有 AudioRegression，并运行原有设备池和 20/30/60 FPS 节奏测试方法；
设备池还覆盖 60/400 FPS、停顿、并发释放及释放失败重试。完整历史 PlayerCheck
仍有之前 SDK 迁移留下的渲染类型与重复基础类型引用错误，未声称全量工具编译通过。
冻结的 Snake RuntimeLib/PackageRepository SDK 镜像不在本次局部源代码迁移中混写；
测试明确使用同一套已构建的临时 SDK。旧 AOT 冷方法清单只移除 65 条已不存在的
Audio 原生类型记录，保留其余记录原值。

Audio DLL 从 212480 B 变为 169984 B（减少 42496 B）。这主要是归属迁移，
平台 DLL 相应增加，不能据此声称游戏总包体等量减小。未做声学回录、移动端设备
听测或完整游戏打包。全包职责审查仍在继续，Networking 等待处理项见职责地图。
