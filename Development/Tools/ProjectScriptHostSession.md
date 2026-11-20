# Live Editor 的项目编译会话

Core Editor 的 `ProjectScriptHostSession` 提供默认工作台及其他编辑器宿主可使用的编译机制。它不依赖任何功能包或默认工作台。调用方持有工程的 `EditorInstanceContext` 和 `BPackageManager`，先关闭脚本宿主，再关闭包管理器与工程实例。

```csharp
using var scripts = ProjectScriptHostSession.Attach(workspace, packages);
scripts.CompileRuntime();
scripts.CompileEditor();

// 在宿主线程准备，后台工作线程编译，回到宿主线程应用。
var request = scripts.PrepareCompilation();
var result = await scheduler.ScheduleAsync("Compile scripts", token => request.Build(token));
// 上一行的 continuation 不保证处于宿主线程；通过宿主自己的 Post 机制应用。
scheduler.Post(() => scripts.Apply(result));
```

`PrepareCompilation`、初次加载、`Apply`、`Release`、`Dispose` 必须在 Attach 的线程执行。`Build` 可在工作线程执行。宿主保留 UI 进度、CompilationPipeline 事务通知、Reload 事件、Undo/Selection、Play Mode 及窗口刷新策略。编译引用解析、Runtime→Editor 顺序、增量缓存及加载作用域由 Core 管理，不向包暴露内部编译图。

- 每个 EditorInstanceContext 同时只能有一个活动宿主；工作区与包管理器必须属于同一工程。
- 初次 Editor 编译失败保留已成功加载的 Runtime，允许修复后重试。
- 后台请求只编译，不加载、不替换当前程序集。结果绑定宿主及请求版本；较新的准备或 Release 会使旧结果失效。请求与结果各使用一次。
- Apply 先加载两个新图，再发布输出并替换宿主内的图。加载失败卸载暂存图并恢复旧的类型注册。新图提交后，旧图清理的异常会报告给调用方，不回滚新图。
- 编译输出的文件发布不构成磁盘事务；调用方仍应处理 IO 异常。取消不会删除可复用的已完成编译缓存。
- Release/Dispose 会取消并等待已经接受的后台编译退出，再卸载自己的程序集。不会释放 `ProjectScriptSession`、工具会话或旧 API 的全局程序集作用域，不触发强制全局 GC。调用方必须停止所有运行中的项目代码并释放自己的对象/程序集引用，才可能完成收集。
- Release 后可用于包重载；Dispose 后不可复用。父实例已提前关闭时仍可 Dispose 清理，但不能继续编译或应用代码。

程序集内容确定性及既有 Web 校验保持原规则，不降低哈希验证要求。`AddBEngineEditorCompilation` 是公开的 Core 编译服务组合入口，具体编译器接口、编译图和输出结构仍为内部实现。

相关回归：`Test-ProjectScriptHostSession.ps1`，覆盖真实编译、RT/Editor 身份、增量跳过、失败回滚、请求过期、线程限制、取消与等待、独立会话隔离、清理异常和可回收程序集。
