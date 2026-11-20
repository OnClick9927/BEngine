# Player 内容构建与通用部署的职责

Core Player Editor 持有构建事务和通用文件部署；内容包解释自己的内容格式；Graphics Editor 解释图像格式并合成启动图像。Application 不参与这些机制。

构建顺序为内容 `PrepareAsync` → Core 准备原始文件、目标清单、启动图像和部署清单 → 内容 `FinalizeAsync` → Core 清理自己的中间文件 → 平台构建 → 校验、事务发布。任意阶段失败或取消继续使用原有 staging 清理及已发布产物保护。已有内容提供方可使用默认空 `FinalizeAsync`，无须接管通用中间文件清理。

`PlayerBuildLayout` 是公开的引擎部署地址契约。计算地址不产生 IO；显式 `CreateDataDirectories` 才创建目录。输出位置、大小写及事务保护仍由 Core 管线执行。开放路径不表示绕过发布事务，也不授予包访问 Core 私有实现的权限。

Core 复制 `Assets/StreamingAssets`，保留大小写和原始字节，跳过辅助元数据及编辑器/版本管理目录，拒绝链接入口。它通过 `IPlayerStartupImageBuilder` 取得不透明 PNG 字节，不解码或绘制。Graphics 注册实现，负责 PNG 校验、默认 Logo 定位和透明角标合成；程序集退出时由已有默认服务清理机制撤销注册。原应用图标保持未叠加的字节；实际闪屏图像保留原有角标、尺寸和留白规则。内部关闭闪屏的 staging 路径不访问图像，常规发布仍保持原有强制闪屏策略。

Core 提供 `PlayerBuildContext.DeploymentManifest`。AssetBundles 在完成阶段把它写入自己的运行元数据，并封装其资源归档；AB 元数据及归档格式、内容版本和更新策略继续属于该包。`PlayerBuildOutputValidation` 只校验目录结构和提供方给出的资源文件集合，不解释 AB 归档或硬编码资源格式名单。平台适配中 AB 的归档内容校验仍在 AB 包。

Graphics Editor 在 VS 工程、包清单的 hostReferences 及 asmdef 中声明 Core Player Editor 编译引用，未增加包间清单依赖。Core、Platform 无反向包引用。此次新增接口均是 Editor 构建能力，不进入 Player 运行时。

验证脚本 `Test-PlayerStagingBoundary.ps1` 包含不加载 Graphics/AB/UI 的自定义内容发布、阶段顺序、清单及原始文件、失败回滚、取消、链接拒绝和中间文件清理；Graphics 探针覆盖默认/自定义/细长/微小 Logo、保留原图、错误输入及注册清理。临时验证另以同一探针对比迁移前后的图像字节，再运行安装版回归和 Example/Snake Windows/Web。完整证据及未完成架构范围见对应 Temp 验证报告，不把局部测试作为所有包审查完成的证明。
