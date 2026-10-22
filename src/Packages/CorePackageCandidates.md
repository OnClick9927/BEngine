# Core 中可继续拆包的功能

平台适配属于 `Engine/src/Platform`，功能模块属于 `Engine/src/Packages`。Core 保留抽象接口、Utility 调用入口和基础数据类型。不能通过把平台接口移动到另一个功能包来迫使所有使用者依赖该包。

| 候选包 | 当前内容 | 拆分条件与建议 |
| --- | --- | --- |
| Preferences | `BEngine/Misc/PlayerPrefs.cs`，偏好数据的 YAML 持久化 | 可以独立拆分；Core 其他代码不调用 PlayerPrefs。依赖 Core 的 FileUtility、ApplicationPathUtility 和序列化。建议优先。 |
| SceneRendering2D | PortableSceneRenderer、默认 2D 渲染流程、纹理缓存、相关 Shader | 可以成为可替换的默认渲染包。Core 保留 RHI、场景渲染接口、基础渲染数据；必须同时移除 EngineRenderer、DI 和 Platform 对默认具体渲染器的直接构造，由宿主选择渲染包。建议其次。 |
| EditorWorkbench | 默认 Hierarchy、Project、Console、Inspector 等窗口及布局组合 | 可拆为编辑器功能包。Core 保留窗口、IMGUI、Undo、序列化属性与命令基础设施；窗口的交叉调用和默认布局需要随包或宿主一起迁移。 |

不建议直接拆出：场景与对象生命周期、数学、基础输入、资源身份/引用、基础序列化、BTask 调度、依赖注入。这些已被 Core 自身使用，直接移到包会形成反向依赖。`Documents/Document` 是 Scene、Prefab、Material 共用的资产持久化桥接，也应继续作为基础设施。

UIElements 已完成拆分，位于 Packages/UIElements；控件、输入路由、渲染、默认样式、UXML/UCSS 导入、保留模式窗口适配及节点／轨道可视控件均在包内。上表仅列出尚未拆分的候选。

Pooling 已完成拆分，位于 Packages/Pooling；对象池、租借生命周期、预热、缓存裁剪、统计及集合池均由包提供。
