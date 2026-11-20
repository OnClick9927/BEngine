# 编辑准备、场景格式构建与游戏模板边界

Core Editor 持有通用编辑准备调度和引擎文档格式规则。AssetPipeline 负责导入与构建资源的编排；Application 仅提供游戏模板和工作流约定；Graphics 解释自己的 Shader 编译产物。

## 机制与调用方

- `EditorWorkBatch` 替代内部编译专用批处理类。包源码编译、包影子准备、程序集预加载和资产元数据预读共同使用这一个机制。保留有界并行、按输入索引返回、调用线程进度/执行泵、取消、单异常拆封和异常情况下等待全部 worker 的行为。无进度观察者时不建立进度队列，不允许 worker 执行扩展初始化或更改编辑器状态。
- `SceneBuildDataCompiler` 从 AssetPipeline 迁入 Core Editor，以公开的字节输入/输出入口封装 Core 场景 DTO、运行格式读取器及验证器。导入编排仍在 AssetPipeline。编译不创建真实 Scene 或组件，不改变 GUID/Prefab/字段协议，不触发生命周期。二进制输入独立复制；合法但不能无损压缩的输入保留原始字节；取消不被回退捕获。没有向外暴露可变场景 DTO。
- `ProjectFileConventions.IsAuxiliaryPath` 持有共同的元数据、锁文件及原子写入命名规则。资产导入、平台文件监听和 StreamingAssets 复制均调用它。它只判定末段名字，不代替各调用方的扩展名、Editor 目录、链接或发布规则。平台不会因此依赖导入包。
- `AssemblyDefinitionDocument.Validate` 调用格式所属的现有验证器，供模板等工具组合；没有公开内部验证类，不执行写入或应用。Application 不再直接访问私有验证器。
- `PathUtility.ResolveChildPath` 提供平台路径服务上的严格相对路径解析，拒绝空段、点/父段、冒号和绝对路径，支持两种分隔符及卷根。它只进行词法检查；Application 模板仍在读取/复制前检查实际符号链接和 junction，保留新建工程专用策略。
- 模板通过 `PlayerBuildSettingsStore.Save` 的正常化流程取得公司/工程对应的 Android/iOS 默认标识；不复制算法，不访问私有默认值辅助方法。其他模板默认值保持，显式开发者标识由存储器继续保留。
- `ShaderCompilationResult.DeletedArtifacts` 是由编译器解释的源资产到退役产物路径映射。Core 不解释 Graphics 的 `current` 指针或 SPIR-V；Graphics 通过公开结果契约执行原有删除和缓存失效。
- Application.Editor 已无 Windows 专用界面/宿主，移除遗留的 `net10.0-windows` 声明，使用公共编辑工具目标 `net10.0`。这不等于已经在其他操作系统完成运行验收。

## 兼容与验证边界

没有修改包清单、实际包依赖、序列化字段或游戏业务源码。内部类迁移和重命名要求仓库程序集重新编译；Player 不包含新增 Core Editor 实现。未增加每帧扫描或反射。

`Test-EditorBuildMechanisms.ps1` 验证独立机制与实际模板/Graphics 集成。并发测试覆盖执行泵及观察者失败后 worker 的生命周期，场景测试包含未知类型、组件不实例化、紧凑格式限制回退及取消。集成测试检查生成标识、AOT 文档修复/拒绝、已有工程跳过，以及真实 Graphics 退役指针删除。

关闭历史包友元后，本层通过，编译继续暴露 IMGUI 的预览、资源绑定和工厂访问。该结果不表示全部包职责审查完成；仍需继续审查公开签名、反射和后续调用链。
