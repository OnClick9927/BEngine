# 跟随预判

`CinemachineCamera.lookaheadTime` 以秒表示根据目标速度向前预判的时间，默认 0，保持普通跟随。lookaheadSmoothing 控制估算速度的平滑时间；位置阻尼、死区和边界限制继续作用于预判后的目标。

目标瞬移、切镜或重新开始跟随时调用 `ResetState()`，清理阻尼与速度历史。替换 follow 时自动清除旧目标速度；首帧不会由未初始化坐标产生速度尖峰。

示例工程：Main → Camera Lookahead & Reset。Predict 和 Follow 对比半秒预判与普通跟随，Teleport 演示清理镜头历史。
