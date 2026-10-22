# Audio 运行与扩展指南

包 ID：`com.bengine.audio`。运行时命名空间：`BEngine.Audio`。数值基于 Fix64；使用引擎已有的组件、资源、场景和服务接口。

## 快速开始

1. 在 Package Manager 启用 Audio，检查依赖与 Console 编译结果；导入 AudioGettingStarted、OneShotMixer 示例。
2. 从 Project 打开示例的 res 场景，在 Hierarchy 检查对象及父子关系。
3. 同时观察 Scene 与 Game，在 Inspector 检查引用，点击 Play；完成后 Stop，并确认编辑模式资源未被运行时状态污染。

1. 导入 PCM WAV，在 Inspector 核对采样率、通道和时长；通过包的资源加载路径引用 Clip，避免硬编码主机绝对路径。
2. 在场景放置 AudioListener 与 AudioSource，给 source.clip 赋值；启用 playOnAwake 或从 Start 调用 Play。
3. 用两段短音调用 PlayOneShot，分别测试 Pause、UnPause、mute、panStereo 和 Stop；循环背景音与一次性提示音使用独立 Source。

## 组件与 API

主要类型：`AudioClip`、`AudioSource`、`AudioListener`、`AudioOutput`、`IAudioOutput`。

AudioClip.Create/GetData/SetData 管理内存 PCM。AudioSource.Play/PlayDelayed/PlayOneShot/Pause/UnPause/Stop 管理播放，time/timeSamples 查询游标。mute 只将输出增益变为零，仍推进播放位置；Pause 冻结主声部和 OneShot。isPlaying 包含仍存活的 OneShot。

```csharp
source.clip = clip;
source.loop = true;
source.Play();
source.PlayOneShot(click, Fix64.Half);
source.mute = true;
```

代码片段中的 scene、camera、资源和业务对象应来自当前场景。先确认资源加载成功再赋值；不要把资源失败吞掉后继续运行。运行时代码放进项目 runtime/Scripts 对应的 asmdef，编辑器工具放进 Editor 装配。

## 生命周期与确定性

使用 Awake 获取依赖，Start 初始化首次运行状态。启用较晚的对象仍应在首次 Update 前执行一次 Start。物理操作在 FixedUpdate，普通状态推进在 Update，相机最终跟随在 LateUpdate。通过 BTask 和 BCancellationToken 编排主线程异步流程；退出场景时取消未完成请求并解除事件订阅。

多个 SceneRuntime 应使用 TickAll 共享一次全局帧推进。录制固定输入、固定步长和资源版本后再比较结果；不要使用主机墙上时钟、后台线程或网络返回时刻直接修改模拟状态。视觉与音频的硬件输出不作为确定性逻辑输入。

## 资源分包与更新

在 AssetImporter 设置 assetBundleName/assetBundleVariant，使用 BuildPipeline.BuildAssetBundles 或播放器构建流程产生目录。跨包序列化引用会加入依赖，公共资源应分配到稳定的共享包，避免重复归属与循环依赖。需要精确构建范围时传入 AssetBundleBuild[]。

首次安装可设置 AssetBundleRuntimeOptions.RequiredBundleNames；null 表示完整安装。通过 IAssetBundleDownloadManager.GetDownloadSizeAsync 估算某组及其依赖的缺失字节，再调用 DownloadDependenciesAsync 获取可选内容。更新目录激活后，已安装可选包继续参与更新。下载完成与目录激活属于不同阶段，失败时不得先切换到不完整资源版本。

本包使用的资源、着色器及其元数据必须随包导出。以相同路径替换内容后检查 BAsset 缓存代次和运行时重新绑定；已有对象持有的资源引用不等于自动替换该对象的所有字段。请在更新成功的明确时机刷新相关资源。

## 平台与扩展

业务代码通过 FileUtility/PathUtility 调用文件和路径能力；网络功能使用所属包的 IHttpTransport、IWebSocketTransport、ITcpTransport/IUdpTransport 或包自身的服务接口。Windows/.NET 等平台适配器处理实际文件、套接字及硬件；引擎在 Poll 中交付网络结果。扩展平台时替换服务注册，不在组件内直接创建 FileStream、HttpClient、Socket 或 Task。

为 Audio 增加功能时，先查阅 `BEngine.Audio` 与 `BEngine.Audio.Editor` 的现有实现、asmdef 依赖与 Editor/Examples。保留 Unity 风格命名及本引擎二维类型；新增公共字段必须具备实际行为，暂不支持的能力应明确报错或写入限制说明。

## 行为边界

提供 WAV/MP3/Ogg Vorbis、独立流式声部、后台解码、Mixer Group 的效果和前/后推子发送。AudioClip.Create(stream:true) 需要 PCM 回调，独立声部工厂可用 CreateStream。具体的加载策略、制作操作、内存限制、跳转成本及托管/原生 C ABI v1 DSP 插件接口见 [流式音频与混音](StreamingAndMixer.md)。PCM/DSP 使用 float，播放时间和参数使用 Fix64；音频硬件时钟不作为游戏逻辑时钟。不提供 3D 空间化。

## 排查顺序

- 无声：检查 AudioListener.pause/volume、Source.mute/volume、组件 active 状态及平台 IAudioOutput 是否为空输出。
- 暂停后提示音仍响：检查是否用了不同 Source；同一 Source 的 Pause 应暂停所有声部。
- 长期静音后恢复从旧位置播放：记录 timeSamples；mute 应持续推进，Pause 才保持位置。

遇到异常时记录最小场景、包版本、平台、步长、资源路径和调用顺序。在 Console 保留第一条异常及堆栈；先复现单对象问题，再逐步恢复完整场景。画面问题应同时保存原始分辨率截图与采样配置。

## 验证清单

- 播放后静音若干帧，确认输出为零而 timeSamples 增加。
- 仅播放 OneShot 时检查 isPlaying=true，Pause 后为 false，UnPause 后继续，Stop 清理所有声部。
- 用假的 IAudioOutput 记录样本长度与饱和范围，再在目标平台听测左右声道。

- 在 Editor Play 和导出的 Windows Player 各运行一次，不以编辑器能加载资源推断包内资源完整。
- 分别测试冷启动、重复启停、资源更新后重启、失败后重试；记录实际执行的平台，未运行的移动端不能标为通过。
- 使用固定输入测稳态 Update/Render 的分配与耗时。首次资源解码、着色器编译和缓存预热应单独记录。

## Skills 与示例

本包额外提供 `bengine-audio-authoring`、`bengine-audio-runtime` 与 `bengine-audio-validation` 三个任务入口，分别覆盖配置、实现与验证。示例通过 Package Manager 导入为独立目录；先运行原始示例，再修改一个参数观察差异，保留原始资源 GUID 以验证重复导入行为。
