# 平台宿主与内容打包约束

平台 Player 实现归 `src/Platform/Player`：`Hosts` 包含桌面、移动端和 Web 宿主，`RuntimeDistribution` 按 tooling 元数据组合运行时，`ExportPlayer.ps1` 导出平台配套内容。Core 仅提供构建事务、设置、启动和内容扩展契约。导出后的 `BuildHosts` / `BuildRuntime` 布局及宿主程序集名称不变；不影响旧项目、资源 GUID 或内容数据。

内容提供者通过 `IPlayerBuildContentProvider.PackagingRequirements` 声明内容格式的物理要求：

```csharp
private static readonly ResourcePackagingRequirements Packaging =
    ResourcePackagingRequirements.Create([".myarchive"]);

public ResourcePackagingRequirements PackagingRequirements => Packaging;
```

默认值为空，因此既有的非 AB 内容提供者不需要改动。快照归一化、去重并按序保存，不持有调用方的可变集合。Core 在一次 Player 构建开始时读取一次，传给内容准备上下文及平台请求，不逐帧访问或重复分配。平台可忽略自己不需要的容器约束。

Android 将后缀转换为一个经过转义的 `BEngineUncompressedResourceExtensions` MSBuild 属性，并保留 SDK 原有免压缩后缀。声明为空时不额外设置此属性。具体格式由内容提供者负责，平台模板不再硬编码格式名称；通用 AdditionalMsBuildProperties 不能覆盖这一结构化设置。

扩展名以 `.` 开头，允许 ASCII 字母、数字、点、下划线和连字符；拒绝路径、控制字符、命令分隔字符及不明确的连续点。传输使用 `%3B` 分隔，防止 MSBuild 把一个扩展列表误读成多个属性。

Web 公开导出的通用内容采样入口由 `GetAssetBundleDiagnostics` 改为 `GetContentDiagnostics`。它仍读取同一个 `PlayerContentDiagnostics` 快照；依赖旧诊断名称的外部工具需更新调用名称，运行时内容数据不变。

验证入口：`Test-PlatformPackagingRequirements.ps1`。除扩展名和不可变性测试外，它使用一个自定义内容提供者穿过真实 Core 构建管线及平台适配器，检查声明无损传递；实际调用 Android 参数配置并使用 MSBuild 分别求值源模板和导出模板，检查多后缀未丢失。测试还检查源码宿主发现路径、Core/Platform 不认识 AB 后缀及高频读取零分配。
