# 资源可达性与 Shader 资源规则

Core Player Editor 的 `PlayerResourceReferenceSet` 读取实际程序集 IL 字符串及已选文本文件，提供资产地址、别名和 GUID 引用查询，以及从显式根和文本依赖开始的资源闭包。快照只在本次构建中使用；选择过程不修改快照，不把未选择的文本资源反向当作根，也不扫描全部安装包。调用者声明保存范围、别名和目录字符串的有效范围。

`PlayerResourceBuildPipeline.SelectHostResources` 负责启动宿主的代码引用、裁剪设置及资源可达性组合。它使用通用宿主部署地址，内容归档的格式和写入仍由内容提供者处理。Core 的保存设置只检查项目资产地址和路径安全，不规定 AOT、AB 或其他内容分区。

AssetBundles 提供自己的主包/AOT、Resources 和包资源规则，校验本内容格式支持的保存范围，提供资源候选和资产依赖。AB 不再调用 Core 的内部代码分析和宿主序列化根，也不再实现通用资源闭包。既有内部测试入口保留委托，格式、地址、GUID、显式保存失败和报告语义保持。

Core Editor 的 `IShaderResourceBuildPolicy` 是资源格式贡献者的契约，`ShaderBuildPipeline` 负责分发。Graphics Editor 注册后端文件命名规则与 Common 启动资源根。Core 的 Shader 构建接口和 AB 不再维护后端筛选的扩展名、命名及 Common 路径列表；未安装 Graphics 时，该接口不虚构图形筛选规则。规则通过已有 `RuntimeServiceContributions` 跟踪程序集所有者，卸载时移除，替代规则卸载后恢复前一个所有者。没有新增程序集扫描或独立全局注册表。

公开资源 API 为构建期机制，编辑器程序集不进入 Player。测试入口 `Development/Tools/Test-ResourceReachabilityBoundary.ps1` 分别运行不加载图形/内容包的 Core 进程，以及加载实际 Graphics/AB 提供者的集成进程。覆盖 IL、GUID、目录边界、循环闭包、保存、取消、快照隔离、图形后端矩阵和规则卸载；实际 SDK 构建和游戏场景运行另由 Example/Snake Windows/Web 回归验收。

此整理未处理所有剩余语义项。Core 代码分析器原有的文本扩展名识别（含 Shader/UI 格式）、通用包输入中的字体后端选择、内容资源编译策略、工具宿主生命周期和其他包的私有机制访问仍需逐项核对，不能据此宣称完整职责审查完成。
