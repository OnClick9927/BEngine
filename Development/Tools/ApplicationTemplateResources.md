# Application 模板与共享编辑器资源

Application 的项目模板负责选择框架默认 UI 和默认图片，并保留开发者已有的资产与元数据；资源根目录的注册、查找和安装布局解析属于共享 EditorServices。

PlayerProjectTemplate 通过公开的 EditorResource.FindPath("Icons/BEngine.png") 查询默认 AOT Logo，再按原有逻辑复制到新工程。它不再沿宿主目录逐级探测 Editor/Icons、src/Core/Editor/Icons 等旧布局。自定义资源根、源码运行和正式安装使用同一查询机制，不向 Core 或 Platform 添加包引用。

回归测试将图片放在与宿主目录无关的注册资源根，检查复制后的字节一致，并在重复初始化时检查已有自定义图片及 GUID 元数据不变。相同用例在旧模板上复现找不到图片，在新模板上通过。其余项目标识、AOT 文档校验、已有工程保护和资源编译职责检查继续保留。

本次只调整框架消费共享资源的方式，不移动资源所有权、不改变公开 API、包清单或编译顺序。产品构建通过，独立回归结果记录在 Temp/Engine/Verification/ApplicationResourceOwnership_20261115；真实 Editor 启动与代码重载、正式发布仍须完成后续验收。
