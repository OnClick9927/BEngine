# BEngine 官网 / Website

官网为纯静态站点，包含引擎介绍、基础入门、扩展包简介、创作展示、Hub 下载与开源说明。所有网页内容位于 `Pages`。支持完整中文/英文、浅色/深色；浅色保留品牌风格，深色采用与引擎编辑器一致的中性灰与蓝色交互，无需 npm 或外部字体。

全站导航提供 GitHub 仓库与「加入我们」入口，后者打开 `https://qm.qq.com/q/3mZda9LeRa`。首页、获取引擎页、关于页以及移动导航和页脚提供 main 分支 `src` 下载。外部入口在新页签打开；源码 ZIP 直接从官网同域下载，支持中文/英文与深浅主题。

The public website is a static introduction to BEngine: basic setup, package summaries, showcase, Hub download, and open-source information. All website content lives in `Pages`. It supports Chinese/English and light/dark themes without npm or external fonts. The dark theme follows the editor’s neutral grays and blue interactions; the light theme retains the existing brand colors.

## 文档与包目录 / Documentation and packages

- **官网只保留基础。** API、详细工作流与包手册由 Hub 根据本地安装的引擎和包版本提供。从 Hub 的「引擎与包」使用文档入口，不再复制或导出详细手册到官网。
- `manual.html` 为旧书签提供基础入口说明，不读取旧 product/version/doc 查询参数。
- `data/packages.json` 为静态功能简介，当前包含 39 个扩展包，不包含 StandardAsset 项目模板。卡片名称固定使用 `nameEn` 中的包原名，不随网站语言切换；`name` 保留为中文搜索别名。每项还包含中英简短介绍与非空 `categories` 数组；`categories` 仅为快速筛选元数据，允许一个包归属多个相关分类。网页卡片仍只显示名称和介绍，不展示版本、ID、依赖图、分类标签或文档按钮。
- 本地 Hub 文档门户的安装版本扫描与版本切换属于 Hub，与此静态简介目录分开维护。

Detailed APIs, workflows, and package manuals are supplied by Hub for locally installed versions. The website does not export or mirror manuals. `manual.html` explains the local entry point for old bookmarks. The public catalog contains original package names in `nameEn`, Chinese search aliases in `name`, bilingual summaries, and a nonempty `categories` array for each of the 39 extension packages. Cards always use the original name regardless of website language. Categories are filter metadata and may overlap; cards still show only the name and summary. Installation and version selection remain in Hub.

分类使用以下稳定 ID；中英显示名称由页面本地化，不将显示名称写入数组：

Use these stable category IDs. Display labels are localized by the page and are not stored in the arrays:

| ID | 中文 | English |
| --- | --- | --- |
| `graphics` | 画面与美术 | Graphics & art |
| `animation` | 动画与演出 | Animation |
| `physics` | 物理与导航 | Physics & navigation |
| `ui` | 界面与输入 | UI & input |
| `media` | 音频与视频 | Audio & video |
| `assets` | 资源与发布 | Assets & builds |
| `development` | 开发工具 | Development |

扩展包页提供“全部”与上述分类页签，可和关键词联合筛选。页签数字表示当前关键词在各分类中的匹配数量，跨分类包会分别计数。切换分类、语言或主题会保留搜索词；键盘左右方向键、Home、End 可切换页签，窄屏自动换行。

The package page combines category tabs with keyword search. Tab counts reflect matches for the current keyword; a package in multiple categories is counted in each. Changing category, language, or theme keeps the keyword. Arrow keys, Home, and End switch tabs; tabs wrap on narrow screens.

## 创作展示 / Playable showcase

`examples.html` 提供 Snake 与 Example 两个网页版入口，点击在新页签打开。成品随官网同域发布，分别位于 `plays/snake/` 与 `plays/example/`，使用相对路径兼容 `/BEngine/` 项目站点前缀。两者都是单机内容，Web 通过 HTTP 直接读取各自的 `StreamingAssets`，不依赖本地 HFS 或外部热更服务器，也不将 AB 复制到持久化沙盒。

`examples.html` opens Snake and Example in separate tabs. Production players are hosted on the same site at `plays/snake/` and `plays/example/`, with relative URLs supporting the `/BEngine/` project-site prefix. Both use bundled single-player content, reading StreamingAssets directly over HTTP without copying AssetBundles to persistent storage or depending on HFS or an external update server.

更新作品时，先使用引擎构建正式 Web 成品，再将完整输出放入 `Pages/plays/` 中对应目录。每个目录只包含 `index.html`、`player.bin`、`framework.bin`、`resources/`、`StreamingAssets/`。保留 PlayerBuild 生成的内容哈希和入口完整性信息；不要单独替换入口、混入旧版文件或放入验证补丁。验证、截图和回滚备份仍统一放在外层 `Temp`。

Update a playable project by building it with the engine and replacing its complete directory below `Pages/plays/`. Keep the generated content hashes and entry integrity metadata intact. Do not mix versions or include verification patches; keep screenshots, verification files, and backups in the outer `Temp` directory.

## 本地预览 / Local preview

在引擎仓库根执行，需要 Python 3.10+ 和 Git。输出目录必须不存在，并位于 `Pages` 外：

Run from the engine repository root with Python 3.10+ and Git. Choose a new output directory outside `Pages`:

```powershell
python Pages/tools/build-site.py --output ..\Temp\Pages-preview
python -m http.server 4173 --bind 127.0.0.1 --directory ..\Temp\Pages-preview
```

访问 `http://127.0.0.1:4173/`，按 Ctrl+C 停止服务。验证文件、截图和报告统一保存在外层 `Temp`。所有站内引用使用相对路径，兼容 GitHub 项目站点的 `/BEngine/` 前缀。

Open `http://127.0.0.1:4173/`. Stop with Ctrl+C. Keep verification files, screenshots, and reports under the outer `Temp` directory. Relative links support the `/BEngine/` GitHub project-site prefix.

## 发布 / Deployment

GitHub Actions 工作流位于 `.github/workflows/pages.yml`。它运行 `tools/build-site.py`，复制官网和两个 Web 成品至临时发布目录，并将当前 `Engine/BEngine.Hub.exe` 放入 `downloads/BEngine.Hub.exe`。网页不保存 Hub 副本，发布时始终使用当前提交中的发行文件。

同一次构建生成 `downloads/BEngine-main-src.zip`，包含该 main 提交中受 Git 跟踪的 `src/` 与根 `LICENSE`，不包含完整发行目录、Git 历史或本机生成文件。`src/**` 变化也会触发官网部署，因此源码下载会随 main 更新。ZIP 在构建时生成，不把副本提交到 `Pages`。

The GitHub Actions workflow in `.github/workflows/pages.yml` stages the website and both Web players, then copies the current exported `Engine/BEngine.Hub.exe` to `downloads/BEngine.Hub.exe`. The website source does not contain a duplicate Hub binary.

构建器要求 Hub 文件存在且有 Windows EXE 文件头，并验证两个 Web 成品的目录结构、入口和运行时完整性、资源引用以及 HTTP StreamingAssets 模式；若成品不完整、包含临时数据，或 `documentation/`、`data/documentation.json` 重新出现则拒绝构建。维护脚本与 README 不进入发布目录。Hub 下载需通过构建后的站点验证，直接用静态服务器打开 `Pages` 时下载文件尚不存在。

The builder checks the Hub executable, both Web players' layout and integrity metadata, resource references, and direct HTTP StreamingAssets mode. Incomplete players, temporary data, and obsolete detailed-manual mirrors are rejected. Maintenance tools and README files are excluded from deployment. Test the Hub download against staged output; directly serving `Pages` does not provide the generated download file.

仓库 Settings → Pages → Source 应为 GitHub Actions。`main` 上的 `Pages/**`、`src/**`、根 `LICENSE`、`Engine/BEngine.Hub.exe` 或工作流变更会触发部署；也可手动运行。公开地址预计为 `https://onclick9927.github.io/BEngine/`，是否上线以远端工作流成功结果为准。本地修改不会自动发布。

Set repository Settings → Pages → Source to GitHub Actions. Changes to `Pages/**`, the exported Hub, or the workflow on `main` trigger deployment; manual dispatch is also available. The expected URL is `https://onclick9927.github.io/BEngine/`. Publishing is confirmed by a successful remote workflow, not by local edits.

## 维护 / Maintenance

- HTML 中每段翻译使用 `data-zh` / `data-en`，`site.js` 同步标题、描述、图片替代文字、ARIA、导航与动态状态。
- `preferences.js` 在 CSS 前同步读取 `bengine-language=zh|en`、`bengine-theme=light|dark`，首访跟随浏览器语言与系统主题，显式选择持久化到 localStorage。
- 颜色 token 与响应布局在 `assets/site.css`。图片保持原样，不随主题反色。
- 不把测试、截图、缓存、详细文档镜像或展开的引擎源码放入网站发布目录；源码仅通过构建生成的 ZIP 提供下载。

Translations use `data-zh` / `data-en` throughout pages, metadata, accessible names, and dynamic states. `preferences.js` applies stored language and theme before CSS loads; first visits follow browser language and system theme. Theme tokens and responsive layouts live in `assets/site.css`. Images keep their original colors. Keep tests, caches, unpacked engine source, and detailed manuals out of the deployed site. The generated source ZIP is the only source download artifact. GitHub and Join us links are available across the site; the source ZIP is served directly from this website.
