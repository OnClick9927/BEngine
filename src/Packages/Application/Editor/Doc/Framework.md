# Application 开发框架

Application 是基于引擎公共 API 的快速游戏开发框架，不提供引擎构建、发布、宿主、程序集装载或 AB 实现。运行时只包含 Framework；Editor 只包含游戏工程模板与其默认参数的组合。

## 提供的功能

- Launcher：切换当前唯一的 GameDomain，AOT 域与热更游戏域使用相同生命周期。
- GameDomain、GameService：显式组合任意数量的服务，允许同类型的多个实例。
- Value、State、Pref、MVC、Events：数据绑定、流程状态、存档适配、界面业务与事件。
- RedDot、UI、Undo、GameObjectPool、Lifetime：游戏常用服务与生命周期管理。
- 工程模板：示例 AOT 界面、游戏入口、GameDomain 和默认引擎参数；只调用公开 API。

Framework 在游戏主循环上执行。异步使用 BTask 和 BCancellationToken，不创建工作线程。UI、对象池和 PlayerPrefs 复用所属模块，不重复实现底层能力。

## 不在本包实现

- Core/BEngine.Player：Player 公共宿主契约、准备进度、托管代码清单、装载契约、激活与回滚机制。
- Core/BEngine.Player.Editor：构建事务、脚本编译、裁剪、Host 契约、构建设置和扩展接口。
- Platform/Player/Hosts、RuntimeDistribution、ExportPlayer.ps1：跨平台 Player 模板与运行时发布组合；通用构建管线及内容扩展接口由 Core 提供。
- Platform：运行目录、操作系统接入、程序集执行域、原生窗口、GPU 后端和平台构建执行。
- AssetBundles：AB 构建、内容发布、真实/虚拟内容接入、资源与托管代码内容加载。
- EditorWorkbench：打包、Player Settings、Editor Play Mode 等默认界面。

Core 与 Platform 不引用具体包。各模块通过服务贡献、内容构建提供者与宿主注册接入。开发者通过 Application 调用和组合这些能力，也可以直接使用所属模块的 API。

## API 与数据

Framework 命名空间保持 BEngine.App；构建 API 使用 BEngine.Editor.Build，启动和托管代码契约使用 BEngine.Player、BEngine.Player.Startup、BEngine.Player.HotUpdate。原有场景、Prefab、资源 GUID、Player 设置和部署格式不因程序集移动而改写；使用旧 API 的源码需要重新编译。

本包保留 required: true。依赖由 package.yaml 声明；旧 BFrame 包身份继续迁移为 Application。

详细用法见 [游戏开发指南](DevelopmentWorkflow.md)。
