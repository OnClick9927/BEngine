# BEngine

开源 C# 2D 游戏引擎，基于 .NET 10。

[引擎官网](https://onclick9927.github.io/BEngine/) · [快速入门](https://onclick9927.github.io/BEngine/docs.html) · [扩展包](https://onclick9927.github.io/BEngine/packages.html)

官网源码与维护说明位于 [Pages](Pages/README.md)，由 GitHub Pages 发布。

引擎源码位于 `src/Core`、`src/Hub`、`src/Packages` 和 `src/Platform`，解决方案为 `src/BEngine.sln`。
共享源码、构建配置、开发工具与导出脚本统一位于 [Development](Development/README.md)。
在此目录运行 `dotnet build src/BEngine.sln -c Release` 构建引擎，运行
`./Development/Scripts/ExportEngine.ps1 -Configuration Release` 导出引擎。
