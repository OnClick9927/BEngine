# BEngine 官网

官网是纯静态站点，网页、样式、脚本、图片与版本文档全部位于此目录，不依赖 npm、编译步骤或运行时环境变量。GitHub Actions 仅上传 `Pages` 目录，不发布引擎源码和发行包。

## 本地预览

在仓库根目录 `E:\Project\_BZP\BEngine\Engine` 中执行以下命令，需要 Python 3：

```powershell
python -m http.server 4173 --bind 127.0.0.1 --directory Pages
```

打开 <http://127.0.0.1:4173/>。按 `Ctrl+C` 停止服务。页面资源和站内链接使用相对路径，同样兼容 GitHub 项目站点的 `/BEngine/` 前缀。

## GitHub Pages 发布

1. 将 `Pages` 与 `.github/workflows/pages.yml` 提交并推送到仓库 `main` 分支。
2. 在 [仓库设置](https://github.com/OnClick9927/BEngine/settings/pages) 中进入 **Settings → Pages → Build and deployment**，把 **Source** 设置为 **GitHub Actions**。
3. 在 **Actions → Deploy BEngine website** 中查看部署；如有需要，选择 `main` 分支手动运行 **Run workflow**。
4. 部署成功后，预期访问地址为 <https://onclick9927.github.io/BEngine/>。实际发布地址以工作流 `github-pages` 环境显示的链接为准。

上述配置文件已准备在仓库中；是否上线以远端工作流成功结果为准。本地修改不会自动发布。

只有 `main` 分支中 `Pages/**` 或该工作流的修改会自动触发发布；其他引擎修改不会触发。手动运行也只允许 `main` 分支部署。工作流使用 GitHub 官方 Pages Actions，发布权限仅授予部署 job，不需要手动配置访问令牌。

## 维护

- 直接编辑此目录中的 HTML、CSS、JavaScript 和图片，保持所有网页内容位于 `Pages` 下。
- 站内引用使用 `./`、相对文件名或锚点；避免 `/assets/...` 这类从域名根目录开始的路径，以兼容 `/BEngine/` 项目站点。
- 新增页面时同步导航与相关链接；发布前检查桌面、手机宽度、菜单、按钮和本地资源加载。
- 文档、包、示例、许可和项目说明在官网内阅读；仅下载和必要的第三方工具入口使用外部链接。
- 下载和文档入口应指向真实可用的发布物或文档；不要用尚未发布的版本代替已发布内容。
- 浏览器截图、测试脚本、临时服务文件、测试报告和缓存统一放在 `E:\Project\_BZP\BEngine\Temp`，不要放入 `Pages` 或仓库其他目录。

## 按版本维护文档

`manual.html` 提供引擎和包的站内阅读器。`data/documentation.json` 记录真实产品、版本与章节，`documentation/产品ID/版本/` 保存对应版本的正文和图片。入口链接包含明确版本，收藏和互链不会随默认版本更新而指向其他版本。

发布引擎或包的新版本后，在仓库根目录执行，需要 Python 3.10+ 和 Node.js 18+：

```powershell
python Pages/tools/export-docs.py
```

导出器从真实发行目录收录手册，保留既有历史版本；默认只刷新每个产品当前最新版本。修订旧版本文档使用 `--refresh-history`。不要创建不存在的历史版本，也不要把新版本手册覆盖到旧版本目录。详情见 [文档导出说明](tools/README.md)。网页本身不需要 Python、Node.js 或构建步骤。

新增版本时，同步 `data/packages.json` 的包版本和 `docsUrl`，以及首页、下载页等明确标注版本的入口。版本文档、目录清单和入口应一起提交发布。

部署规则参考 [GitHub 官方自定义 Pages 工作流文档](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages) 和 [发布来源配置文档](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site)。
