# 编辑器启动记录、包命令与资产源事务

Core Editor 负责启动测量、编译输入判断和无界面包命令的执行机制。默认工作台负责启动阶段名称、JSON 文件输出、默认日志位置、界面策略和刷新时机。原生窗口的帧率平滑算法由 Windows Editor 平台实现；Core 仅保留帧采样值契约。

## 启动记录

`EditorStartupTrace.Begin(initialStage)` 创建独占、作用域化的进程启动记录。创建线程负责 `Mark`、`Complete` 和 `Dispose`；`Measure` 返回可在工作线程释放的单次测量。`Complete` 返回只读的阶段与明细快照，并释放进程记录权；重复完成返回相同快照。失败启动必须释放 trace，不能把失败路径当成成功 profile。已结束 trace 的测量不能写入新 trace。

Core 的包编译、程序集加载等机制可以贡献明细，但不写日志文件。EditorWorkbench 的 `EditorStartupProfile` 保留既有 `EditorStartupProfile.json` 路径和字段，组合平台进程信息与 Core 快照，在第一帧完成后写入。测量不在每帧执行。

## 包命令

`AddBEngineEditorCompilation` 注册可替换的 `IEditorCommandLineRunner`。命令行宿主调用 `Run(arguments, optionalCatalog)`，不需要窗口、IMGUI 或默认工作台。Core 解析包声明的命令入口，通过注入的 `IProjectPackageManagerFactory` 激活其依赖闭包，只实例化当前激活作用域拥有的命令类型。调用参数经过快照并只读传递。已有 `IEditorCommandLineTool` 实现无需修改。

调用不能替换正在运行的 Editor/工程工具，也不允许并发或重入修改进程环境。工具实例按逆序释放，之后释放包作用域；执行与释放同时失败时保留两者。运行前的包启用状态和 TEMP/TMP/TMPDIR/工程环境变量在退出时恢复。命令自行拥有它打开的具体工程会话。

临时工作区在退出时尽力删除。Windows 映射 DLL 或调用者持有的异常栈可能继续持有可卸载程序集的镜像租约；这时只允许 `Library/PackageImages` 留待租约释放，不能宣称 DLL 已立即从磁盘删除。引擎不为删除临时镜像而强制结束进程或无限等待 GC。

## 编译与资产源

Core 的 `ProjectCompilationInputs.IsScriptPath` 统一源码监测与工作台对 CSharp/程序集定义的分类。Shader 格式仍由所属编译提供者判断。`GameViewFrameTimingSnapshot.HasValue` 是公开值契约，不公开窗口实现。

EditorServices 的 `IProjectAssetDatabase` 增加 `CopyAssetSource` / `MoveAssetSource` 能力，默认实现明确拒绝不支持的操作。AssetPipeline 在自己拥有的数据库、文件服务和刷新门内执行：复制跳过 metadata，移动保留 metadata；新目标不能覆盖孤立 metadata；目标限定项目 Assets，移动源也限定项目 Assets，复制允许外部导入。操作使旧刷新快照失效。工作台仍负责写入策略、修改处理器、调用后刷新和选中项。

工作台不再直接调用 `AssetFileOperations`。既有场景/Prefab/资源 GUID 格式不改变，复制与移动的身份行为由真实资产数据库和实际 Editor 回归验证。

## 内部访问约束

构建不再从 package.yaml 或包工具工程自动生成所有包的友元访问。内部协作名单仅来自引擎实现工程与显式测试契约。`Test-PackageBoundaries.ps1` 对全部 Core/Platform 产物检查：不得向功能包授予内部访问。包自身 Runtime/Editor 之间合法的内部协作并不因此改成公开 API。

本轮并不据此宣布所有包的语义职责审查完成；跨包绘制、编辑契约及所有包的完整职责清单仍需继续核对。
