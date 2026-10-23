# Website staging

`build-site.py` copies the static website to a new directory outside `Pages` and includes the current exported `Engine/BEngine.Hub.exe` as `downloads/BEngine.Hub.exe`.

```powershell
python Pages/tools/build-site.py --output ..\Temp\Pages-preview
```

Only Python 3.10+ is required. The output directory must not already exist. Detailed documentation mirrors are rejected: versioned engine and package manuals are served by Hub from locally installed content.

此目录仅维护官网发布准备脚本，不再包含详细文档导出流程。输出和验证统一使用外层 `Temp`；Hub 版本手册由本地文档门户处理。
