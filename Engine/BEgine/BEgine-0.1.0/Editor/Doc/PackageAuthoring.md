# 包版本与工程内开发

引擎包仓库使用 `Engine/Engine/Packages/<包目录名>/<包目录名>-<版本>/package.yaml`。
例如 `Engine/Engine/Packages/Audio/Audio-1.0.0/package.yaml`。不同版本共存；源代码仓库
`Engine/src/Packages` 的开发目录保持原结构。`ExportEngine.ps1` 和程序集构建导出均写入版本子目录。
旧的平铺导出目录可由 `Engine/src/MigratePackageLayout.ps1` 迁移；重复执行不会再次移动已迁移包。

每个新发布的包必须声明：

```yaml
format: BEngine.Package
version: 2
id: com.studio.inventory
type: package
packageVersion: 1.0.0
minimumEngineVersion: 0.1.0
displayName: Inventory
runtime:
  assembly: Studio.Inventory
  rootNamespace: Studio.Inventory
editor:
  assembly: Studio.Inventory.Editor
  rootNamespace: Studio.Inventory.Editor
```

`minimumEngineVersion` 声明包要求的最低引擎版本，包含边界。例如 `0.1.0` 表示引擎版本
达到 `0.1.0` 即可进入候选集合。Hub 使用所选引擎版本筛选，编辑器使用当前运行的
`EngineVersion.Current` 判断。版本按 SemVer 比较：正式版高于同版本预发布版，构建元数据
不影响比较结果，非法版本会被拒绝。候选包还需要满足必需依赖及程序集分层规则。

新发布与官方导出要求明确最低引擎版本，并检查编译引擎达到此版本。历史已安装清单缺少
最低版本时，以 `0.0.0` 读取。最低版本是作者声明的要求，具体功能仍需通过编译和运行验证。

## 选择和切换版本

Hub 的“引擎与包”页将引擎版本和包目录合并显示。先选择已安装的引擎版本，包列表只展示
适用于该版本且必需依赖可用的包；同一包 ID 的新版本不可用时，会尝试较旧的兼容版本。
依赖图使用相同范围的数据，切换引擎时同步更新。声明的可选依赖缺失或不兼容时，
图中显示不可用节点，不会用其他引擎版本的包补齐。启动页偏好中的旧“包”选项会迁移为“引擎与包”。

Package Manager 左侧每个包 ID 只显示一行，包含仓库里尚未安装的兼容包。
右侧 `Version` 列出兼容版本；切换下拉框只预览该版本详情，点击 `Import` 或 `Use Version` 才安装。
`Installed` 显示工程实际使用的版本。`Packages/manifest.yaml` 固定保存所选版本，刷新、重启和
发现更高版本时不会自动升级。固定版本不在仓库时，可继续使用身份、版本和兼容性均匹配的工程缓存。
编译或加载失败会回滚清单和缓存，再恢复原程序集。

`type: project` 包导入后属于工程可编辑内容；为保留用户修改，不提供覆盖升级或一键删除。
引擎扩展包 `type: package` 支持版本切换。依赖按包 ID 解析，不提供依赖版本范围求解。

## 在工程内创建模板

Package Manager 顶部工具栏左侧是搜索框和刷新图标，右侧是创建（加号）和导出图标。
点击 `Create package...`，在按钮旁打开有尺寸上限的 Pop EditorWindow，不遮罩整个编辑器。
填写包名、包 ID、显示名、根命名空间、版本及描述；最低引擎版本通过下拉框选择。
下拉框读取当前版本及 `Engine/Engine/BEgine` 中已安装的有效引擎版本，按版本倒序排列，默认选中当前版本。
包名用作包目录和示例类型名称前缀，须以英文字母开头，可包含数字和下划线；显示名和描述可使用中文。
根命名空间默认建议 `Project.<包名>`，可填写 `Studio.Inventory` 或 `Studio.Tools` 等由点分隔的标识符。
两个程序集目录直接使用填写的根命名空间和 `<根命名空间>.Editor`，不再追加包名。
默认包名为 `NewPackage`，若已存在则建议数字后缀。弹窗预览生成路径，校验通过后点击 `Create`，
才会生成当前工程的 `Assets/Packages/<包名>`；取消不写入文件。
模板包括 `package.yaml`、运行时组件、编辑器窗口、两份 `.asmdef.yaml`、资源目录及说明。
源码直接参与工程编译，可用 `Edit/Recompile Scripts` 调试，窗口菜单为 `Window/<包名>`。
创建模板不写入 `Engine/Engine/Packages`，也不修改其他工程或自动安装发布版本。
例如包名 `Inventory`、根命名空间 `Studio.Inventory`：

```text
Assets/Packages/Inventory/
  package.yaml
  Studio.Inventory/
    Studio.Inventory.asmdef.yaml
    Runtime/InventoryExampleComponent.cs
  Studio.Inventory.Editor/
    Studio.Inventory.Editor.asmdef.yaml
    InventoryWindow.cs
  Resources/
  Editor/Readme.md
```

清单与 asmdef 中 Runtime 的程序集名和根命名空间均为 `Studio.Inventory`，Editor 均为 `Studio.Inventory.Editor`。
生成的 C# 文件使用对应命名空间；Editor asmdef 引用 `Studio.Inventory`。不同包应使用不同根命名空间，以免程序集重名。
发布前检查包 ID、显示名、版本及最低引擎版本。若修改程序集名或命名空间，需要同步源码目录、
清单与 asmdef。Editor asmdef 设置 `editorOnly: true` 并引用自己的 Runtime 程序集。
外部包依赖同时写入清单的 `dependencies` 和 asmdef 的 `references`。

## 导出工程内的包

点击右侧 `Export package...` 图标打开 Pop EditorWindow。`Source package` 使用下拉框，
只列出当前工程 `Assets/Packages` 直属目录中含有 `package.yaml` 的包；刚创建的包优先选中。
没有可选包时显示提示并禁用导出。源目录和目标目录均不提供自由输入或文件夹浏览。
目标固定为与 `Assets` 同级的 `Packages-Exported/<包目录名>/<包目录名>-<packageVersion>`，
选择包后预览完整路径。例如包名 `testPackage`、版本 `1.0.0`：

```text
工程目录/
  Assets/
    Packages/testPackage/
  Packages-Exported/
    testPackage/
      testPackage-1.0.0/
        package.yaml
        ...
```

只有点击 `Export` 且校验通过后才写入目标；同一 ID/版本禁止覆盖。
弹出窗口支持取消、Escape 或点击窗口外关闭；编译进度窗口出现时会保留表单。
鼠标位于 Pop 窗口内时，点击、滚动、拖动及进入/离开事件由弹窗接收，下层窗口不会响应，也不会显示悬停提示或改变光标。

导出前会重新检查所选包仍位于 `Assets/Packages` 下，并检查清单、版本、目录边界、符号链接、
程序集名称、Runtime/Editor 分层、asmdef、依赖闭包和源码。
使用独立临时工程编译待导出快照，不引用游戏工程的程序集，也不改动当前工程的程序集输出指针。
编译通过后，以目录重命名的方式导出完整快照。失败时弹窗保留输入并显示原因，编译日志位于工程 `Temp/PackagePublish/<编号>/Logs`。

此操作生成工程本地可分发的版本目录。需要在其他工程的 Package Manager 中使用时，
可将导出的包名目录复制到 `Engine/Engine/Packages`，然后刷新包列表。
脚本 API `BPackageAuthoring.GetProjectExportPath(workspace, source)` 预览路径，
`ExportProjectPackage(workspace, source)` 使用相同的源目录限制和固定目标。
底层 `Export(workspace, source, target)` 与 `Publish(workspace, source)` API 保留显式导出和向引擎仓库发布的行为。

发布内容仅包括清单、程序集源代码、Resources 和 Editor；跳过构建目录和 csproj，拒绝 DLL/PDB。
同一 ID 和版本已存在时拒绝覆盖，请增加 `packageVersion`。
此按钮面向 `type: package` 引擎扩展；普通工程资源仍可使用 `Assets/Export Package...` 导出 `.bpackage`。
Play 模式及切换期间禁止创建和导出。

发布操作不会在作者工程再次安装自己的包：该工程已在编译模板源码。
在其他工程安装发布版本，或先移走作者工程的源码，再在该工程安装，避免同名程序集重复。

`BENGINE_PACKAGES_PATH` 仍可覆盖本地仓库根目录，适用于隔离测试和自定义安装。
