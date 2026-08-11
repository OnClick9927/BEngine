# BEngine Codex 编辑器集成

BEngine 通过 Codex CLI 的 `app-server` 协议接入 Codex。编辑器不会读取、保存或复制 OpenAI API Key；认证和令牌刷新由本机 Codex CLI 管理。

## 打开窗口

1. 安装 Codex CLI，并确保终端可以执行 `codex`。
2. 用 `BEngine.bat` 打开工程。
3. 在编辑器中选择 `Window > Codex`。
4. 状态显示需要登录时，点击 `Sign in`，在浏览器中完成 ChatGPT 登录。

窗口支持流式回答、命令与文件修改任务、停止当前 Turn、新建会话、工程资源上下文和审批操作。选中 Project 资源后点击 `+ Selection`，该资源路径会作为下一条消息的工程上下文发送给 Codex。

## 工程文件

- `Packages/manifest.yaml`：`com.bengine.codex` 包的启用状态。
- `ProjectSettings/CodexSettings.yaml`：CLI 路径、模型、推理强度、审批策略和网络权限。
- `Library/Codex/Session.yaml`：当前 Codex Thread ID 和窗口消息缓存。

这些文件全部位于当前 BEngine 工程目录。会话缓存不包含认证密钥；Codex 自己的认证数据仍由 Codex CLI 管理。

如果 `codex` 不在 `PATH` 中，在 Codex 窗口的 `Settings` 中填写本机 `codex.exe` 的完整路径，然后点击 `Save and Restart`。也可以设置环境变量 `BENGINE_CODEX_PATH`。

要停用集成，将 `Packages/manifest.yaml` 中 `com.bengine.codex` 的 `enabled` 改为 `false`，重新打开工程后 `Window > Codex` 将不可用。
