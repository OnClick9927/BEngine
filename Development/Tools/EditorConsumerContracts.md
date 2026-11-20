# 编辑器消费方的公开机制边界

本轮沿用本机 Unity 模块与包元数据的职责参照，保持 BEngine 的最小 Core 约定。包内领域策略仍由包持有，公共机制通过受控 API 使用；没有新增包依赖、友元权限或内部数据类型转发。

## 归属与迁移

- Core 的 `BEngine.PathLexicalRules` 提供完整路径段匹配、相对遍历和非法路径段检查。它只处理字符，不访问文件系统，也不保证符号链接或最终物理路径安全。AB 自己决定哪些路径允许、是否排除 Editor、是否要求空白或冒号检查。
- Animation Editor 使用既有 `ComponentFieldSerializer.GetInspectorMembers/GetMemberType` 查询可动画成员，不再访问 RuntimeTypeCache 的私有元数据数组。名称分段使用标准 Span 查询，缺少分隔符时保留原行为。
- `GetSerializableMembers/GetInspectorMembers` 返回缓存的只读视图，禁止通过强转数组或 IList 修改全局发现缓存。视图使用弱键关联，避免仅因该视图缓存阻止已退役程序集卸载。首次查询创建视图，重复查询复用；内部序列化规则未修改。
- AB 使用既有 `RuntimeAssetReferences.ValidateOptionalRelativePath` 和 `ContentVersionLabel` 协议，不再绕过公开接口访问验证器或构建设置存储器。
- Platform 的 `PlatformContentBuildPolicy.CompressBuiltInArchive` 管理内置解码能力限制：Web 不启用当前运行库不支持的 Brotli 解码。AB 只持有是否请求压缩的资源策略；独立发布压缩格式和包协议没有迁到 Core。
- Core 构建机制的 `PlayerOutputValidation.ValidateLowercaseTree` 验证部署路径。保留原有 StreamingAssets 大小写例外与诊断，接收公开文件和路径服务，不接管 AB 内容选择。
- Core Editor 的 `EditorExtensionLifecycle.SynchronizeReflection` 协调已完成注册、优先级变更或退役之后的反射查询及扩展预热。调用方仍负责程序集所有权与成功/失败回滚；该入口不加载程序集，不执行游戏初始化，不选择具体包。AB 热更的三个调用位置和 Core 包编译管理器统一使用该入口。

## API 与数据

新增三个公共静态类型、六个公共方法，共九条程序集公开面记录；不移除现有公开 API。内部压缩辅助类及旧内部路径校验入口移除，仓库内调用者已迁移。场景、Prefab、资源 GUID、项目设置格式和包清单保持原样。

编译顺序仍为 Core RT → Core Editor → 包 RT → 包 Editor → 项目 RT → 项目 Editor，各阶段按依赖排序。无需新增项目层适配代码。

## 验证范围

`Test-EditorConsumerContracts.ps1` 用无 Core 内部访问权限的独立程序集，分进程验证路径语法、只读缓存、可卸载真实程序集、平台压缩、发布校验、版本协议、反射域切换/失败清理和动画属性发现。旧版对照使用实际上一版 DLL，记录输入路径及 SHA256。预热后的 10,000 次路径查询和成员查询分别测量托管分配；不把接口迁移宣称为 CPU 或帧率提升。

诊断构建仍剩 EditorWorkbench 的五条内部文档访问错误，集中在 Tags/Layers 对 Prefab 基线与组件图的事务编辑。正常发布构建通过不代表全部职责边界清理完成，tooling 与动态调用继续单独审查。
