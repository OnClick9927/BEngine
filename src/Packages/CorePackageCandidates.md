# Core 功能拆分状态

Graphics、Rendering2D、IMGUI、EditorWorkbench 与 Diagnostics 已从 Core 或原 Application 的默认宿主中拆出。具体职责与依赖见 [包边界](PackageBoundaries.md)。

Core 不保留具体图像、材质、Sprite、场景绘制器、GUI 控件、默认窗口或重型诊断实现。场景、对象生命周期、定点数、输入、调度、资源身份与引用、序列化、通用内存预算以及服务注册继续属于 Core。RHI 数据与接口以及轻量采样接口是基础机制，不因具体模块拆分而强制依赖某个功能包。

PlayerPrefs 等体积较小且普遍使用的基础应用数据接口本轮保留。后续拆分应基于清晰的可选能力和实际依赖，不应仅按文件数移动代码。
