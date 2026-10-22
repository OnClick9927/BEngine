---
name: bengine-property-attributes-authoring
description: "Property Attributes 资源与场景配置。用于 ExtendedPropertyAttribute, ExtendedPropertyDrawer, ShowIf, Required, ValidateInput, EnumFlags, Prefix, Suffix, HorizontalLine, InlineButton 的场景创建、Inspector 配置、资源导入与示例修改。"
---

# Property Attributes：资源与场景配置

## 进入任务

先读 [运行与扩展指南](../../Doc/RuntimeGuide.md)，再按需要读 [包手册](../../Readme.md) 与 Doc/index.html。检查 `com.bengine.property-attributes` 已在 Package Manager 启用；从 Project 确认目标资源存在，不能仅根据类名猜测完整 Unity 行为。

当前约束：单线程运行时、Fix64 二维逻辑、通过平台服务进行 IO。相关组件：ExtendedPropertyAttribute, ExtendedPropertyDrawer, ShowIf, Required, ValidateInput, EnumFlags, Prefix, Suffix, HorizontalLine, InlineButton。包源代码在本包 `BEngine.PropertyAttributes`，编辑器扩展在 `BEngine.Editor`。

## 操作流程

1. 创建可序列化 MonoBehaviour 字段或属性，先使用 Range 与 Required，在 Inspector 确认 SerializedProperty 能找到成员。
2. 为 ShowIf 或 ValidateInput 指定对象上真实存在的条件成员/方法；分别验证条件通过与失败时的布局。
3. 逐步组合 Title、InfoBox、Prefix/Suffix、InlineButton，调整 order；窄 Inspector 中确认输入框和提示不重叠。
4. 在 Hierarchy 选择相关对象，在 Inspector 核实序列化值；对照 Scene 与 Game 的实际结果，不依赖静态截图推断行为。
5. 点击 Play 完成预期输入，检查 Console 第一条异常；Stop 后再次运行，检查状态、订阅和资源是否清理。
6. 在 Export/Build 产物中重复关键步骤，记录包版本、资源目录版本、平台和结果。

## 诊断依据

- 字段消失：检查 ShowIf 条件名和返回值；不存在的条件会判为 false。
- 提示盖住下一项：使用 EditorGUI.GetPropertyHeight 分配 Rect，不能固定写死单行高度。
- 编辑后无法撤销：通过 SerializedProperty 写入并 ApplyModifiedProperties；直接修改目标前调用 Undo.RecordObject。

不要通过每帧重新读取文件来解决缓存问题。检查 BAsset 的资源代次、实际引用与更新激活时机；网络操作的完成必须由主线程 Poll 进入运行时。

## 验收与交付

- 切换条件使错误提示出现/消失，验证高度与实际绘制行数一致。
- 在绘制失败或结束后确认 GUI.enabled 和 EditorGUI.indentLevel 恢复，避免污染后续字段。
- 用 Flags 枚举选择多个位，再撤销；InlineButton 修改字段后撤销并确认场景 dirty 状态。

提交可复现的场景路径、最小代码、执行过的命令及观察结果。资源必须可经 Package Manager 导入；保留 GUID、asmdef 和 res 目录，示例说明应指向真实场景。

## 已知边界

这是 BEngine IMGUI PropertyDrawer 扩展，不是完整 Odin Inspector。条件和验证方法应快速且无副作用，因为布局和重绘都可能求值。运行时代码不能引用 Editor 装配；自定义 drawer 放在 Editor 目录和编辑器 asmdef 中。不同属性的约束需要保持上下界一致。
