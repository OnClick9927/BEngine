# 官网构建与文档导出

## 构建可发布站点

在仓库根目录执行，需要 Python 3.10+：

```powershell
python Pages/tools/build-site.py --output ..\Temp\Pages-preview
```

输出目录必须尚不存在且位于 `Pages` 外。构建器复制网页、资源与版本文档，并把当前发行的 `Engine/BEngine.Hub.exe` 复制到输出的 `downloads/BEngine.Hub.exe`；维护脚本和本说明不进入站点。Hub 缺失或不是 Windows 可执行文件时，构建失败，不发布失效的下载入口。构建日志记录下载文件的字节数和 SHA-256。

GitHub Pages 工作流使用同一脚本，在临时目录准备网站后上传该输出。更新发行 Hub 会触发官网重新部署；不要把可执行文件复制进 `Pages` 源码。

## 导出版本文档

在仓库根目录执行，需要 Python 3.10+ 和 Node.js 18+：

```powershell
python Pages/tools/export-docs.py
```

脚本读取 `Engine/BEgine/BEgine-版本/Editor` 和 `Engine/Packages/包名/包名-版本/Editor` 中真实存在的发行文档，更新 `Pages/data/documentation.json` 与 `Pages/documentation/产品ID/版本/`。每个版本的 `overview` 来自完整 `Editor/Readme.md`，其他 Markdown/HTML 页面及图片一起收录；当前引擎版本补充 `src/Platform` 的平台说明。

普通执行刷新每个产品当前最新的版本，并保留已生成的历史版本、版本列表和图片。发布新版本时，先生成版本化的引擎或包发行目录，再运行脚本；不要把旧手册复制成不存在的历史版本。只有确实需要修订旧版手册时，才使用 `--refresh-history`。平台源码补充文档只应在对应引擎版本发布时导出。

网页读取器入口为 `manual.html?product=产品ID&version=版本&doc=页面ID`。引擎产品 ID 为 `engine`，其他产品使用包的真实 `package.yaml` ID。未指定页面时使用 `overview`。页面清单位于 `documentation.json`；站内互链保留产品、版本、页面和锚点，显式引用旧版发行目录时保持引用的旧版本。本项目源码、示例和许可链接分别引导到官网的相关介绍；第三方 GitHub 参考保留为不可点击文本，悬停提示原始参考地址。

输出为安全 HTML 片段，移除文档自带的脚本、样式、导航、事件属性和不可用的路径，保留标题、表格、代码、图片与正文。产品 ID、SemVer 版本和现有页面路径经过校验，输出限制在 `Pages/documentation` 内。图片和页面都随文档版本保存。没有额外 npm 或 pip 安装步骤；Markdown 渲染使用随此目录提供的 Marked 17.0.5，许可证见 [vendor/marked.LICENSE.md](vendor/marked.LICENSE.md)。浏览器运行官网不依赖 Python、Node.js 或 Marked。

验证脚本、预览日志和检查报告统一放在仓库外的 `BEngine/Temp` 下。
