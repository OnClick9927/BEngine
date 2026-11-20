# 动画编辑器制作

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
