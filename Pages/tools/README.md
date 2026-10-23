# Website staging

`build-site.py` copies the static website to a new directory outside `Pages` and prepares two downloads:

- `downloads/BEngine.Hub.exe`: the current exported `Engine/BEngine.Hub.exe`.
- `downloads/BEngine-main-src.zip`: `src/` and the root `LICENSE` from the current Git commit, with executable modes and third-party notices preserved. Untracked files and local source edits are excluded. A commit containing tracked `bin/` or `obj/` output is rejected.

```powershell
python Pages/tools/build-site.py --output ..\Temp\Pages-preview
```

Python 3.10+ and Git are required. Run inside a Git checkout; deployment checks out `main`, so the ZIP contains the same main commit as the published website. The builder prints the source commit, file count, size, and both download SHA-256 hashes. It verifies the ZIP and publishes the output directory only after both downloads are ready. Failed builds remove their temporary staging data and never leave a partial source ZIP at its download URL.

The output directory must not already exist. Detailed documentation mirrors are rejected: versioned engine and package manuals are served by Hub from locally installed content.

此目录仅维护官网发布准备脚本，不再包含详细文档导出流程。源码 ZIP 来自同次部署的 main 提交，仅包含 `src/` 和根目录 `LICENSE`，不会包含未跟踪文件、本地未提交修改或构建输出；保留第三方声明与执行位。需要 Python 3.10+ 和 Git。输出和验证统一使用外层 `Temp`；Hub 版本手册由本地文档门户处理。
