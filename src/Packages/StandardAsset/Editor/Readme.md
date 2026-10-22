# StandardAsset

类型：`project`。仅依赖 BEngine Core，无可选包依赖。

在 Package Manager 选择 StandardAsset，点击 Import。整个包复制到
`Assets/3rd/StandardAsset/`，Scripts 下的程序集自动参与工程编译。导入后源码属于工程；
重新打开工程不会覆盖修改。移除整个包时先从 Packages/manifest.yaml 移除对应条目，再在 Project 窗口删除目录。

- TopDownMover：WASD / 方向键移动，规范化斜向速度；可关闭键盘输入并设置 moveInput。
- CameraFollow：阻尼跟随、偏移与 Snap。
- Rotator：按每秒角度旋转。
- Bobbing：本地坐标正弦浮动。

示例工程主菜单的 StandardAsset 入口演示四个组件。
