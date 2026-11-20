# Editor Play Mode：虚拟 AB 与真实 AB

在编辑器中打开 **Project Settings → Editor Play Mode**，通过 **Content Mode** 选择进入 Play Mode 时使用的资源来源。默认是 **Virtual**。这项设置由 AssetBundles 提供内容接入，EditorWorkbench 提供设置界面，独立于 Player 的资源加载模式、缓存和更新设置，不会改变应用的打包或启动策略。

设置保存在当前工程的 `ProjectSettings/EditorPlayMode.yaml`，停止 Play Mode 后才能修改；进入和退出过程也不能切换。下次进入 Play Mode 时应用新设置。

## Virtual：直接验证当前工程内容

Virtual 从当前已导入的工程资源生成一次虚拟 AB 快照，使用统一的 AB 地址、资源加载、场景及热更新入口，不要求先生成 `.bassetbundle` 文件。适合日常修改资源和脚本后的快速运行。

虚拟快照不验证物理 AB 的压缩、包文件完整性或构建后的资源格式。需要检查实际发布内容时，切换到 Real。

## Real：读取已经构建的发布目录

先完成资源构建或内容发布，再将 **Content Mode** 设为 **Real**：

| 设置 | 含义 |
|---|---|
| **AB Release Directory** | 已构建的 AB 发布目录；可以是相对工程根目录的路径或绝对路径，也可通过 **Browse AB Release Directory...** 选择。 |
| **Release Package Name** | 整个发布的 `packageName`，必须与所选发布的版本元数据一致；它不是 `fonts`、`images` 等 assetbundle package 分组名。首次设置时优先读取工程现有 `AssetBundles.yaml` 的发布身份，否则默认 `main`。 |

目录支持以下两种结构：

- 发布根目录：包含 `latest.yaml`，其版本子目录内包含 `version.yaml`、catalog 和 `bundles/*.bassetbundle`。
- 单个版本目录：直接包含 `version.yaml`、对应 catalog 和 `bundles/*.bassetbundle`。

选择发布本身的目录，不要只选择 `bundles` 文件夹或其中一个包文件。Real 直接读取这个目录，按正常运行时规则校验版本、catalog、包文件和资源内容，并按需加载物理 AB；不会生成虚拟快照来替代选中的发布。

资源、场景或代码修改后，应先重新构建对应发布，再停止并重新进入 Play Mode。源文件变化不会自动更新已经构建的 AB。只使用资源构建入口时，输出仅包含所选导入资源；需要同时验证热更新程序集及扩展包运行资源时，应使用 Player 的完整内容发布流程。

加密发布需要可用的工程内容密钥；普通编辑器源码工程没有单独的密钥文件选择项。仅验证资源读取时，可使用未加密的资源发布。

## 场景与 Resources 的行为

进入 Play Mode 时，**当前已经打开的编辑场景仍按现有机制复制为运行场景**，保留未保存的修改；退出后恢复编辑状态。这项选择不会强制用 AB 内的旧版本场景覆盖当前编辑场景。Play Mode 中按名称或路径加载其他场景时，使用所选内容管理器，Real 可读取构建后的场景数据。

Real 的目录、版本或 catalog 缺失、不匹配时会停止进入；物理包缺失、损坏或资源读取校验失败时会报告错误。对于 catalog 已声明的资源，读取失败不会悄悄改用同名工程源文件，也不会切回 Virtual。

**主包 Resources 仍然正常使用。** 未由 AB catalog 声明的 Resources 资源继续通过普通主包资源机制加载，不要求所有 Resources 都进入 AB。

退出 Play Mode 或替换内容会话时，会释放旧热更新会话及内容管理器。对 AB 加载句柄仍应遵守原有生命周期，及时 `Dispose`；编辑器资源模式切换不会改变业务加载接口。

返回 [AssetBundles 文档首页](index.html)。
