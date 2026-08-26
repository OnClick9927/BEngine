# BEngine Core

BEngine Core 是引擎与编辑器的内置基础层，提供 2D Scene、GameObject、Component、Transform、Camera2D、Sprite、粒子、资源系统、序列化、输入、生命周期和 Unity 风格编辑器扩展 API。Core 始终可用，不需要在 `Packages.yaml` 中启用，也不依赖 Animation、Physics2D、Navigation2D、TiledMap 或 UIElements。

![Core 编辑器概览](Doc/images/overview.png)

## 包概览与依赖

Core 是所有工程与扩展包的共同依赖，也是唯一不通过 Package Manager 卸载的功能集合。扩展包只允许从自身指向 Core；Core 不保存具体扩展包注册，保证运行时可按工程清单组合。

### 程序集与目录

| 项目 | 用途 | 运行时可用 |
| --- | --- | --- |
| `BEngine` | Scene、GameObject、组件、资源、渲染、输入和数学 API | 是 |
| `BEngine.Editor` | 编辑器宿主、Inspector、菜单、窗口、Package Manager 和资源导入 | 否 |
| `BEngine.Launcher` | 工程选择与编辑器启动 | 否 |
| `BEngine.Player` | 导出游戏的通用运行时入口 | 是 |
| `Resources` | 默认 Shader 等运行时资源 | 是 |
| `Editor` | 图标、离线文档、示例和 AI Skill | 否 |

Core 使用 `Microsoft.Extensions.DependencyInjection` 管理宿主与 Scene scope，并使用 YAML 文档保存工程、场景、Prefab 和资源元数据。运行时是单线程模型；编辑器可以在后台执行扫描、编译、哈希和文件 IO，但不能从后台线程修改场景对象。

## 可导入示例

| 示例 | 导入目录 | 场景 | 内容 |
| --- | --- | --- | --- |
| Core Getting Started | `Assets/Examples/CoreGettingStarted` | `res/Core.scene.yaml` | MonoBehaviour 生命周期、输入、协程、Transform、SpriteRenderer、Camera2D 和 EditorWindow |

### 快速开始：从 Import 到 Play

1. 启动编辑器，打开 `Window > Package Manager`。
2. 在左侧选择 `BEngine Core`，切换到 `Examples` 页签。
3. 在 `Core Getting Started` 行点击 `Import`。导入完成后按钮会变为 `Reimport`。
4. 在 Project 窗口打开 `Assets/Examples/CoreGettingStarted/res/Core.scene.yaml`。
5. 等待脚本编译完成，确认 Console 没有错误，然后点击顶部 Play 按钮。
6. 使用 Horizontal/Vertical 输入轴移动前景 Sprite，按 `Space` 切换颜色；Console 会显示 Awake、OnEnable、Start 和协程日志。
7. 停止 Play。运行时创建的 Camera、背景和伴随 Sprite 会随运行镜像丢弃，原始 Scene 资产不会被改写。
8. 打开 `Tools > Examples > BEngine Editor Window` 查看 `MenuItem`、EditorWindow 生命周期、Selection 和 GenericMenu 示例。

`Reimport` 默认复用未变化文件，并保留本地修改。需要一份干净参考时，先复制导入目录，再按 Package Manager 的提示选择覆盖策略。

## 组件字段表

| 组件 | 字段 | 说明 |
| --- | --- | --- |
| `Transform` | `localPosition`, `localRotation`, `localScale` | 本地二维位置、角度和缩放 |
| `Transform` | `position`, `rotation`, `lossyScale` | 计算后的世界值；层级变化由 `SetParent` 管理 |
| `Camera2D` | `size` | 正交相机半高，最小值为 0.001 |
| `Camera2D` | `backgroundColor`, `clearMode` | Color、DepthOnly 或 Nothing 清屏策略 |
| `Camera2D` | `priority`, `isMain` | 多相机按 priority 从小到大稳定渲染 |
| `Camera2D` | `cullingMask` | 63 个 Sorting Layer 的位掩码 |
| `Camera2D` | `viewportRect` | 左下角原点、0 到 1 的归一化视口 |
| `Renderer2D` | `sortingLayer`, `orderInLayer`, `opacity` | 所有 2D Renderer 共用的排序与透明度字段 |
| `SpriteRenderer` | `sprite`, `size`, `pivot`, `useSpritePivot` | Sprite 资源、尺寸与轴心 |
| `SpriteRenderer` | `color`, `flipX`, `flipY`, `material` | 着色、翻转与材质；Atlas 由 Sprite 引用自动反查 |
| `ParticleSystem2D` | `duration`, `loop`, `playOnAwake`, `emissionRate` | 发射周期与自动播放 |
| `ParticleSystem2D` | `startLifetime`, `startSpeed`, `startDirection`, `startSize` | 新生粒子的寿命、速度、方向和尺寸 |
| `ParticleSystem2D` | `startColor`, `startRotation`, `startAngularVelocity` | 新生粒子的颜色和旋转 |
| `ParticleSystem2D` | `maxParticles`, `sprite`, `atlas`, `material` | 容量、视觉资源与合批键 |

世界 Renderer 只能使用 `2^1` 到 `2^58`；最高五层 `2^59` 到 `2^63` 保留给 UI。最终顺序由 Layer、Order in Layer、Hierarchy、透明度和提交顺序共同决定，Material、Shader、Atlas 决定相邻提交能否合批。

## 资源字段表与导入参数

| 资源 | 关键字段或扩展名 | 说明 |
| --- | --- | --- |
| `Scene` | `*.scene.yaml` | 保存 GameObject、Transform 和 Component 数据 |
| `PrefabAsset` | `*.prefab.yaml` | 可实例化并 Apply/Revert 的对象层级 |
| `Sprite` | `.png` + `.meta`: `textureType`, `spritePivotX/Y` | 将 PNG 的 Texture Type 设为 Sprite；同一图片可赋给 SpriteRenderer 或加入 Atlas |
| `TextureAtlas` | `*.atlas.yaml`: `MaxSize`, `Padding`, `Extrude`, `SpriteReferences` | 在 `Window > 2D > Texture Atlas` 构建 |
| `Material` | `*.material.yaml`: `shader`, `color`, `renderQueue` | 可保存 Color、Fix64、Vector4 和 Int 属性 |
| `GUISkin` | `*.guiskin.yaml`: `palette`, 内置样式槽, `customStyles` | 编辑器主题资产；继承 `BAsset`，仅存在于 `BEngine.Editor` 程序集 |
| `Texture` | `.png` | Inspector Import Settings 写入相邻 `.meta`；当前运行时解码器仅支持 PNG |
| `TextureImporter` | `compressionFormat`, `filterMode`, `wrapMode` | 请求的压缩、过滤和寻址模式 |
| `TextureImporter` | `generateMipMaps`, `maxTextureSize`, `pixelsPerUnit` | MipMap、32 到 16384 的 2 次幂尺寸和 PPU |
| `Font`, `Shader`, `Script`, `TextAsset` | 对应源文件 | 均遵循 BAsset 的稳定路径和 GUID 契约 |

Inspector 中修改 Texture 导入参数后必须点击 `Apply`；`Revert` 会重新读取 `.meta`。压缩格式是导入请求，当前后端不支持的 GPU 转码不会伪装为已完成，但请求会稳定保留。

## 运行时 API

| API | 用途 |
| --- | --- |
| `scene.CreateGameObject(name)` | 在指定 Scene 创建对象 |
| `gameObject.AddComponent<T>()` | 添加组件并自动满足 `RequireComponent` |
| `GetComponent<T>()` / `TryGetComponent<T>()` | 查询当前对象组件 |
| `scene.QueryComponents<T>()` | 查询 Scene 中所有匹配组件 |
| `transform.Translate/Rotate/SetParent` | 2D 空间变换与层级操作 |
| `SceneManager.LoadScene` | Single 或 Additive 加载场景 |
| `StartCoroutine`, `Invoke`, `CancelInvoke` | MonoBehaviour 定时与协程 |
| `BAsset.Load<T>(path)` | 按 Assets/Packages 稳定路径加载资源 |
| `Object.Instantiate`, `Object.Destroy` | 复制与销毁运行时对象 |

```csharp
using BEngine;

[AddComponentMenu("Gameplay/Player Mover")]
public sealed class PlayerMover : MonoBehaviour
{
    public Fix64 speed = 4;
    private SpriteRenderer? _renderer;

    public override void Awake() => _renderer = GetComponent<SpriteRenderer>();

    public override void Update()
    {
        var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        transform.Translate(input * speed * Time.deltaTime, Space.World);
        if (Input.GetKeyDown(KeyCode.Space) && _renderer is not null)
            _renderer.color = _renderer.color == Color.white ? Color.red : Color.white;
    }
}
```

生命周期顺序为 `Awake -> OnEnable -> Start -> FixedUpdate/Update/LateUpdate -> OnDisable -> OnDestroy`。运行时修改只发生在 Play 镜像；停止 Play 后编辑器继续显示播放前的 Scene 和组件数据。

## 编辑器 API

| API/特性 | 用途与注册方式 |
| --- | --- |
| `[MenuItem("Tools/...")]` | 程序集加载时自动发现静态菜单方法；同路径布尔方法用于验证 |
| `EditorWindow.GetWindow<T>()` | 创建或聚焦可停靠窗口 |
| `[CustomEditor(typeof(T), true)]` | 为目标类型及可选子类注册 Inspector |
| `[CustomPropertyDrawer(typeof(T))]` | 为字段类型或 PropertyAttribute 注册 Drawer |
| `SerializedObject`, `SerializedProperty` | 统一多选、Undo 和字段绘制 |
| `EditorGUI.ObjectField` | 为 BObject/BAsset 字段提供选择、拖放和类型校验 |
| `Undo.RecordObject`, `EditorUtility.SetDirty` | 正确记录编辑态修改 |
| `AssetDatabase.LoadAssetAtPath<T>()` | 通过工程路径加载编辑器资源 |
| `Selection.activeObject` | 同步 Project、Hierarchy、Scene 和 Inspector 选择 |
| `ScenePickingProviderRegistry.Register` | 让外部 Renderer 参与 Scene 点击选取 |
| `GUI.skin` | 获取或设置当前 `GUISkin`；控件从中解析对应的默认 `GUIStyle` |
| `EditorAppearance.SetSkin` | 将指定 Skin 应用到整个编辑器并重绘所有窗口 |

### 自定义编辑器主题

`GUISkin` 继承 `BAsset`，自定义主题以 `*.guiskin.yaml` 保存在工程中，因此可以创建多份、纳入版本控制并在不同工程成员之间共享。每个 Skin 包含编辑器调色板、Button、Label、TextField、Toolbar、Dock Tab、Tree View、Inspector 等全部内置样式槽，以及可由扩展包增加的 `customStyles`。`GUIStyle` 保存 Normal、Hover、Active、Focused、On 与 Disabled 状态的文字色、背景色、边框色、背景图片和布局参数。

1. 打开 `Edit > Preferences > General`，在 `Theme` 列表查看 Light、Dark、Classic 和工程内所有自定义 Skin。
2. 内置 Light、Dark、Classic 可以通过对应 ObjectField 选中并在 Inspector 查看，但它们是只读资源，不能修改或删除。
3. 点击 `New` 会复制当前主题，在当前 Project 文件夹创建一份可重命名的 `*.guiskin.yaml`。也可以使用 `Assets > Create > GUI > GUISkin` 创建独立资产。
4. 点击 Skin 的 ObjectField 会把它设为 `Selection.activeObject`，随后在 Inspector 编辑 Palette、内置样式和 `customStyles`；点击 Inspector 的 Apply 保存。
5. 回到 Preferences 点击 `Set` 将该 Skin 应用到整个编辑器。`Delete` 只对自定义 Skin 可用；删除正在使用的 Skin 后会回到 Dark。

扩展编辑器时，`GUI`、`EditorGUI`、`GUILayout`、`EditorGUILayout` 和 `EditorToolbar` 的绘制重载都可以接收 `GUIStyle?`。传入具体样式会覆盖主题；传入 `null` 则按控件语义回退到当前 `GUI.skin` 的对应槽，例如 Button 使用 `GUI.skin.button`、文本输入使用 `GUI.skin.textField`、工具栏按钮使用 `GUI.skin.toolbarButton`。因此扩展窗口应优先使用 `null` 或 `EditorStyles`，只在需要包专用外观时使用 `customStyles`。

```csharp
var skin = AssetDatabase.LoadAssetAtPath<GUISkin>("Assets/Editor/Studio.guiskin.yaml");
if (skin is not null)
    EditorAppearance.SetSkin(skin); // 应用全局主题并重绘窗口

GUI.Button(buttonRect, new GUIContent("Run"), style: null); // 当前 skin.button
var accent = GUI.skin.FindStyle("MyPackage/AccentButton") ?? GUI.skin.button;
GUILayout.Button("Build", accent);
```

直接赋值 `GUI.skin` 适合底层宿主或测试；编辑器工具应调用 `EditorAppearance.SetSkin`，以同步 `EditorAppearance.palette`、主题状态和窗口重绘。

## 完整编辑器工作流

1. 从 `File > New Scene` 创建场景，并立刻保存到 `Assets/Scenes`。
2. 使用 `GameObject > Camera 2D` 创建相机；在 Inspector 设置 size、priority、clearMode、cullingMask 和 viewportRect。
3. 在 Project 选中 PNG，在 Inspector 将 `Texture Type` 设为 `Sprite`，按需调整 Pivot/PPU，然后点击 `Apply`；使用 `GameObject > 2D Object > Sprite` 创建对象并把这张图片赋给 SpriteRenderer。
4. 需要 Atlas 时，从 `Assets > Create > 2D > Texture Atlas` 创建 Atlas，在 `Window > 2D > Texture Atlas` 添加选中的 Sprite 模式图片并 Build。同一图片仍直接用于 SpriteRenderer，无需创建第二份资源。
5. 在 `Edit > Project Settings > Tags and Layers` 管理 Tag 与全部 Sorting Layer。
6. 用 W/E/R 切换移动、旋转、缩放 Handle；Scene 点击对象会同步 Hierarchy 选择。
7. 进入 Play 验证运行时行为。Play 期间不要尝试保存 Scene 或组件变更；停止后原数据会恢复。
8. 检查 Game View 的分辨率和 Status 统计，再通过 `File > Save Scene` 保存编辑态修改。

## 扩展 Core

### 可运行 EditorWindow

把以下脚本放入 Editor 程序集。`MenuItem` 和 `EditorWindow` 不需要手工调用注册器，编辑器在程序集加载后自动发现。

```csharp
using BEngine;
using BEngine.Editor;

public sealed class SelectionInfoWindow : EditorWindow
{
    [MenuItem("Tools/My Package/Selection Info")]
    private static void Open() => GetWindow<SelectionInfoWindow>("Selection Info");

    protected override void OnEnable() => Selection.selectionChanged += Repaint;
    protected override void OnDisable() => Selection.selectionChanged -= Repaint;

    protected override void OnGUI()
    {
        EditorGUILayout.LabelField("当前选择", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(Selection.activeObject?.name ?? "None");
    }
}
```

### 注册 Scene 运行系统

实现 `ISceneRuntimeSystem` 后可通过扩展包的 `IEngineServiceModule` 注入。模块会在包程序集启用时发现，禁用包时作用域和注册会被清理。

```csharp
using BEngine;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class CombatActor : MonoBehaviour
{
    public void Tick(Fix64 deltaTime) => transform.Rotate(deltaTime * 45);
}

public sealed class CombatSystem : ISceneRuntimeSystem
{
    public string packageId => "com.example.combat";
    public int order => 200;
    public void Update(Scene scene, Fix64 deltaTime)
    {
        foreach (var actor in scene.QueryComponents<CombatActor>())
            actor.Tick(deltaTime);
    }
}

public sealed class CombatModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISceneRuntimeSystem, CombatSystem>());
}
```

## 调试与常见问题

| 问题 | 检查与处理 |
| --- | --- |
| Game 窗口没有画面 | 确认 Scene 中有启用的 Camera2D、cullingMask 包含对象层、viewportRect 非零且对象位于正交范围内 |
| Scene 能看到但 Game 看不到 | 检查 Renderer sortingLayer、Camera cullingMask、Transform、opacity 和 Camera size |
| Sprite 只显示纯色 | 检查图片的 `Texture Type = Sprite` 是否已 Apply、源图 `.meta` 和 Atlas 是否已 Build；未指定 Sprite 时纯色 Quad 是正常行为 |
| 不能合批 | Material、Shader、解析后的 Atlas 必须相同，而且排序后必须相邻 |
| Texture 参数重选后丢失 | 修改后点击 Apply，并确认源文件旁 `.meta` 可写；Reimport 后查看 Console |
| Play 停止后对象消失 | Play 中创建的对象属于运行镜像，停止后丢弃是设计行为 |
| Play 中修改被保存 | 不应发生；记录 Editor 日志并检查自定义工具是否绕过 Play 状态直接写 YAML |
| Add Component 找不到脚本 | 检查编译错误、程序集定义依赖和脚本类型是否为非抽象 Component |
| 包菜单或 Drawer 不出现 | 确认代码位于 Editor 程序集，等待编译完成并查看 Console 的功能边界错误 |
| 文档或示例按钮不可用 | Play Mode、编译或包切换期间会禁用导入；回到 Edit Mode 后重试 |

离线网页手册位于 `Editor/Doc/index.html`，可从 `Help > Documentation` 或 Package Manager 打开。

旧项目中的 `*.sprite.yaml` 只保留读取兼容；新内容不要再创建或编辑这类描述符。
