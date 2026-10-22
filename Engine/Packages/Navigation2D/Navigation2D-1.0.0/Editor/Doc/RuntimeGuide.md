# Navigation2D 运行与扩展指南

包 ID：`com.bengine.navigation2d`。运行时命名空间：`BEngine.Navigation2D`。数值基于 Fix64；使用引擎已有的组件、资源、场景和服务接口。

## 快速开始

1. 在 Package Manager 启用 Navigation2D，检查依赖与 Console 编译结果；导入 NavigationSurfaceAndAgent、DynamicRebake 示例。
2. 从 Project 打开示例的 res 场景，在 Hierarchy 检查对象及父子关系。
3. 同时观察 Scene 与 Game，在 Inspector 检查引用，点击 Play；完成后 Stop，并确认编辑模式资源未被运行时状态污染。

1. 创建 Surface，设置 bounds/cellSize 和障碍层，添加 Collider2D 障碍后执行 Bake。先观察烘焙结果，再创建 Agent。
2. 用 SamplePosition 验证目标落在可行走区域，给 SetDestination 的返回值和 pathStatus 加日志。PathPartial 只能走到已找到的可达点。
3. 用 Modifier 设置区域并用 areaMask 过滤；添加 Link 连接两个区域，重建 Surface 后验证单向/双向和 costModifier。

## 组件与 API

主要类型：`NavigationSurface2D`、`NavigationAgent2D`、`NavigationPath2D`、`NavigationObstacle2D`、`NavigationModifier2D`、`NavigationLink2D`、`Navigation2D`。

Surface.BuildNavigation 重建格子；All/Volume/Children 控制场景、范围和子层级采集，PhysicsColliders/Sprites 控制几何来源，Surface 变换会作用于格网。Navigation2D.CalculatePath/SamplePosition 使用区域掩码，带 agentTypeId 的 SamplePosition 重载和 Agent 请求只选择匹配 Surface。Agent.SetDestination 返回是否找到可用路径；SetPath 保留独立副本；remainingDistance/velocity/pathStatus 描述运动状态。updatePosition=false 时通过 nextPosition 获取模拟位置。

```csharp
surface.BuildNavigation();
agent.speed = (Fix64)3;
agent.stoppingDistance = Fix64.Parse("0.1");
bool accepted = agent.SetDestination(destination);
NavigationPathStatus status = agent.pathStatus;
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

为 Navigation2D 增加功能时，先查阅 `BEngine.Navigation2D` 与 `BEngine.Navigation2D.Editor` 的现有实现、asmdef 依赖与 Editor/Examples。保留 Unity 风格命名及本引擎二维类型；新增公共字段必须具备实际行为，暂不支持的能力应明确报错或写入限制说明。

## 行为边界

采用离散格网而非 Unity 多边形 NavMesh。Agent 半径参与局部避让，格网净空取决于 Surface 烘焙配置。局部避让是轻量的二维排斥，不是 ORCA，也不保证密集队列无死锁。不同 avoidance quality 当前不提供不同求解器。不要把区域编号直接当掩码，使用 1UL << area。

## 排查顺序

- 目标在障碍中：允许得到 PathPartial；不得把阻挡中的原始目标追加到路径末尾。
- 高速角色越过终点：核对 stoppingDistance、autoBraking 与每步位移限制，运动应被当前拐点距离截断。
- 障碍移动后仍走旧路：更新障碍并 BuildNavigation；autoRepath 监听烘焙代次，不能代替烘焙。

遇到异常时记录最小场景、包版本、平台、步长、资源路径和调用顺序。在 Console 保留第一条异常及堆栈；先复现单对象问题，再逐步恢复完整场景。画面问题应同时保存原始分辨率截图与采样配置。

## 验证清单

- 分别测试 mask=0、仅某区域、全区域和不可达目标，检查返回值与 pathStatus。
- 设很大 speed 和较大 Tick，确认不会越过拐点，在终点速度归零。
- 修改传给 SetPath 的原始 corners 后，确认 Agent 的路径不改变；重烘焙后检查 autoRepath。

- 在 Editor Play 和导出的 Windows Player 各运行一次，不以编辑器能加载资源推断包内资源完整。
- 分别测试冷启动、重复启停、资源更新后重启、失败后重试；记录实际执行的平台，未运行的移动端不能标为通过。
- 使用固定输入测稳态 Update/Render 的分配与耗时。首次资源解码、着色器编译和缓存预热应单独记录。

## Skills 与示例

本包额外提供 `bengine-navigation2d-authoring`、`bengine-navigation2d-runtime` 与 `bengine-navigation2d-validation` 三个任务入口，分别覆盖配置、实现与验证。示例通过 Package Manager 导入为独立目录；先运行原始示例，再修改一个参数观察差异，保留原始资源 GUID 以验证重复导入行为。
