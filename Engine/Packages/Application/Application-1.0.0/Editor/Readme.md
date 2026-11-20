# Application

只提供 Framework 和游戏工程模板：Launcher / GameDomain / GameService、Value、State、Pref、MVC、Events、RedDot、UI、Undo、GameObjectPool 与 Lifetime。

Framework 运行于游戏主循环，异步统一使用 BTask。允许多个同类型服务；一个时刻只有一个活动 GameDomain。引擎机制及 AB 功能通过公开 API 调用，不由本包实现。

- [职责与依赖](Doc/Framework.md)
- [开发指南](Doc/DevelopmentWorkflow.md)

命名空间 BEngine.App，包 com.bengine.application 保持必装。
