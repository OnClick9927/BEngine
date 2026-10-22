# Animation 运行与扩展指南

包 ID：`com.bengine.animation`。运行时命名空间：`BEngine`。数值基于 Fix64；使用引擎已有的组件、资源、场景和服务接口。

## 快速开始

1. 在 Package Manager 启用 Animation，检查依赖与 Console 编译结果；导入 AnimationGettingStarted、StateMachine 示例。
2. 从 Project 打开示例的 res 场景，在 Hierarchy 检查对象及父子关系。
3. 同时观察 Scene 与 Game，在 Inspector 检查引用，点击 Play；完成后 Stop，并确认编辑模式资源未被运行时状态污染。

1. 创建 Clip，绑定 Transform.localPosition.x 或 localRotation；关键帧时间、数值和切线使用 Fix64。先用两个关键帧确认插值方向。
2. 创建 Controller，加入命名状态和默认状态。将 Controller 指向 Animator；先调用 Play 验证绑定，再添加参数条件与退出时间。
3. 设置 CrossFade 的归一化时长，观察过渡中点和终点。为事件添加 MonoBehaviour 接收方法，跨越多个循环检查次数和顺序。

## 组件与 API

主要类型：`AnimationCurve`、`AnimationClip`、`AnimatorController`、`AnimatorState`、`Animator`、`Animation`。

Animator.Play(name) 立即选择状态；CrossFade(name, normalizedDuration) 按源 Clip 长度计算过渡时间。SetFloat/SetInteger/SetBool/SetTrigger 更新参数，IsInTransition(0) 查询实际过渡。applyRootMotion 将根 Transform 的位移和旋转增量应用到对象；deltaPosition/deltaRotation 暴露本次增量。

```csharp
animator.Play("Idle");
animator.SetFloat("Speed", Fix64.One);
animator.CrossFade("Run", Fix64.Parse("0.2"));
animator.applyRootMotion = true;
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

为 Animation 增加功能时，先查阅 `BEngine.Animation` 与 `BEngine.Animation.Editor` 的现有实现、asmdef 依赖与 Editor/Examples。保留 Unity 风格命名及本引擎二维类型；新增公共字段必须具备实际行为，暂不支持的能力应明确报错或写入限制说明。

## 行为边界

Animator 提供基础层、覆盖/叠加层、路径遮罩、同步层，以及一维/二维/嵌套混合树。二维使用归一化梯度带混合；同步层跟随前序层的状态、归一化时间和过渡，通过完整路径相同的状态覆盖动作，事件由源层派发。子状态机支持局部/相对/绝对路径、条件入口、默认状态、祖先 Any State 和向父图传播的 Exit。图中右键创建、双击进入、顶部层级导航返回；重命名和删除会修复引用并支持 Undo。入口和状态机出口路由立即解析，淡入时长由触发路由的叶状态/Any State 过渡控制。根运动只解释本引擎二维根 Transform 曲线。中断正在进行的过渡时，源状态使用当前目标状态，不保存完整混合姿态。

Animation 支持 Sprite/资源离散帧、布尔轨道、Color.r/g/b/a 和 Vector2.x/y 通道；数值关键帧支持 Free/Linear/Constant/Auto/ClampedAuto 及入/出切线权重。对象与布尔值在关键帧时刻切换，Animator/Timeline 混合时选择权重最大的贡献，相同权重取后者。使用 Animation 窗口 Add Property 或 Record 创建轨道；停止预览会恢复原值，对象键可以跨 Clip 复制。

新 Clip 保存为版本 2，仍可读取版本 1。对象帧使用引擎资源引用，保存前需要资源具有持久身份；不能把场景对象保存进 Clip。切线和曲线求值使用 Fix64，加权切线求解 Bezier 的时间坐标。

## 排查顺序

- 画面不动：核对 Clip 路径、绑定成员大小写、对象相对路径、组件启用状态与默认状态名称。
- 事件重复或缺失：记录归一化时间和播放方向，检查同一时刻是否在 Clip 内重复定义事件；事件回调调用 Play 会终止旧状态剩余回调。
- CPU 读文件：Clip/Controller 应经 BAsset 缓存加载；热更新激活后缓存代次改变，下次访问重新加载，勿在 Update 手工解析 YAML。

遇到异常时记录最小场景、包版本、平台、步长、资源路径和调用顺序。在 Console 保留第一条异常及堆栈；先复现单对象问题，再逐步恢复完整场景。画面问题应同时保存原始分辨率截图与采样配置。

## 验证清单

- 让一次 Tick 跨越 2.5 个循环，再反向跨越 2 个循环，确认事件和根位移都连续。
- 对相同两个常量姿态，在过渡一半时检查数值为中间值，在结束时 IsInTransition(0) 为 false。
- 在独立场景和 TickAll 的多场景模式下比较同样输入的状态结果。

- 在 Editor Play 和导出的 Windows Player 各运行一次，不以编辑器能加载资源推断包内资源完整。
- 分别测试冷启动、重复启停、资源更新后重启、失败后重试；记录实际执行的平台，未运行的移动端不能标为通过。
- 使用固定输入测稳态 Update/Render 的分配与耗时。首次资源解码、着色器编译和缓存预热应单独记录。

## Skills 与示例

本包额外提供 `bengine-animation-authoring`、`bengine-animation-runtime` 与 `bengine-animation-validation` 三个任务入口，分别覆盖配置、实现与验证。示例通过 Package Manager 导入为独立目录；先运行原始示例，再修改一个参数观察差异，保留原始资源 GUID 以验证重复导入行为。
