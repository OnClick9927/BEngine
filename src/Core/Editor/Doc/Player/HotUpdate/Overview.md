# 托管代码加载

Core/BEngine.Player 提供 BEngine.Player.HotUpdate：清单、IHotUpdateModule、ManagedCodeRelease、激活与回滚机制。实际程序集执行域由 Platform 提供；内容模块负责交付代码数据，不改变执行机制。
