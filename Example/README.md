# BEngine 默认 2D 示例

打开 `Project.yaml` 后，编辑器会加载 `Assets/Scenes/Main.scene.yaml`。这个场景是一个可以直接运行和拆解学习的最小 2D 工程，展示资源导入、Sprite 图集、渲染层、相机、粒子、脚本生命周期和输入。

## 直接运行

1. 通过 Hub 打开本目录下的 `Project.yaml`。
2. 等待脚本编译和资源导入完成。
3. 点击 Play，Game 窗口会立即获得一次焦点。
4. 使用 `WASD` 或方向键移动右下角的 Navigation 图标。
5. 停止 Play。Player 位置、旋转动画和粒子状态会立即恢复到播放前状态，运行时数据不会写回场景资源。

如果工程启动失败，Hub 或 Editor 会弹窗显示原因。详细日志位于 `Output/EditorData/Logs`。

## 场景结构

- `Environment`：Background 层上的纯色 SpriteRenderer，组成背景、地面和左右装饰。
- `Atlas Showcase`：Gameplay 层上的四张功能卡片、BEngine 标志和可移动 Player。
- `Foreground Effects`：Foreground 层上的两个 ParticleSystem2D，共用 Spark 图集区域。
- `Inset Camera Content`：只属于 Inset Only 层的独立内容。
- `Main Camera`：渲染 Background、Gameplay 和 Foreground 三层。
- `Inset Camera`：只渲染 Inset Only 层，并通过 `Viewport Rect` 叠加在右上角。

Hierarchy 名称直接说明对象用途。选中任意图标即可在 Inspector 中查看 Sprite、Sorting Layer、Order in Layer 和脚本参数。

## Sprite 与 Texture Atlas

`Assets/Art/Sources` 中的 PNG 由 TextureImporter 以 `Texture Type = Sprite` 导入。每张 Texture 会产生一个 Sprite SubAsset：

- `BEngine.png`：中心品牌标志。
- `Animation.png`：左上角动画图标。
- `Physics2D.png`：右上角物理图标。
- `Navigation2D.png`：下方导航图标和 Player。
- `Spark.png`：粒子贴图。

`Assets/Art/Showcase.atlas.yaml` 保存这些 Sprite 的 GUID 与 Local Identifier。SpriteRenderer 仍然只引用 Sprite，不引用 TextureAtlas；编辑器把生成纹理作为图集的子资源写入 `Library/Artifacts`，运行时会根据图集中的 Sprite 引用自动选择对应纹理和 UV，因此同一图集、Material 与 Shader 的对象可以合批。

在 Project 中选中 `Showcase.atlas` 可以查看和编辑 Sources。添加或删除 Sprite 后重新 Build，即可更新图集 PNG SubAsset。

## 示例脚本

`Assets/Scripts/ShowcaseMotion.cs` 展示 `Start`、`Update`、公开 Inspector 字段、正弦位移和旋转。不同图标使用相同组件，只修改振幅、速度、相位和旋转速度。

`Assets/Scripts/PlayerMover.cs` 展示：

- `Input.GetKey` 读取 WASD 与方向键。
- 归一化斜向输入，保证各方向速度一致。
- 使用 `Time.deltaTime` 进行帧率无关移动。
- 通过 `moveBounds` 将 Player 保持在相机可见范围内。
- `Reset` 为组件提供稳定的默认值，并明确关闭 Edit Mode 执行。

脚本由 `Assets/Scripts/Game.asmdef.yaml` 归入 `Game` 程序集。编辑器扩展示例位于 `Assets/Editor/ProjectMenus.cs`，会在 `Tools/Showcase` 下增加一个带验证方法的菜单项。

## AssetBundle 热更新演示

`Assets/Resources/HotUpdate` 与 `Assets/Scripts/HotUpdateShowcase.cs` 是实际参与构建的游戏热更新内容。V1 使用蓝色 `REMOTE V1` 图片、`RESOURCE-V1` 文本、`SHADER-V1` Shader 与 `REMOTE-CODE-V1` C#；V2 使用绿色 `REMOTE V2` 图片以及对应的 V2 文本、Shader 和 C# 标记。Player 主包不会包含任一版本的游戏资源。

两套模板保存在 `Assets/Editor/HotUpdateDemo/V1` 与 `V2`，因此不会进入 Player。以下命令会切换四个 active Asset，不会修改 `.meta` 或场景引用：

```powershell
./Apply-HotUpdateProfile.ps1 -Profile V1
./Apply-HotUpdateProfile.ps1 -Profile V2
```

Player Settings 的 `Hot Resource Version` 是远端不可变版本标签，默认值为 `v1`。标签必须是小写 `vN`，其中 `N` 是无前导零的正整数；`v1`、`v2`、`v10` 有效，`V1`、`v01`、`v1.0` 无效。同一标签只能重复发布完全相同的内容；内容发生变化必须使用更高版本。只有数值更高的版本会推进 `latest.json`，所以在 `v2` 之后重新发布相同 `v1` 不会回退线上版本。最终目录合并和 `latest` 替换由跨进程发布锁串行化。命令行构建可用最后一个参数覆盖设置：

```powershell
BEngine.Editor.exe --build-content-update <project> <Build/hotres> windows v1
BEngine.Editor.exe --build-content-update <project> <Build/hotres> windows v2
```

线上版本由远端顶层 `latest.json` 决定，不受客户端当前版本高低限制。需要快速切回已经发布的 V1 时，不要重新构建或覆盖 `v1`、`v2` 目录，只修改文件中唯一的 `version` 字段：

```json
{"version":"v1"}
```

Windows 端到端验收使用本机安装的 Rejetto HFS，不由脚本临时实现 HTTP 服务。先启动 `E:\Tools\Hfs\hfs.exe`，确认端口为 `8080`；在 HFS 的 Virtual File System 中添加目录 `E:\Project\_BZP\BEngine\Build\hotres\bengine-2d-showcase`，选择 **Real folder**，发布名称保持为 `bengine-2d-showcase`。最终 URL 必须能访问 `http://127.0.0.1:8080/bengine-2d-showcase/latest.json`。

验收脚本每次都先精确清空仓库根目录的 `Build`。它先构建只含 AOT Scene、AOT 程序集及其依赖的 Player；AOT 内容位于 `<product>_data/resources/aot.bresources`，启动元数据、平台清单、闪屏和 Core 资源位于同级 `player.bresources`，两者都不是 AssetBundle。`_data` 只包含 `assembly` 与 `resources`，不再存在 `res`；两个归档的索引和 payload 都经过确定性混淆及 SHA-256 校验，因此目录和归档二进制中没有这些资源的直接明文。该处理用于发布混淆而非外部密钥加密。主包递归包含零个 `.bassetbundle`，也没有 V1/V2 游戏内容，`Build` 下所有成品文件和子目录名均为小写。

随后脚本在同一份 Player 副本与同一个 `sandbox` 上自动执行五个真实窗口流程：

1. 发布 V1；空沙盒检查并确认更新，通过 HFS 下载 V1 AB，注入 V1 C#，进入游戏并显示 V1。
2. 发布 V2；已安装 V1 的使用者检查后选择暂不更新，继续进入游戏，证明 active/catalog/C#/资源/Shader 仍全部来自 V1，且没有下载或激活 V2 bundle。
3. 再次检查并确认 V2，下载差异 bundle，激活 V2 C#、资源和 Shader，然后进入游戏并显示 V2。
4. 仅把远端 `latest.json` 中唯一的 `version` 从 `v2` 改为 `v1`；已安装 V2 的使用者选择暂不切换，继续进入游戏并显示完整 V2，且没有任何 bundle HTTP 请求。
5. 再次检查并确认远端 V1；客户端直接复用缓存的 V1 bundle，以零 bundle、零字节下载切回 V1，最终 `active=v1`、`previous=v2`。

验收结束后，`Build/hotres/bengine-2d-showcase/v1` 与 `v2` 都会保留，两个不可变目录在指针切换前后的逐文件哈希、创建时间与修改时间必须完全一致，`latest.json` 指向 `v1`。客户端每次仍会依次获取远端版本指针、`v1/version.json` 和目标 catalog；缓存完整时不会请求 bundle payload。

主包没有游戏资源。首次安装时，如果远端不可用、使用者拒绝必需更新，或沙盒内容缺少主场景/资源/Shader/C# 任一部分，`Enter Game` 都不会出现。只有沙盒已有完整有效版本时，更新确认框才允许选择暂不更新；`FallbackToLastValid` 也只允许使用已经完整校验并成功启动过的远端版本。

```powershell
./Validate-WindowsHotUpdate.ps1
```

成功时会输出 `BENGINE_WINDOWS_HOTUPDATE_OK`。验收在 `Temp/WindowsHotUpdateValidation/player-runtime` 中运行 Player 副本，因此 `Build/bengine 2d showcase` 始终保持可交付的首次安装状态。五个阶段分别生成 `game-v1.proof`、`game-v1-after-decline-v2.proof`、`game-v2.proof`、`game-v2-after-decline-v1.proof` 与 `game-v1-after-latest-rollback.proof`；Player 启动日志、V1/V2/回切 V1 的 HFS 预检证据、构建日志和 `manual-latest-v1.log` 位于同目录的 `logs`。脚本会核对每次实际下载的 bundle 数量与字节数；零下载回切必须完全没有 Bundle HTTP 事件，并逐个验证沙盒对象与 HFS 源文件哈希一致。任一步骤失败都会写入 `logs/validation-failure.log`。验收还会拒绝 Player 内的 `.bassetbundle`、`res`、松散资源/元数据/源码，并扫描两个 `.bresources`，拒绝资源地址、场景 YAML、Shader 源码、PNG 签名与运行时元数据标识的明文泄漏。

Player 不会改写可执行文件或 `<product>_data`。默认沙盒位于成品根目录的 `sandbox`，稳定内容为 `objects`、`catalogs`、`active.json`、可回滚时的 `previous.json` 以及 `logs`；`pending.json` 只在新版本尚未完成首次启动时短暂存在。沙盒不会生成 `AssetBundles/<package>` 包装层，也不会包含 AOT、BuiltIn 或 `_aot_builtin`。默认日志为 `sandbox/logs/player.log`，缓存根目录可在 Player Settings 自定义。

## 推荐练习

1. 修改 `ShowcaseMotion` 的公开字段，观察 Inspector 和 Play 镜像中的变化。
2. 将一个图标切换到其他 Sorting Layer，再调整 Camera 的 Culling Mask。
3. 修改 SpriteRenderer 的 Order in Layer，观察卡片背景与图标的前后关系。
4. 在 `Showcase.atlas` 的 Sources 中暂时移除一个 Sprite 并重新 Build，观察它从图集合批退回原始 Texture。
5. 在 Play 中移动或修改对象，然后停止 Play，确认场景文件和组件原值没有变化。

## 启动入口

从已导出引擎启动时运行 `Output/BEngine.bat`，然后在 Hub 中选择 Example。也可以直接运行：

```text
Output/BEgine/BEngine.Editor.exe Example/Project.yaml
```
