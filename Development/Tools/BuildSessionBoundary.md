# 编辑器构建会话的职责

Core Editor 的 `EditorBuildEvents.BeginBuild(outputPath)` 创建 `EditorBuildSession`。会话拥有一次开始和一次结束通知；`Complete(succeeded)` 提交最终结果，未完成时 `Dispose()` 报告失败。首次完成或释放生效，通知回调重入不会重复结束。嵌套构建各自通知，不合并、不依赖全局“当前构建”。输出标识原样保留，不在事件机制里改写路径。

```csharp
using var build = EditorBuildEvents.BeginBuild(outputPath);
await PerformBuildAsync();
CleanTemporaryFiles();
build.Complete(true);
```

异步 UI 操作可以把同一个会话转交给主线程完成回调，不能仅在启动任务的同步方法中使用 `using`。调用方负责把会话、取消源和临时资源保留到任务真正结束。会话不创建线程，也不替代线程调度或资源构建。现有 `buildStarted` / `buildFinished` 事件继续可用；通知仍走 Core 的回调隔离机制。未添加针对任何包的 Core 引用、包身份判断、友元授权或类型转发。

Player 发布及 Package Manager 的 Runtime 包导出使用该会话。Player 的开始/完成、内容处理、平台发布和清理顺序保持；发布后清理失败时，返回异常并发送失败结束通知，而非漏发。完成进度回调抛错也不报告成功。失败构建原有的诊断临时文件保留规则不变。

AssetBundles 菜单只调用这个公开机制，不再通过自身 `BuildPipeline` 的内部方法广播 Core 事件。后台 AB 任务完成后仍排入编辑器主线程，再报告结果和释放取消状态；完成回调或进度清理抛错时仍清除活动状态并释放 CTS。若已退出的宿主拒绝排队，则记录错误、结束失败会话并释放取消状态，不再操作该宿主的 UI。AB 归档已经完成之后的 UI 回调/进度呈现错误不改写归档构建结果。直接调用底层 AB 构建器仍不自动发送编辑器外层构建事件，避免嵌入 Player 发布时重复通知。原有 `BuildPipeline` 两个公开事件保留为兼容订阅入口。

验证入口为 `Development/Tools/Test-BuildSessionBoundary.ps1`。Core 测试运行公开会话及实际非 AB Player 发布事务，注入内容、平台、取消、完成进度和文件删除失败；AB 测试生成真实小型归档，再测试实际菜单完成处理、异步主线程排队和同步准备失败。测试内的反射仅接入私有菜单/宿主测试夹具，不增加生产访问权限。实际 Editor 和 Windows/Web Player 仍需另行回归。

这次整理只处理构建生命周期。AB 内容编译中的代码/资源固定点、托管镜像一致性、shader 策略及其余包的职责审查尚未结束，不能凭会话测试或内部访问数量认定整体边界已经清理。
