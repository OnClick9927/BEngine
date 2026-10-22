# Localization

独立游戏本地化包 `com.bengine.localization`。行为参考 [WooLocalization](https://github.com/OnClick9927/WooLocalization)：Key 查询、文本和对象资源、组件属性绑定、语言分表与按需加载、偏好记录、CSV 导入导出和 C# Key 生成。使用 BEngine 原生资产和主线程异步，不依赖 Unity。

## 制作

1. Package Manager 启用 Localization。
2. Assets/Create/Localization/Table 创建 `.asset.yaml`；每个语言一个表。
3. 填写 language（例如 `zh-Hans`、`en`）和 entries。Key 区分大小写；空文本是有效译文，null 表示没有文本。asset 接受 Sprite、Texture、Prefab、音频等 BObject 引用。
4. Window/Localization/Tables 支持 Key/译文搜索、独立预览、CSV/Excel 导入导出、生成 C# 常量。选中多个表后 Validate Table 会检查缺失 Key、全部格式分支和跨语言参数差异。表使用普通 Inspector 编辑，支持 Undo、Save 和 Play 写保护。CSV 列为 `key,language,text,comment`，可用 Excel 编辑；CSV 不搬运对象引用，导入同 Key 文本保留原资源。审阅表提供了非 null 资产时，合并会替换资产并支持 Undo。

## 运行

```csharp
using BEngine.Localization;

using var localization = new LocalizationContext("en");
localization.MergeContext(englishTable);
localization.MergeContext(chineseTable);
await localization.SetLanguageAsync("zh-Hans");
using var binding = localization.BindText("menu.play", value => label.text = value);
using var spriteBinding = localization.BindAsset<Sprite>("logo", value => renderer.sprite = value);
using var propertyBinding = localization.BindProperty("menu.play", myComponent, "caption");
```

场景可添加 LocalizationController 和 LocalizedProperty，后者在启用时绑定、禁用/销毁时释放。Controller 的内联 tables 用于常驻小表；大项目使用 AssetLocalizationTableProvider.SetPaths 或实现 ILocalizationTableProvider，按语言加载 AssetBundle 中的表。路径动态寻址的表须由项目明确纳入资源构建。上下文不持有全局单例，不销毁借用的表或资源；自定义 Provider 负责自己的 Bundle handle 生命周期。

回退顺序为请求语言、逐级父语言、默认语言及其父语言。上下文复制表条目以隔离编辑，后合并的同 Key 覆盖先前条目。切换时先完成全部加载、校验和取消检查，再更新状态。并发切换以最后一次请求为准，旧请求返回 false。失败保持原语言，Dispose 阻止未完成操作提交。RemoveLanguage 后下次切换可重新加载该语言。

ILocalizationPreferenceRecorder 支持自定义记录器，PlayerPrefsLocalizationRecorder 可持久保存；编辑器预览不设置记录器。绑定必须 Dispose，动态参数变化后调用 binding.Refresh()。

## 格式化

支持 `{player}`、`{score:N0}` 和 `{count:plural:=0{No apples}one{# apple}other{# apples}}`，以及 ordinal、select、if、list；分支可嵌套占位符，`{{`/`}}` 输出花括号。分支格式必须提供 other。数量规则来自 Unicode CLDR 48，包含区域回退和可见小数；1120 个规则样例通过验收。直接调用 Format 未提供 culture 时，数值使用 InvariantCulture，复数使用英文规则；LocalizationContext 使用实际译文语言。非法格式/缺少参数会抛出 FormatException。未翻译 Key 返回 Key 并触发 missingKey。

`LocalizedTextFormatter.GetArgumentNames(text)` 不执行参数，递归校验所有分支并返回去重、排序后的参数名；可用于翻译审阅和 CI。`LocalizationAuthoring.FindFormatIssues(tables)` 返回格式错误及同 Key 跨语言参数差异。运行格式化只展开选中的分支，编辑器校验用于提前发现未选分支中的错误。

开启 pseudoLocalization 扩展文本长度以发现布局问题。编辑器可生成安全转义的 C# 常量，规范化后重名会报错，避免静默覆盖。

## Excel 与翻译服务

Localization 窗口支持 `.xlsx` 导入/导出，列仍为 key、language、text、comment。读取第一个工作表，支持 Excel shared strings 与 inline strings、多行文本和 Unicode；资源引用保留在原表中。工作簿公式不会执行，包含公式的单元格要求先转为文本；不读取旧式 `.xls`。展开后的工作簿上限为 32 MiB。

实现 `ILocalizationTranslator.TranslateAsync`，再调用 `LocalizationTranslation.CreateMissingTranslationsAsync` 可为缺失文本生成独立审阅表。此操作支持取消、保留原表、不覆盖有意设置的空译文；审阅后调用 `LocalizationAuthoring.ApplyTranslations` 合并，支持 Undo。引擎没有预设外部翻译服务或密钥。

本地资产通过原生 ManagedAsset v2 对象图保存，能还原嵌套条目、定点数和实际资源类型；旧 v1 文件仍可读取。持久化资源引用须先保存被引用资源；运行时可以把任意 BObject 交给上下文，场景对象不适合作为跨场景资产表的持久引用。
