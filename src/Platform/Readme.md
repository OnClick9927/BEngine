# Platform hosts and contracts

此目录包含跨平台接口、各平台 Host 实现、构建目标实现、原生桥接资源及通用编辑器启动契约。

`Abstractions` 和 `Contracts` 提供宿主接口；`Android`、`iOS`、`Linux`、`macOS`、`Web`、`Windows` 各自包含运行时实现与对应的 `BEngine.*.Build` 编辑器构建模块。共享实现位于 `DotNet`、`Desktop`、`Veldrid` 和 `Build`。这些程序集随引擎宿主发布，不作为功能包导出；功能包通过 `hostReferences` 引用平台契约。

`BEngine.Editor.Host.Contracts` 只声明通用应用入口。启动器根据安装描述读取入口程序集，不引用编辑器工作台或编译实现。

`Player` 持有各目标平台的 Player Host 模板、运行时分发工程和导出脚本；通用 Player 运行时位于 `Core/BEngine.Player`，编辑器构建管线位于 `Core/BEngine.Core.Editor/Player`。

`ExportPlatformSources.ps1` 导出 Android/iOS 原生适配源码及通用桥接资源，保留源文件目录隔离和路径检查。
