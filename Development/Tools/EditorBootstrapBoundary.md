# 编辑器启动扩展

默认工作台不引用游戏框架。某个包需要在工程编译前注册工程策略时，在自己的 `Editor/tooling.json` 中声明可选的 `editorBootstrap.assembly` 和 `editorBootstrapProject`。项目路径相对描述文件，必须落在声明包自己的目录内；其他 tooling 路径维持原来的 Editor 目录限制。

统一导出器发布该项目与依赖，将程序集名写入 `editor.application.json` 的可选 `bootstrapAssemblies` 数组。Platform 的通用编辑器入口为这些声明组合依赖解析器，不出现包名、框架类型或项目模板实现。默认工作台仍通过现有程序集初始化机制发现扩展。解析器只在编辑器入口运行期间订阅，正常返回与异常退出均解除订阅。

Application 使用此入口注册游戏工程模板策略；它不提供引擎导出脚本。EditorWorkbench.Host 的项目引用与发布依赖闭包不再包含 Application。扩展及其内容依赖仍随默认 SDK 安装，声明独立依赖元数据；这不是从发行版移除必需包。

原有 version 1 描述文件不带 `bootstrapAssemblies` 时继续运行。重复名称、跨目录名称、非 DLL、缺失程序集与依赖元数据会在调用编辑器入口前失败。Core 的编译顺序、工程资源 GUID、序列化数据及 Player 内容不变。

验证入口：`Test-EditorBootstrapBoundary.ps1`。它用独立的入口、策略和依赖程序集检查实际动态解析、模块初始化、退出清理及发布依赖闭包；同时应执行默认 Editor smoke，确认工程策略、编译和工作台仍能启动。

独立发布也要求平台依赖完整声明。DotNet Editor 的 SPIR-V 适配器直接使用 Veldrid 的 ShaderStages，因而显式引用中央配置的 Veldrid 4.9，而不能依赖 SPIR-V 工具传递的旧 4.8。原有 Vulkan 校验和补丁继续执行。
