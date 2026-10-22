# Property Attributes 运行与扩展指南

包 ID：`com.bengine.property-attributes`。运行时命名空间：`BEngine.PropertyAttributes`。数值基于 Fix64；使用引擎已有的组件、资源、场景和服务接口。

## 快速开始

1. 在 Package Manager 启用 Property Attributes，检查依赖与 Console 编译结果；导入 AttributesGallery、InspectorAttributesAndDrawer 示例。
2. 从 Project 打开示例的 res 场景，在 Hierarchy 检查对象及父子关系。
3. 同时观察 Scene 与 Game，在 Inspector 检查引用，点击 Play；完成后 Stop，并确认编辑模式资源未被运行时状态污染。

1. 创建可序列化 MonoBehaviour 字段或属性，先使用 Range 与 Required，在 Inspector 确认 SerializedProperty 能找到成员。
2. 为 ShowIf 或 ValidateInput 指定对象上真实存在的条件成员/方法；分别验证条件通过与失败时的布局。
3. 逐步组合 Title、InfoBox、Prefix/Suffix、InlineButton，调整 order；窄 Inspector 中确认输入框和提示不重叠。

## 组件与 API

主要类型：`ExtendedPropertyAttribute`、`ExtendedPropertyDrawer`、`ShowIf`、`Required`、`ValidateInput`、`EnumFlags`、`Prefix`、`Suffix`、`HorizontalLine`、`InlineButton`。

属性继承 ExtendedPropertyAttribute，由一个 useForChildren 的 PropertyDrawer 组合处理。ShowIf/HideIf 决定可见性，EnableIf/DisableIf/ReadOnly 决定可编辑性。Required/ValidateInput 提示计入 GetPropertyHeight；Prefix/Suffix/HorizontalLine 和 EnumFlags 实际参与绘制。InlineButton 通过 Undo.RecordObject 记录动作。

```csharp
[Required("Configuration is required.")]
public string config = string.Empty;
[Prefix("HP"), Suffix("points"), Range(0, 100)]
public Fix64 health = (Fix64)75;
[HorizontalLine, EnumFlags]
public Features features;
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

为 Property Attributes 增加功能时，先查阅 `BEngine.PropertyAttributes` 与 `BEngine.Editor` 的现有实现、asmdef 依赖与 Editor/Examples。保留 Unity 风格命名及本引擎二维类型；新增公共字段必须具备实际行为，暂不支持的能力应明确报错或写入限制说明。

## 行为边界

这是 BEngine IMGUI PropertyDrawer 扩展，不是完整 Odin Inspector。条件和验证方法应快速且无副作用，因为布局和重绘都可能求值。运行时代码不能引用 Editor 装配；自定义 drawer 放在 Editor 目录和编辑器 asmdef 中。不同属性的约束需要保持上下界一致。

## 排查顺序

- 字段消失：检查 ShowIf 条件名和返回值；不存在的条件会判为 false。
- 提示盖住下一项：使用 EditorGUI.GetPropertyHeight 分配 Rect，不能固定写死单行高度。
- 编辑后无法撤销：通过 SerializedProperty 写入并 ApplyModifiedProperties；直接修改目标前调用 Undo.RecordObject。

遇到异常时记录最小场景、包版本、平台、步长、资源路径和调用顺序。在 Console 保留第一条异常及堆栈；先复现单对象问题，再逐步恢复完整场景。画面问题应同时保存原始分辨率截图与采样配置。

## 验证清单

- 切换条件使错误提示出现/消失，验证高度与实际绘制行数一致。
- 在绘制失败或结束后确认 GUI.enabled 和 EditorGUI.indentLevel 恢复，避免污染后续字段。
- 用 Flags 枚举选择多个位，再撤销；InlineButton 修改字段后撤销并确认场景 dirty 状态。

- 在 Editor Play 和导出的 Windows Player 各运行一次，不以编辑器能加载资源推断包内资源完整。
- 分别测试冷启动、重复启停、资源更新后重启、失败后重试；记录实际执行的平台，未运行的移动端不能标为通过。
- 使用固定输入测稳态 Update/Render 的分配与耗时。首次资源解码、着色器编译和缓存预热应单独记录。

## Skills 与示例

本包额外提供 `bengine-property-attributes-authoring`、`bengine-property-attributes-runtime` 与 `bengine-property-attributes-validation` 三个任务入口，分别覆盖配置、实现与验证。示例通过 Package Manager 导入为独立目录；先运行原始示例，再修改一个参数观察差异，保留原始资源 GUID 以验证重复导入行为。
