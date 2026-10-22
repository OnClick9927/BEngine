# Physics2D 运行与扩展指南

新增几何/动力学能力与已知边界见 [多边形与刚体求解](GeometryAndDynamics.md)。

包 ID：`com.bengine.physics2d`。运行时命名空间：`BEngine.Physics2D`。数值基于 Fix64；使用引擎已有的组件、资源、场景和服务接口。

## 快速开始

1. 在 Package Manager 启用 Physics2D，检查依赖与 Console 编译结果；导入 RigidbodyAndQueries、TriggersAndQueries 示例。
2. 从 Project 打开示例的 res 场景，在 Hierarchy 检查对象及父子关系。
3. 同时观察 Scene 与 Game，在 Inspector 检查引用，点击 Play；完成后 Stop，并确认编辑模式资源未被运行时状态污染。

1. 用静态边界和一个动态 Rigidbody2D 创建最小场景，添加 Collider 并确认尺寸、层和质量。
2. 将力输入写在 FixedUpdate，以固定步长观察落地与反弹；通过 layerMask 排除不相关物体。
3. 增加 isTrigger 区域并记录进入、停留、退出；分别禁用 Collider、GameObject 和 Rigidbody2D.simulated，比较预期行为。

## 组件与 API

主要类型：`Rigidbody2D`、`BoxCollider2D`、`CircleCollider2D`、`Collider2D`、`Physics2D`、`RaycastHit2D`。

Rigidbody2D.simulated=false 同时退出积分、碰撞和空间查询；重新启用保留原速度。力与速度应从 FixedUpdate 管理。Raycast/Overlap 查询使用引擎二维向量和 LayerMask。Collider 的触发事件和接触回调仍在主线程执行。

```csharp
body.simulated = true;
body.linearVelocity = new Vector2((Fix64)2, Fix64.Zero);
// Disable simulation and remove attached colliders from queries.
body.simulated = false;
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

为 Physics2D 增加功能时，先查阅 `BEngine.Physics2D` 与 `BEngine.Physics2D.Editor` 的现有实现、asmdef 依赖与 Editor/Examples。保留 Unity 风格命名及本引擎二维类型；新增公共字段必须具备实际行为，暂不支持的能力应明确报错或写入限制说明。

## 行为边界

这是独立二维定点物理实现，不能假设与 Unity Box2D 数值轨迹相同。刚体与查询的参数使用 Fix64。大量密集碰撞体仍会提高接触求解成本；按场景范围拆分并限制活动物体数量。业务确定性依赖固定输入顺序和一致步长。

## 排查顺序

- 停止的刚体仍挡路：确认设置的是 simulated=false，而不是仅把速度设为零。
- 快物体穿透：减小固定步长、检查 Collider 厚度及当前支持的碰撞检测模式；不要在 Update 直接跳过障碍移动。
- 多场景重置物理时间：用 SceneRuntime.TickAll 推进多个运行时，共享单次全局帧推进。

遇到异常时记录最小场景、包版本、平台、步长、资源路径和调用顺序。在 Console 保留第一条异常及堆栈；先复现单对象问题，再逐步恢复完整场景。画面问题应同时保存原始分辨率截图与采样配置。

## 验证清单

- 将 simulated=false 的物体放在射线中，确认它不参与查询且位置不变化。
- 重新启用后验证再次参与碰撞；观察 Stop/销毁后没有遗留接触。
- 相同固定步长和输入运行两次，比较位置与速度的 Fix64.RawValue，而不是格式化小数。

- 在 Editor Play 和导出的 Windows Player 各运行一次，不以编辑器能加载资源推断包内资源完整。
- 分别测试冷启动、重复启停、资源更新后重启、失败后重试；记录实际执行的平台，未运行的移动端不能标为通过。
- 使用固定输入测稳态 Update/Render 的分配与耗时。首次资源解码、着色器编译和缓存预热应单独记录。

## Skills 与示例

本包额外提供 `bengine-physics2d-authoring`、`bengine-physics2d-runtime` 与 `bengine-physics2d-validation` 三个任务入口，分别覆盖配置、实现与验证。示例通过 Package Manager 导入为独立目录；先运行原始示例，再修改一个参数观察差异，保留原始资源 GUID 以验证重复导入行为。
