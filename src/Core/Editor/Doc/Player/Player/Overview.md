# Player

Core 提供公共宿主入口 BEngine.Player.BEnginePlayer、BEngine.Player.Startup 准备进度与 BEngine.Player.HotUpdate 托管代码契约。具体内容模块通过 IPlayerHost 注册，实现由分发时的模块元数据选择。

操作系统、执行域和 GPU 实现由 Platform 提供。应用内闪屏保持 Logo 渐显 0.5 秒、并行准备、进度达到 100% 后交接；不另开闪屏窗口。
