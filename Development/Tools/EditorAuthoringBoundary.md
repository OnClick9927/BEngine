# 共享编辑能力与默认工作台的边界

`EditorWorkbench` 组合默认窗口、布局、菜单与宿主，不提供其他功能包必需的共享编辑 API。功能包不依赖工作台；默认发行版仍自动安装必需的工作台包及其依赖。

## 类型归属

```text
EditorServices.Editor
  ProjectWindowUtil：创建资产、读取当前资产目录、发送定位或 Ping 请求
  EditorProjectSettings：工程设置 v3 的读写、校验、应用与修改通知
Rendering2D.Editor
  SceneView / SceneViewToolContext：静态场景工具入口与 GUI/世界坐标转换
  SceneHandleUtility：Sprite 视觉包围盒的手柄中心
  ScenePickCandidate / ScenePickingProvider / ScenePickingProviderRegistry
  ScenePickingUtility：场景拾取、可见性过滤、排序与扩展合并
  GizmoDrawerRegistry / SceneGizmoPass / SceneGizmoVisibility
  Rendering2DBuiltInGizmoDrawers：Camera、Sprite、SpriteMask 的 Gizmo
EditorWorkbench
  默认 Scene 窗口、Gizmo 菜单、工具交互及窗口呈现
  其他默认窗口、停靠、布局、设置窗口、包管理窗口与宿主
```

命名空间保持 `BEngine.Editor`。共享公共类/结构的旧 `BEngine.EditorWorkbench.Editor` 程序集限定名由所属类型的 `RuntimeTypeAlias` 保留；不在 Core 中添加类型转发。拾取委托不持久化，仓库内引用其签名的程序集同步重编译。资产 GUID、场景、Prefab、工程设置格式不变。

需要实现自定义场景编辑宿主时，在自己的 IMGUI 帧内调用 `SceneView.DrawTools(viewport, camera)`，返回值表示扩展工具是否抑制默认工具；结束交互时调用 `SceneView.CancelToolInteraction()`。通过 `ScenePickingUtility.PickAll` 获取按前后顺序排列的可拾取对象。扩展包继续订阅 `SceneView.duringSceneGui` 或注册 `ScenePickingProviderRegistry`。这三个宿主入口为公开接口，没有增加友元授权。

Rendering2D Editor 的模块初始化负责共享手柄策略与 Gizmo 预热；卸载清除拾取提供者、Gizmo 类型/方法/编译回调缓存，取消自己的生命周期订阅。若宿主替换了手柄策略，卸载不清空宿主的替代实现。普通共享资产 API 和工程设置不需要 IMGUI、Graphics、Rendering2D 或工作台。

## 验证

```powershell
& ./Development/Scripts/SetToolEnvironment.ps1
$verification = 'E:/Project/_BZP/BEngine/Temp/Engine/Verification/EditorAuthoringBoundary'
& ./Development/Tools/Test-EditorAuthoringBoundary.ps1 `
    -EngineBinaries '<已导出引擎>/BEgine/BEgine-0.1.0' `
    -OutputDirectory $verification
```

测试以 ServicesOnly 和 Scene 两个独立程序执行，均不复制或加载工作台程序集。验证资产创建与定位、设置读写校验、旧类型标识、投影、工具派发、拾取顺序与过滤、扩展贡献失败隔离、Gizmo、注册与卸载。内部宿主替身和 Gizmo 检查只在测试中使用反射，不增加生产友元或测试专用公开 API。

`Test-PackageBoundaries.ps1` 同时检查 Core/Platform 无具体包依赖，以及普通功能包清单、asmdef 和项目文件没有工作台依赖。实际程序集引用应另与清单依赖图核对。基准工程位于 `Tests/EditorAuthoringBoundary/Benchmark`，对同一场景测量拾取和工具派发；需要使用拆分前后相同配置分别构建，不能把测试程序启动耗时当作编辑器启动耗时。

本次验证记录位于 `Temp/Engine/Verification/EditorAuthoringBoundary_20261111`。整个架构审计仍包含绘制诊断阶段定义、AB 内容/代码策略和历史友元访问的后续检查，本次边界迁移不代表整体审计结束。
