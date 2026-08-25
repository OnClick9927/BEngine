# BEngine Core

核心目录包含四个入口程序集：

- `BEngine`：纯运行时引擎 API，不包含 PackageManager，也不记录任何具体包。
- `BEngine.Editor`：编辑器宿主、扩展 API、PackageManager 和 Codex。
- `BEngine.Launcher`：工程选择与启动器。
- `BEngine.Player`：通用运行时入口，按导出内容装载扩展程序集。

`Resources` 存放引擎运行时默认资源；`EditorResources` 存放编辑器图标、样式和其他编辑器专用资源。离线网页文档位于 `EditorResources/Doc/index.html`，可从 Package Manager 直接打开；AI Skill 统一放在 `EditorResources/Skills/<skill-name>/`。内置 Codex 会提供 Core Skill，并发现当前工程已启用包中的 Skill；它只传递 `SKILL.md` 路径，相关任务才读取正文。核心不是扩展包，因此本目录没有 `package.yaml`。

四个程序集项目目录内只存放 C# 代码和 `.csproj`。窗口图标等资源也不嵌入程序集：Editor/Launcher 从 `EditorResources` 加载，Player 从 `Resources` 加载。

## 2D 世界与渲染层

BEngine 世界只包含 X/Y 坐标与单一旋转角。`Transform` 使用 `Vector2` 位置和缩放，`Camera2D` 使用正交尺寸；Sprite、ParticleSystem2D 与 UIElements 进入同一排序和合批队列。

一个 Scene 可以包含多台启用的 `Camera2D`。相机按 `priority` 从小到大稳定渲染，并分别支持 `Color`、`DepthOnly`、`Nothing` 清屏模式、63 位 Sorting Layer 遮罩以及左下角原点的归一化 `viewportRect`。每台相机只收集遮罩允许的提交，Sprite 与粒子先做旋转包围盒视锥剔除，三角形绘制再做背面剔除；剩余提交继续遵循全局排序和合批规则。旧场景的 `depth` 字段在载入时自动迁移到 `priority`。

Sorting Layer 值只能是 `2^1` 到 `2^63`。低 58 层属于世界，最高 5 层固定属于 UI；编辑器只显示指数和层名。最终顺序由 Layer、Order in Layer、Hierarchy、透明度与提交顺序确定，Material、Shader、Atlas 共同决定相邻项目能否合批。

## Scene 选取与可见性

Scene 窗口左键按最终 `RenderSortKey2D` 从最上层选取对象；同一位置存在多个对象时，重复点击会依次循环。Hierarchy 行末的眼睛和锁按钮分别控制对象及其现有子层级是否在 Scene 中显示、是否允许 Scene 点击选取。这两个状态仅属于编辑器会话，不修改 `activeSelf`，不会标脏或序列化到 Scene，并会在 Play Mode 镜像与编辑态对象之间映射。

外部 Editor 包可通过 `ScenePickingProviderRegistry.Register` 提交 `ScenePickCandidate`，让自定义渲染组件参与同一排序、循环和可见/可选过滤。包卸载时编辑器会按提供器所属程序集清理注册，避免阻止可回收加载上下文释放。Core 的 Sprite/Particle 与 TiledMap 包均使用该选取契约。

## Texture Atlas

使用 `Assets/Create/2D/Texture Atlas` 创建 `.atlas.yaml`，再从 `Window/2D/Texture Atlas` 添加 PNG 来源并执行 Build。打包器使用确定性的无旋转 MaxRects，输出 2 的整数次方 PNG，并支持 Padding 与 Extrude 防止边缘采样渗色。

`SpriteRenderer` 和 `ParticleSystem2D` 的 `atlas` 填写 `.atlas.yaml` 路径，`sprite` 填写区域名称；两者会使用生成的归一化 UV 和 pivot。未填写 Atlas 时，`sprite` 可直接填写 PNG 路径。相邻项目只有共享 Material、Shader 和 Atlas 才会合批。

`Scene` 直接拥有 `GameObject` 集合，`GameObject` 直接拥有 Component；`Scene.QueryComponents<T>()` 提供统一的托管组件查询。对象持久身份使用 `BObject.Id`，场景 YAML 只保存 GameObject、Transform 和 Component 数据，不包含额外的实体映射。

运行时只支持单线程。`SceneRuntime` 在当前线程依次执行 MonoBehaviour 生命周期、协程和 `ISceneRuntimeSystem`，没有 World、Entity、SystemGroup 或主线程守卫层。动画、Navigation2D、Physics2D、UIElements 和场景渲染均通过 `Scene.QueryComponents<T>()` 查询组件。

有状态服务统一使用 `Microsoft.Extensions.DependencyInjection`：`BEngine` 只依赖 DI Abstractions，Editor、Launcher、Player 分别通过 `AddBEngineEditor`、`AddBEngineLauncher`、`AddBEnginePlayer` 建立组合根。项目是 scope，每个 Scene 再创建独立的场景 scope，运行时系统支持构造器注入。扩展包可实现 `IEngineServiceModule` 注册服务；编辑器为每个动态包建立独立 Provider，卸载包前先释放场景 scope 和包 Provider，避免可回收程序集被根容器持有。

`Application`、`Selection`、`AssetDatabase` 等 Unity 风格静态 API 仅作为兼容外观保留。纯数学、定点运算和无状态 YAML 转换不会为了 IoC 而包装成服务。

## 编辑器对象字段

自定义 Inspector 和 EditorWindow 可以使用 `EditorGUI.ObjectField` 或 `EditorGUILayout.ObjectField` 为任意 `BObject` 派生类型赋值。API 提供无标签、`string`、`GUIContent`、泛型和 `SerializedProperty` 重载；默认 Inspector 也会自动为 `BObject` 字段绘制同样的控件。

```csharp
target = EditorGUILayout.ObjectField("Target", target, typeof(SpriteRenderer), true)
    as SpriteRenderer;
camera = EditorGUILayout.ObjectField("Camera", camera, allowSceneObjects: true);
EditorGUILayout.ObjectField(serializedObject.FindProperty("target")!,
    typeof(SpriteRenderer), allowSceneObjects: true);
```

对象选择器支持搜索、`None`、使用当前 Selection，以及从 Project 和 Hierarchy 拖入对象。类型会在候选列表、Selection 和拖放三个入口统一校验；`allowSceneObjects: false` 只接受已经导入工程的持久资源。

## Game View 分辨率

Game 窗口工具栏可以搜索并切换常用横屏、竖屏分辨率，也可以新增或删除自定义分辨率。Free Aspect 使用 Game 面板当前像素尺寸；固定分辨率保持所选宽高比并在面板内居中显示，空余区域使用 letterbox，不会改变编辑器主窗口尺寸。

固定模式下，相机剔除、UI 布局以及 `Screen.width`、`Screen.height` 使用所选逻辑分辨率，最终画面再缩放到 Game 面板内的实际 GPU viewport。当前预设与自定义项保存在 `EditorPrefs.yaml`，不会写入 Scene 或 Project Settings。

此外，`BEngine` 提供完整的 SceneRuntime、MonoBehaviour、RuntimeLifecycle、初始化特性和 YAML 序列化回调。`BEngine.Editor` 提供 EditorApplication、EditorWindow、CustomEditor、ObjectFactory、CompilationPipeline、AssemblyReloadEvents 与资源处理器生命周期。

可导入示例位于 `EditorResources/Examples`：`CoreGettingStarted.bpackage` 展示场景、生命周期、输入、渲染和编辑器扩展，可在 Package Manager 的 `BEngine Core` 详情中导入。
