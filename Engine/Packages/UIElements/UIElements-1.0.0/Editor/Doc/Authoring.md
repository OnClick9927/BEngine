# UIElements 编辑工具

启用 `com.bengine.ui-elements` 即可使用本页工具，编辑器程序集为 `BEngine.UIElements.Editor`。
UI Builder 与 HTML Converter 已并入本包，不再单独安装或启用。

## UI Builder

打开 `Window/UIElement/UI Builder`，或在 Project 中双击 `.bxml`。
通过 Library 添加控件，在 Hierarchy 选择元素，用 Inspector 编辑布局、样式、文本和绑定。
工具栏提供 New、Open、Save、Undo/Redo、Duplicate 及 HTML Converter 入口。
界面保存为 `.bxml`，样式使用 `.bss` 或 `.bcss`。

`Assets/Create/UI Toolkit/UI Document` 和 `Style Sheet` 可创建初始文件。
运行时向 GameObject 添加 UIDocument，指定 Visual Tree Asset，并配置缩放和输入。

## HTML/CSS Converter

打开 `Window/UIElement/HTML Converter`，选择源 HTML 和目标 `.bxml`，点击 Convert。
转换器生成界面、样式与诊断报告；请检查报告中的不支持项，并在 UI Builder 中预览结果。
转换用于创建引擎 UI，不会嵌入浏览器或执行网页脚本。

命令行入口保持为：

```text
BEngine.Editor.exe --convert-html source.html destination.bxml
```

AngleSharp 与 AngleSharp.Css 仅属于编辑器依赖，不加入 UIElements 运行时程序集。

## 字体与示例

在 `Window/UIElement/SDF Font` 制作字体；详见 [SDF Font](SdfAndTouch.md)。
在 Package Manager 的 UIElements → Examples 中可导入 UI Builder Getting Started、
HtmlUIConverter Getting Started、Runtime HUD 和 Controls Gallery。
Controls Gallery 还需要 Reactive；其余依赖由工程清单与包管理器处理。

旧工程打开后，已启用的 UIBuilder 或 HtmlUIConverter 引用会合并为 UIElements 引用，
旧包缓存随之清理。工具的 C# 类型名称和命名空间保持不变，所属程序集改为 `BEngine.UIElements.Editor`。
