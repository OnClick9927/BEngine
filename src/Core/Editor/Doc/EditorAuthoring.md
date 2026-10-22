# 编辑器资源制作

Prefab 的 Variant、嵌套引用、覆盖项窗口和 Apply/Revert 流程见 [Prefab 制作](PrefabAuthoring.md)。

## 地图与动画入口

| 入口 | 操作 |
| --- | --- |
| Window > 2D > Tile Palette | Palette 窗口选取多格图案，Active Tilemap 指定目标，在 Scene 中绘制地图。支持画笔、擦除、矩形、吸取、填充、框选和移动；Edit Palette 单独编辑调色板布局。 |
| Window > Animation > Animation | 左侧对象属性轨道，右侧 Dopesheet / Curves 时间轴；提供 Preview、Record、逐帧与关键帧步进、事件轨道、关键帧多选和拖动编辑。 |
| Window > Animation > Animator | 左侧 Layers / Parameters，中央可缩放状态图，右侧状态与转换属性。支持节点拖动、框选、连线、Motion 拖入、条件编辑与运行时状态高亮。 |

双击 Clip、Controller、TilePalette 会打开对应工具，组件 Inspector 也提供入口。资源改动支持撤销和保存。
动画预览停止、切换选中对象、进入 Play 或关闭窗口时还原采样属性；场景保存和撤销前先还原预览，保留用户对其他属性的编辑。

## Animation 窗口

选中带 Animation 或 Animator 的对象，或双击 `.anim.yaml`。顶部 Clip、Target 可独立指定资源和预览根对象；Lock 固定当前编辑对象。New 创建 Clip，Save 保存到 Assets。新 Clip 需要通过组件 Inspector 指定给运行时使用的 Animation，或加入 Animator Controller 状态。

1. 点击 Add Property，从根对象、子对象和组件的属性菜单添加轨道。初始关键帧取当前对象值。左侧轨道数值可直接编辑，在当前帧创建或替换关键帧；轨道右键可移除属性。
2. 在时间标尺拖动定位；Preview 采样到场景，Play 播放，左右按钮逐帧或跳到前后关键帧。Samples 指定帧率，Frame 显示当前帧。Record 时，在 Inspector 或场景修改受支持的属性会录入当前帧，并自动添加缺失轨道。
3. Dopesheet 显示每个属性的关键帧；Curves 显示所选属性曲线，可拖动关键帧调整时间和值，选中关键帧后可拖动进出切线手柄。Shift 加选，空白拖动框选，双击添加关键帧；Delete 删除，Ctrl+C / Ctrl+V 将相对时间粘贴到游标。底部可精确编辑单个关键帧的帧号、值和进出切线。
4. 顶部 Events 轨道双击或 Add Event 添加事件，拖动标记修改时间；选中后编辑函数名和字符串、整数、浮点参数。所有时间编辑均按 Samples 对齐。
5. 滚轮缩放时间轴，中键拖动平移，左侧滚轮滚动轨道；F 适配当前选中的关键帧，A 显示整个片段。时间轴获得焦点后，左右方向键或 `,` / `.` 逐帧，Alt 配合这些按键跳关键帧，Home / End 到首尾，Space 播放，K 添加关键帧；Ctrl+Z / Ctrl+Y 撤销或重做。

退出 Preview / Record、切换目标、关闭窗口、进入 Play、保存场景和撤销时会恢复原始场景姿态。Inspector 的纯录制改动直接进入 Clip 的撤销记录；同时修改非动画字段时，该字段仍按普通场景编辑保存和撤销。录制期间选择根对象的子对象可继续编辑同一个片段。

目前与 Unity 共用上述主要编辑流程，但仅列出运行时支持的 Transform 本地位置/旋转/缩放，以及组件 `Fix64` / `int` 数值属性。不提供 Sprite / BObject 引用帧、布尔/颜色曲线、动画重定向、Humanoid、加权切线、切线自动模式、预览中的动画事件执行或跨窗口系统剪贴板交换。一个绑定以子对象路径和组件类型定位，因此同一路径下的同类型组件只支持运行时可解析的第一个实例。

## Animator 状态机

窗口按 Unity Animator 的基础操作组织：Layers / Parameters 切换、中央网格状态图和右侧属性。中键或 Alt + 左键拖动平移，滚轮以鼠标位置为中心缩放；F / Frame All 将整张状态图放入视野。节点位置随 Controller 保存，Entry、Any State、Exit 也可拖动。

在空白处右键 Create State / Empty 创建状态，或将项目中的 Clip 拖入图中创建 Motion 状态；拖到已有状态上则替换 Motion。单击/框选选择，Ctrl / Shift 单击增减选中状态，拖动可一起移动。右键状态可设置默认状态、复制、删除或 Make Transition，随后点击目标节点完成连线。双击有 Motion 的状态打开 Animation。Ctrl+A 全选，Ctrl+C/V 复制粘贴，Ctrl+D 复制副本，Delete 删除，Ctrl+Z/Y 或 Ctrl+Shift+Z 撤销重做。复制多个状态会重新连接副本间的转换。

点击转换连线或状态的 Transitions 列表编辑目标、Has Exit Time、Exit Time、固定秒数/标准化时长、Transition Offset 和条件。Float 使用 Greater/Less；Int 另支持 Equals/NotEqual；Bool/Trigger 使用 If/IfNot。参数与状态的重命名会更新所有层中的转换引用，删除会清理引用。Parameters 的 + 菜单按类型创建参数；Play 时选中带此 Controller 的对象，可查看当前状态及进度，并实时改变参数或触发 Trigger。

Entry 的条件分支在层启动时按顺序检查，无匹配条件时进入橙色默认状态。Any State 分支优先于普通状态分支，也可打断正在进行的淡化；Trigger 在转换触发时被消费。Exit 结束当前层，显式 Play 可以再次启动。Layers 的 + 创建独立状态机，Weight 将该层动画按 Override 方式混合到前一层结果；运行时可用 GetLayerWeight / SetLayerWeight、GetCurrentStateName(layer) 和 GetNormalizedTime(layer) 查询或控制。

状态的 Motion 可选单个 Clip 或一维 Blend Tree。Blend Tree 由 Float / Int 参数驱动，按各 Clip 的 Threshold 融合；右侧面板可增删 Clip、调整阈值，运行时修改参数会保持不同长度 Clip 的播放相位。当前不提供二维或嵌套 Blend Tree、嵌套子状态机、Avatar Mask、Additive / Sync 层、StateMachineBehaviour 或 Unity 的完整过渡中断排序面板。Exit 在本引擎表示结束平面状态机。参考：[Unity Animator window](https://docs.unity3d.com/Manual/AnimatorWindow.html)、[State Machine Transitions](https://docs.unity3d.com/Manual/StateMachineTransitions.html)。

## Timeline 窗口

双击 `.timeline.yaml` 或选择带 PlayableDirector 的对象打开 Timeline。顶部选择资产、新建与保存；轨道列表可添加 Animation、Activation、Signal、Audio、Video，点击轨道头的 M / L 静音或锁定，右键调整顺序。双击轨道添加片段，拖动片段移动或从两端修剪；右键或使用 S / Ctrl+D 拆分、复制片段。Snap 控制帧吸附，Frame All 适配整个序列，右侧可编辑片段的时间、资源、播放倍率与动画混合时长。重叠的 Animation 片段自动交叉渐变，选中片段可拖动混合手柄调整淡入/淡出；短片段完全包含在长片段内时，会渐入并渐出到长片段。显式混合时长覆盖对应侧的自动时长。相同目标的多个 Animation 轨道按属性权重合成，无活动片段时恢复记录的姿态。

选中场景中绑定同一资产的 PlayableDirector 后，Preview、播放、逐帧与时间输入才会驱动场景；关闭 Preview 或切换资产会停止并还原 Director。Timeline 资产只保存轨道与片段，场景目标绑定保存在 Director 上。当前提供这套 2D 序列编辑流程，不包含 Unity 的 Control Track、Cinemachine Track 和嵌套 Timeline。

## Tile Palette 与 Scene 绘制

新建或打开 Palette，在 Edit Palette 中选择纹理或 Sprite Atlas、切片并保存。退出 Edit Palette 后，拖动框选调色板中的矩形图案作为画笔；Active Tilemap 选择场景地图，点击 Paint in Scene 后直接在 Scene 操作。地图有平移、旋转或缩放时仍按本地网格定位。

S / M / B / U / I / D / G 分别选择框选、移动、画笔、矩形填充、吸取、擦除和洪水填充；`[` / `]` 旋转画笔，Shift 配合方括号翻转。支持多格图案重复填充、重叠区域移动和整笔一次撤销。中键平移和滚轮缩放仍由 Scene 视图处理；切换工具或失去焦点会结束当前笔画。

此流程面向引擎的矩形 2D 网格；不支持 Unity 的 Hexagonal / Isometric Grid、RuleTile 或 Prefab Brush。详细操作见 TiledMap 包 Editor/Readme.md。

## 包的 Inspector

- AudioClip / AudioSource：编辑器试听、波形定位、播放/暂停/停止、循环和预览音量。
- VideoClip / VideoPlayer：视频与音轨预览、时间定位、逐帧查看。预览与场景播放器相互独立。
- CinemachineCamera / Brain：匹配输出镜头、优先级操作、选中时显示镜头范围与跟随连线，运行时查看激活相机和混合状态。
- PostProcessProfile / Volume：效果分组、参数覆盖、颜色和滑块控件、内联 Profile 编辑与保存。
- Collider2D / Rigidbody2D：按 Sprite 适配碰撞体，运行时休眠/唤醒操作。

## 引用与生命周期

Animation.clip、Animator.runtimeAnimatorController、UIDocument.visualTreeAsset、Tilemap.palette 和 ParticleSystem2D.sprite 使用对象引用。旧场景中的路径字段在读取时迁移；路径兼容属性不再序列化。

UIDocument 使用 enabled / GameObject.SetActive 控制显示，通过 sortingLayer / sortingOrder 排序。引擎不再提供 UIManager、逻辑窗口层级或模态管理；项目可自行实现导航行为。

显式销毁 BAsset 会注销对象、清除缓存引用并释放自身拥有的资源。持有的共享资源不连带销毁；VideoClip 的内嵌音轨、VisualTreeAsset 的内联样式、预览纹理和音频输出按所有权释放。已销毁对象不会再次从资源缓存返回。运行时资产不允许保存。

进入 Play 时 VisualTreeAsset 保留 UXML 模板及外部样式的隔离副本，运行时节点不会修改编辑态资源；Object.Instantiate 同样保留这些内容。音频预览的波形扫描与采样循环按实际 PCM/输出帧数设置局部循环预算，退出后恢复编辑器的默认保护限制。

Play 镜像同时保留 AudioClip 的 PCM、VideoClip 的帧及内嵌音轨、Texture 像素和 Sprite / Material 的纹理引用。副本间共享同一映射后的资源；停止 Play、创建失败、热重载回滚或成功替换时会释放对应副本。自定义 BAsset 若包含未序列化的内部数据，可重写 CaptureClonePayload / RestoreClonePayload；传输数据应使用 CLR 或引擎本体类型，内部 BObject 引用通过 cloneObject 映射，以支持程序集更新后的同名类型。

## 示例工程

打开 Example/Project 的 Main 场景，菜单可进入 Animation、AnimationBlending、AnimationCurves、TiledMap、TilemapEditing、Audio、AudioMixing、VideoPlayback、Cinemachine、PostProcessing 等独立示例。退出 Play 后可在同一工程中使用以上工具编辑对应资源。
