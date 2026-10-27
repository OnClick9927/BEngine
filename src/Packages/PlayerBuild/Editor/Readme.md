# PlayerBuild

本包提供 Player 构建与发布流程。依赖见 package.yaml。

Web 的 `Script Compilation` 可选择 `Interpreter`（默认）或 `AheadOfTime`。
`AheadOfTime` 将游戏域独有的脚本预编译进 Web Player，仍通过原有 AssetBundle 加载和模块生命周期激活。
启动域与游戏域共用的包使用同一份原始 DLL 和加载身份，避免分别裁剪或重复加载造成类型不兼容。
其中 UIElements 和 Audio 也会预编译；其他共享包及第三方音频解码器（NLayer、NVorbis）继续解释执行。
共享包由引用它的运行阶段共同持有，阶段切换不会提前移除另一阶段仍使用的注册。
构建会补齐共享包的代码依赖，不扩大已确定的资源集合；游戏脚本及其依赖的原始 DLL 会检查 SHA-256 与 MVID。
此模式下修改游戏脚本或任何固定依赖的代码，需要重新构建并部署完整 Web Player，
不能只发布 AssetBundle 替换已编译代码。资源内容仍可通过原有内容发布流程更新。
`PlayerBuildRequest.WebScriptCompilation` 与 Player Settings 使用同一选项。

同一 Hub 下的工程通过 `Hub/Cache/ContentPublishLocks` 协调对同一输出包的并发发布。
该目录仅在发布时创建，锁释放后保留协调文件；不要在构建进行中删除此缓存目录。

Player 与热资源默认输出到当前工程的 `Build` 目录，分别为 `Build/<player>` 和 `Build/hotres`。
编辑器菜单与构建 API 使用相同的路径校验：工程内部只允许 `Build` 及其子目录，
工程根目录、父目录以及 `Assets` 等其他工程内部目录不能作为 Player 替换目标。
也可以输出到工程外部的普通目录；输出路径不允许经过符号链接或目录连接。

Player Settings 的 `Compress Resources` 控制所有工程的主包与热更资源压缩，
对应原有 `CompressAssetBundles` 配置字段，默认开启。主包 `aot.bresources` 与 `player.bresources`
按资源使用 Brotli 压缩，相同载荷继续共享存储；小资源或压缩收益不足的资源原样保存。
若整个归档的压缩收益不足以抵消新增索引字段，则自动输出原来的 v2 格式，避免包体反而变大。
有收益的归档采用 v3，新 Player 同时读取 v1/v2/v3；发布主包需要使用匹配的新引擎运行库。
压缩在混淆和加密之前进行，运行时只解码所请求资源，异步读取按 64 KiB 协作让出并支持取消，
不创建工作线程，也不将解压数据写到磁盘。热更 Bundle 格式保持原有实现。
切换压缩设置会改变内置内容版本。此功能与 Managed Stripping 独立，Disabled/Conservative/Full 均可使用。

`File/Build Settings...` 顶部的 Build Profile 工具栏管理工程内的命名构建配置。配置保存在
`ProjectSettings/BuildProfiles.yaml`，可创建、复制、重命名、删除和激活；每个配置独立保存目标平台、
场景列表、输出目录、开发构建选项、内容服务地址、热更新启动场景和脚本编译符号。激活配置时会把设置
同步到兼容的 `BuildSettings.yaml`，并在脚本符号变化时请求重新编译。

热更新启动场景必须位于 `Assets` 下、实际存在且不能是固定 AOT Bootstrap 场景。构建清单和内容版本哈希
都包含该场景，Player 启动时读取构建配置中的值，不再固定使用 `Project.yaml`。脚本符号使用分号分隔，
保存时会校验标识符并去重。

编辑器 API 为 `PlayerBuildProfileStore`：`Ensure`、`Create`、`SetActive`、`Rename`、`Delete`、
`GetActive` 和 `ApplyActiveScriptingDefines`。构建期间编辑器正在编译时，Build 按钮保持禁用。

在 Player Settings 将 Managed Stripping 设为 `Full`，并启用 Self Contained，可裁剪主包。
新建配置默认使用 Full，已有配置中显式保存的 Disabled/Conservative 继续生效。
Full 对 Host 和内置 AOT 程序集分别运行 ILLink：移除未被调用的程序集、类型和方法，
保留启动场景、UI 标记、序列化成员、生命周期回调和热更新对 Host 的接口依赖。
项目任意深度的 `Resources` 目录及其运行时依赖自动进入主包 `aot.bresources`，
不要求已有场景或代码引用，Full 裁剪也保留这些资源。目录名匹配忽略大小写；
任意 `Editor` 目录内的内容不会进入 Player，包括 `Editor/Resources` 和 `Resources/Editor`。
主包还收集 AOT 场景依赖、保留下来的代码字符串和保留配置，支持资源 GUID、Assets 地址、
Resources 短路径和目录引用。删除方法后再次收集其他资源，避免只被无用方法引用的文件进入主包。
热更新 AB 排除主包 Resources 及其依赖，避免重复发布。
工程根目录的 `Assets/StreamingAssets` 通过单独的原样复制流程进入
`<player>_data/StreamingAssets`，不进入 `aot.bresources`、`player.bresources` 或热更新 AB。
复制保留相对路径、文件名大小写与字节内容，排除 `.meta`、编辑器目录及版本控制、元数据锁和临时文件，
并拒绝符号链接或目录连接。该目录不参与资源压缩或裁剪。
包资源同样按引用筛选，并继续应用目标图形后端的 Shader 过滤。
保留下来的包标记资源继续扩展组件类型与项目资产依赖，代码与资源图一起迭代到稳定。
如果裁剪图中没有 SkiaSharp/HarfBuzzSharp 文本绑定，Full 构建会同时移除对应托管程序集和原生库；需要文本渲染时会自动保留。

成品的 `Application.dataPath` 指向 `<player>_data`，`Application.streamingAssetsPath` 指向其下
大小写固定的 `StreamingAssets` 目录。`Application.persistentDataPath` 由平台按工程的 Company Name、
Product Name 提供，与安装目录分离；`BENGINE_PLAYER_PERSISTENT_DATA_PATH` 可用绝对路径覆盖此位置。
包内文件不会由平台宿主自动写入持久目录。需要离线 AB 的工程可选择调用 AssetBundles 包的
`IAssetBundleLocalInstaller`，把 `streamingAssetsPath` 下的发布目录验证并安装到 `persistentDataPath`，
再初始化 AB 管理器。具体版本替换与损坏修复规则见 AssetBundles 包文档。

由运行时拼接路径、远端配置或任意反射调用到的内容需要显式声明。
在工程 `ProjectSettings/PlayerStripping.yaml` 中填写：

```yaml
format: BEngine.PlayerStripping
version: 1
preservedAssets:
  - Assets/AOT/Resources/RuntimeConfigured
  - Assets/Packages/com.example.ui/Runtime/Resources/Fonts
preservedTypes:
  - AOT.RuntimeConfiguredComponent
  - MyPackage.ReflectionFactory
```

资产保留项支持文件和目录。显式项目资产保留项必须在 Assets/AOT 内，包资源用其实际 Assets/Packages 地址。
项目 Resources 及其依赖会自动保留，无需填写这些声明。
引擎资源使用归档地址 Assets/Resources，例如 Assets/Resources/Icons/BEngine.64.rgba。
不存在的保留项会报错。类型保留项必须使用完整类型名；需要只保留某些方法或整个程序集时，
可改用标准 ILLink 描述文件。构建会递归发现 `Assets/**/link.xml`，并兼容
`ProjectSettings/link.xml`；全部规则合并后同时用于 AOT 与 Host 裁剪。程序集名
必须准确，类型支持通配符及嵌套类型的 `+` / `/` 写法。例如：

```xml
<linker>
  <assembly fullname="AOT">
    <type fullname="AOT.RuntimeConfiguredComponent" preserve="nothing">
      <method name="Create" />
      <property name="Configuration" accessors="get;set" />
    </type>
  </assembly>
</linker>
```

支持 assembly/type 的 `preserve="all|nothing|fields|methods"`，以及
method/field/property/event 的 name 或 signature。可选依赖使用
`<assembly fullname="Optional.Package" ignoreIfMissing="true">`，缺失时跳过；
必需依赖缺失、非法 XML 或 preserve 值会在构建时报错并给出文件路径。
Unity 专用的 `ignoreIfUnreferenced` 不会被静默忽略，当前会明确报错；
条件类型根可使用 ILLink 的 `required="false"`。

包的 `IEngineServiceModule` 构造器与服务注册契约和热更模块一样自动保留。
热更域在激活时持有自身类型优先级，准备新域不会替换旧域；卸载/回滚会恢复
旧域优先级与默认服务。同名的旧 AOT 场景系统不再与活跃 AB 系统重复运行。

构建报告位于工程 `Temp/BuildReports/stripping-*.yaml`，记录 AOT/Host 程序集与资源的保留原因和字节数；
相邻的 `.publish.log` 保留完整 Host 发布诊断，`LinkDescriptors` 记录所有显式规则文件。
API 返回值 `PlayerBuildResult.StrippingReportPath` 可定位报告。
报告中的 `AotArchiveBytes`、`PlayerResourceArchiveBytes` 是最终归档文件大小，包含索引和加密开销；
资源 `StoredBytes` 是压缩后的载荷长度，`PublishedBytes` 继续表示解码后的发布资源长度。
去重资源共享载荷，不能简单累加每行 `StoredBytes` 推导归档大小。
项目资源的 OriginalBytes 是导入产物大小，PublishedBytes 是目标平台编码后、归档压缩前的资源大小，包含子资源。
纹理格式转换可能增加字节数，因此这两项不能直接当作同一编码格式的压缩率。
裁剪缓存也位于工程 Temp，成功后清理，失败时保留 ILLink 日志。
热更新资源包继续独立发布；Full 根据当前热更新代码保留其 Host 合约。
未来热更新若使用原先没有保留的 Host API，需要在构建主包时通过 link.xml 预留，或重新发布主包。
建议每次改变动态保留规则后运行裁剪成品的启动与业务验收。

在 `Player Settings / Build` 启用 `Encrypt Code And Resources` 后，Build、Build And Run 和
Build Hot Resources 会打开密钥弹窗。输入并确认 Key，或生成 256 位随机 Key；自行保存生成的 Key，
同一主包的后续热更新必须使用同一个 Key。密钥输入不做 Trim，大小写和空格都参与派生，要求
16-1024 个 UTF-8 字节。项目设置和 Build Profile 只保存加密开关，不保存 Key。
构建结果的 `ContentEncryptionKeyFingerprint` 和 Full 裁剪报告记录指纹，可核对主包/热更新密钥是否一致。

构建 API 使用 `PlayerBuildRequest.EncryptContent = true` 和 `EncryptionKey = "..."`。
独立 AssetBundle 构建可使用 `AssetBundleBuildOptions.EncryptionKey =
BEngine.Content.ContentEncryptionKey.FromPassphrase(key)`，独立运行时对应配置
`AssetBundleRuntimeOptions.EncryptionKey` 和 `RequireEncryptedContent = true`。

覆盖主包 `aot.bresources` 内的 AOT/包业务程序集、PDB 和项目/包资源，`player.bresources` 内的
引擎资源、启动元数据及 Splash，以及热更新所有 `.bassetbundle` 内的程序集、PDB、项目/包资源。
采用 PBKDF2-SHA256 派生密钥和 64 KiB 分块 AES-256-GCM；头部及各分块均有认证标签，错误密钥、
截断和内容篡改会导致加载失败。运行时按需解密到内存，不生成磁盘明文副本，也不创建工作线程。
下载哈希、文件大小和缓存验证针对密文；解密后继续执行原资源哈希验证。
相同内容与密钥产生相同密文，支持不可变热更新版本的重复发布，并会暴露内容是否相同。

为支持离线自动启动，主包 Player 资源容器内包含经过变换的派生密钥；AOT 容器和热更新容器不包含密钥。
这提供发行资源/缓存的加密存储，但客户端自带解密能力，不能保证密钥无法被逆向提取。
用于启动的静态 Host/运行库仍由系统/.NET 正常加载，此功能不加密可执行文件里的静态引擎 IL 或原生 DLL。
当前支持原生 Player 加载；Web 构建启用加密会明确报错，尚未接入浏览器加密加载后端。
更换密钥必须重新发布主包并建立新的热更新发布版本，不能用新密钥覆盖旧的不可变版本。

Player Settings 的 Graphics Build 选择实际链接的图形后端。空的 `GraphicsBackends` 使用平台已实现的默认后端；
Windows 可选择 Vulkan、OpenGL 或两者，Linux/macOS 内置桌面 Host 当前使用 OpenGL。
资源收集、启动清单、编译符号与导出 SDK 采用同一选择，运行时偏好不能越过清单启用其他后端。
导出引擎包含 `BuildRuntime/Desktop/OpenGL`、`Vulkan`、`VulkanOffline`、`BothOffline`，
默认兼容 SDK 位于 BuildRuntime 根目录。源码构建同样按选择编译，不依赖删除发布目录中的 DLL。

`CompileShaders` 默认开启，为 Vulkan GLSL 顶点/片元 Shader 生成 SPIR-V。
按后端、阶段、语言、入口与完整源码计算标识，字节码随所属主包/热更资源一起压缩、加密和校验。
运行时先读取离线产物，再使用有界缓存；`RuntimeShaderCompilation` 默认开启以支持自定义动态 Shader。
关闭后将从桌面 Host 移除 Veldrid.SPIRV 及其编译器原生库，缺少产物会明确报错。
GLSL 源码仍用于运行时描述和产物标识；OpenGL 继续由驱动编译，GPU Pipeline 仍由驱动创建。
本次离线编译不代表 Metal/D3D/WebGPU 等后端也有离线编译器。

包编辑器可实现 `IPlayerShaderSourceProvider`，输入当前分区的导入资产与包资源，返回最终生成的源码变体。
普通资源采用 `.vulkan.vert.glsl` / `.vulkan.frag.glsl` 后缀；含 `__NAME__` 占位符的模板由包自己的 Provider 展开。
内置 Lighting2D、SpriteSkinning2D 与 ShaderGraph 已提供构建扩展，并复用运行时源码生成函数。
只收集当前分区资产和资源对应的变体；运行时新建或修改的图、自定义代码生成器须提供相应构建扩展，或保留运行时编译器。

每次成功发布 Host 会在工程 `Temp/BuildReports/host-contract-*.json` 导出实际保留的程序集、类型、方法、字段与转发器。
API 通过 `PlayerBuildResult.HostContractPath` 返回；编辑器构建成功后自动将其保存到当前 Build Profile。
发布热更时设置 `PlayerBuildRequest.HostContractPath`，会在生成并发布资源前检查目标主包是否保留其 API。
平台、后端和编译器策略必须匹配；缺失 API 时停止发布，须提前用 link.xml 预留并重新打主包。
未提供合约的 API 调用保持兼容，不检查未来 API；跨机器构建须复制主包对应的合约并重新指定路径。
普通成员按调用保留，类型引用不再自动保留所有构造器/属性/虚方法；序列化、反射、原生布局和泛型重载仍有保守规则。
Host 构建临时文件也位于工程 `Temp/PlayerBuildArtifacts`，成功或失败后清理；报告保留。
