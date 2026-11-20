# 应用宿主通知与工程设置的所有权

Core 持有应用状态、生命周期分发和运行配置不变量。EditorServices 提供编辑状态、设置文件操作及通知，EditorWorkbench 组合默认工作台；平台与 Player 宿主通过 Core 契约传递生命周期通知。

## 接入

- Core 的 ApplicationHostEvents 提供焦点、暂停和低内存通知入口。它委托已有 Application 分发机制，不维护第二套事件、宿主实例、暂停策略或缓存。宿主在引擎线程调用。焦点去重、输入重置、回调异常处理和暂停通知次数保留原有语义。
- Core Player 的两种会话、通用窗口呈现和 EditorServices 暂停入口使用上述契约。Application 的私有通知实现不再直接向这些调用方泄漏。
- RuntimeConfiguration.ApplyIdentity 用于宿主应用公司/产品标识，不改变后台运行、标签、层、图形或路径设置。参数先检查 null，避免部分更新；保持编辑器草稿值的既有行为，不自动裁剪字符串。运行文档激活仍需 RuntimeConfiguration.Validate。
- ProjectSettingsData.Validate 统一调用 Core Editor 的文档格式、脚本符号及运行配置校验。所属平台/功能通过已有 IRuntimeConfigurationExtension 参与，不由 EditorServices 复制这些规则。
- EditorServices 保存设置时先恢复缺失的默认层列表，再完整校验，最后才写入文件和应用。默认层来自 RuntimeSettingsData 的独立实例，不维护另一份内置层/标签定义。

## 修复的实际缺陷

旧编辑器校验只检查层名，未检查连续索引、内置身份等 Core 不变量。保存时先写入 YAML，再在 LayerRegistry.Configure 中失败；磁盘文件已损坏，应用标识和标签也可能部分改变。统一文档校验之后，非法设置不会进入写入及应用阶段。该保证针对校验失败，不宣称新增了通用文件写入事务或抵御任意外部文件系统故障。

ProjectSettings v3、字段、GUID、场景和 Prefab 协议未更改。缺失层列表仍使用默认值，正常保存仍通知一次；编辑器保存 RunInBackground 的数据但不替宿主改变运行策略。

## 验证和未完成范围

Test-ApplicationHostContracts.ps1 分别验证无 UI 的 Core 宿主、无呈现包的 Core Editor 文档规则及 EditorServices 集成。覆盖通知顺序/次数、焦点去重、异常处理、身份应用范围、非法设置拒绝及文件/状态保持、默认值独立性、平台校验扩展和有效文档往返。

同一集成测试二进制加载旧 EditorServices，在非法层索引保存后复现源文件被覆盖；仅替换本轮 EditorServices 后通过。

本轮使关闭历史友元权限的编译继续越过 EditorServices，进入 AssetPipeline 和 Application 模板。新暴露的场景构建 DTO、元数据预读的编译批处理辅助类、工程路径过滤及模板校验/默认标识访问仍需语义审查。不能把当前编译层的诊断数、程序集图或公开 API 检查当成全部 44 包职责清理完成。
