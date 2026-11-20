# 生成文件子资源的公开导入能力

`EditorServices` 的 `GeneratedAssetArtifacts.Write(ownerSourcePath, localIdentifier,
extension, contents)` 提供通用生成文件存储。它返回所有者 GUID、局部 ID、稳定引用
和 Library 中的物理路径。TextureAtlas 与 ArtImport2D 只决定内容格式和导入设置，
不拥有通用路径、身份登记或事务回滚。独立导入器无需友元权限，也无需 GUI 或图形包。

所有者必须是 Assets 或 Packages 下已存在的源文件，路径使用绝对路径。内容可以是
任意格式；扩展名只接受字母和数字，并规范为小写。参数、扩展名、源路径和文件存在
检查在创建元数据之前完成。默认输出位置和 `guid:<owner>#subasset=<local>` 格式
保持不变，已有子资源 GUID 不变。

写入失败会尽力恢复原始输出及元数据，回滚失败保留原始错误。Delete 按所有者和
局部 ID 删除生成文件，不删除源文件或所有者元数据；删除阶段失败会恢复已暂存文件。
这不是进程崩溃恢复日志。后台资源刷新在 Play Mode 下也可能运行，所以这一产物
导入能力允许更新 Library 数据；修改源资产和手动构建图集仍经过各自创作写入检查。

服务不触发全工程扫描或隐式刷新，避免在导入回调中重新进入管线。正常导入由管线
继续发布和登记；外部创作工具完成后可调用 AssetDatabase.ImportAsset 或 Refresh。
索引、元数据和资产提供者仍由共享资产机制管理。

`AssetDatabase.GetAssetSourcePath` 是活动工程 Assets 内的受约束源路径解析接口，
支持绝对路径、Assets 开头的工程相对路径和 Assets 内相对路径，不解析 GUID 或
返回 Library 产物。需要工程上下文；无窗口的导入工具可直接使用源文件绝对路径。
图集工具使用这一接口、EditorApplication.projectPath 和 AssetDatabase.Refresh，
不直接访问私有默认宿主。

本机 UnityEditor.CoreModule 的 AssetImporters.AssetImportContext.AddObjectToAsset
及 GetArtifactFilePath 等公开元数据作为通用资产导入能力的边界参考；不推断 Unity
私有产物格式和原生事务实现，也不声称此 API 与 Unity 的对象子资源 API 完全等价。

测试覆盖独立导入器、精确字节回滚、身份冲突、输入范围、Play Mode、冷导入、
真实 Editor 的生成纹理解码、增量导入与删除；验证产物位于工作区 Temp。
