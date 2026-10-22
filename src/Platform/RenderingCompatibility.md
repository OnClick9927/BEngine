桌面渲染兼容性修复（2026-10-04）

桌面 Player 和 Windows 编辑器在创建窗口时自动验证实际图形设备。按候选顺序尝试当前实现的 Vulkan / OpenGL；每次尝试都创建独立的隐藏窗口，验证设备创建、着色器、顶点和 Uniform 更新、纹理更新、透明混合及 GPU 像素回读。成功后保留该窗口及设备。失败时释放候选设备和窗口，再创建下一种 API 的窗口。项目偏好仅影响尝试顺序，旧项目无需修改配置即可获得回退行为。

启动日志包含 `Automatic graphics selection`、实际 GPU 名称和失败原因。`SystemInfo` 发布最终设备的信息。自检针对驱动实际分配的 GPU；本次没有增加 Vulkan 物理设备选择接口。选择发生在启动阶段，不会在正在运行的游戏中迁移资源或切换 API。

Vulkan 缓冲区更新改为按绘制保留数据版本：

- 网格和 Uniform 使用可复用的 host-visible / host-coherent 动态缓冲区。当前帧已经引用的版本不能被后续更新覆盖。
- CPU 在队列提交前写入独立版本；下一帧等待原有 fence 后复用。即使本帧先绘制旧数据、再更新同一资源，也会保留前一次绘制的数据。
- 这样不再通过 Veldrid 4.9.0 的 CommandList.UpdateBuffer 在绘制之间插入复制命令，避开该路径对 Uniform 读取同步覆盖不足、频繁拆分 render pass 的问题。没有替换或修改 NuGet 依赖。
- 同帧已被采样的可变纹理在更新时创建新图像版本，重建相关 descriptor；旧图像和 descriptor 在 fence 完成后回收。帧外更新先等待已提交帧。此前的 WaitForIdle 无法保护尚未提交的绘制命令。
- OpenGL 多窗口在使用和释放资源前恢复所属 context。

回归验证：

```powershell
dotnet run --project Temp/Validation/DesktopGraphics/DesktopGraphics.csproj -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false
dotnet run --project Temp/Validation/UIElementsDemo/UIElementsDemo.csproj -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false
```

DesktopGraphics 验证覆盖：

- 一帧中复用并修改网格、Uniform、纹理，验证前后两次绘制各自保留正确像素；下一帧先绘制旧数据再更新；透明混合。
- 注入首选后端初始化失败，确认重新创建窗口并通过 OpenGL 回读，且 SystemInfo 没有保留失败设备的信息。
- 两个 OpenGL 窗口交替绘制；关闭其中一个后，另一个仍能正确绘制和回读。
- 两个后端都失败时汇总诊断，之后再次创建正常设备。
- Player 的实际场景渲染器初始化失败触发回退；成功实例直接交给渲染上下文，不重复创建。
- Player 加载事件、真实场景渲染、关闭，确认关闭后原生窗口仍保留到渲染器和输入回调释放完成；编辑器初始化、文字绘制、关闭。
- Vulkan / OpenGL 各 160 帧动态中英文文字、各 640 个不同文字纹理、每帧四次绘制、逻辑尺寸变化、缓存淘汰，逐帧逐行检查文字和背景像素。

旧版已导出的 Vulkan DLL 在新增的纹理版本回归中确定失败：应为红色的先前绘制读到了后续绿色纹理，像素为 `(0,128,127)`，而非 `(255,0,0)`。修复版同一检查通过。这证明修复了一个可重现的资源版本错误，但不能据此宣称已经复现用户那台机器的全部问题。

本机验证实际使用 Intel UHD Graphics；上述检查以及 UIElementsDemo 六种 API/分辨率配置全部通过。Radeon 780M / RTX 4060 故障机仍需要用重新打包后的应用回归。启动像素探测是有界场景检测，不能覆盖所有驱动或业务绘制组合。

发布验证（2026-10-04）：

- `Engine/src/ExportEngine.ps1 -Configuration Release` 成功，输出 `Engine/Engine/BEgine/BEgine-0.1.0`。导出的编辑器和桌面 BuildRuntime 的 `BEngine.Platform.Desktop.dll` 与本次 Release 编译产物 SHA-256 一致。
- `Tools/Build-UIElementsDemo.ps1 -SkipEngineExport` 成功，重新生成 `Example/Project/Build/ui-elements-demo/uielements demo.exe`。
- `Temp/Validation/Tools/Verify-UIElementsDemo.ps1 -Runs 3 -SkipSource` 成功：编辑器两次 Play Mode、六轮交互；打包 Player 三次独立启动、每次三轮交互。三个 Player 日志均记录 `Automatic graphics selection: Vulkan (Intel(R) UHD Graphics); pixel verification passed.`。
- 本轮导出、打包与验证日志保存在 `Temp/Engine/Diagnostics/UiFlickerProbe/Export-final.log`、`Packaged-build.log` 和 `Packaged-verify.log`。

已有应用需要使用新版引擎重新打包，才能包含上述修复和自动选择逻辑；用户电脑无需设置渲染器参数。
