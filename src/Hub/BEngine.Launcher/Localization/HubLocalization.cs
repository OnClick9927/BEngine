using System.Globalization;

namespace BEngine.Launcher;

// This culture belongs to one Hub window. Never change the engine or process-wide culture.
internal sealed class HubLocalization
{
    internal CultureInfo Culture { get; }
    internal HubLocalization(string language, CultureInfo? systemCulture = null)
    {
        var name = language == "system" ? (systemCulture ?? CultureInfo.CurrentUICulture).Name : language;
        Culture = CultureInfo.GetCultureInfo(name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US");
    }

    internal string Text(string key, params object?[] arguments)
    {
        var template = Culture.Name == "zh-CN" && Chinese.TryGetValue(key, out var translated) ? translated : key;
        return arguments.Length == 0 ? template : string.Format(Culture, template, arguments);
    }

    internal string Option(string value) => value switch
    {
        "system" => Text("Follow system"), "zh-CN" => "简体中文", "en" => "English",
        "Engines" => Text("Engines & packages"),
        _ => Text(value)
    };

    private static readonly Dictionary<string, string> Chinese = new(StringComparer.Ordinal)
    {
        ["Projects"] = "工程", ["Engines"] = "引擎与包", ["Engines & packages"] = "引擎与包", ["Packages"] = "包", ["Settings"] = "设置",
        ["Project"] = "工程", ["Engine"] = "引擎", ["Package"] = "包", ["Version"] = "版本",
        ["Location"] = "位置", ["Last opened"] = "最近打开", ["Engine version"] = "引擎版本",
        ["Search projects"] = "搜索工程", ["Search engines"] = "搜索引擎", ["Search packages"] = "搜索包",
        ["Open"] = "打开", ["New project"] = "新建工程", ["Open selected"] = "打开所选工程",
        ["Remove from list"] = "从列表中移除", ["Show in Explorer"] = "在资源管理器中显示",
        ["Refresh"] = "刷新", ["Open Documentation"] = "打开文档", ["Browse..."] = "浏览…",
        ["Cancel"] = "取消", ["Create project"] = "创建工程", ["Project name"] = "工程名称",
        ["Package type"] = "包类型", ["All"] = "全部", ["package"] = "扩展包", ["project"] = "工程包",
        ["Details"] = "详情", ["Dependency graph"] = "依赖图", ["Package dependency graph"] = "包依赖图",
        ["Dependency kind"] = "依赖类型", ["All dependencies"] = "全部依赖",
        ["Runtime dependencies"] = "运行时依赖", ["Editor dependencies"] = "编辑器依赖",
        ["runtime"] = "运行时", ["editor"] = "编辑器", ["Optional"] = "可选",
        ["Fit"] = "适应", ["Fit graph"] = "适应依赖图",
        ["Zoom in"] = "放大", ["Zoom out"] = "缩小", ["Missing package"] = "缺失包",
        ["Package cycle"] = "包级循环依赖", ["Unavailable dependency"] = "依赖不可用",
        ["Selected package dependencies"] = "所选包的依赖信息",
        ["Depends on: {0}"] = "依赖：{0}", ["Used by: {0}"] = "被依赖：{0}",
        ["{0} packages · {1} dependency links"] = "{0} 个包 · {1} 条依赖连线",
        ["Blue: depends on · Orange: used by · Dashed: optional\nHover/click: highlight · Wheel: zoom · Drag: pan · Double-click: details"] =
            "蓝色：当前包依赖 · 橙色：依赖当前包 · 虚线：可选依赖\n悬停预览关系 · 点击固定 · 滚轮缩放 · 拖动平移 · 双击详情",
        ["Installed engines"] = "已安装引擎", ["Available packages"] = "可用包", ["Hub settings"] = "Hub 设置",
        ["Engine documentation"] = "引擎文档",
        ["Found {0} package(s) and {1} project package(s) for BEngine {2}."] = "已找到适用于 BEngine {2} 的 {0} 个扩展包和 {1} 个工程包。",
        ["No compatible packages for this engine version."] = "没有兼容此引擎版本的包。",
        ["Select an installed engine version."] = "请选择已安装的引擎版本。",
        ["Engine compatibility: {0}"] = "兼容引擎：{0}",
        ["Unknown"] = "未知", ["{0}  (Missing)"] = "{0}  （不存在）",
        ["No projects yet. Create a project or locate an existing one."] = "暂无工程。可以新建工程，或打开已有工程。",
        ["No projects match your search."] = "没有匹配的工程。",
        ["No packages match the current type and search"] = "没有符合当前类型和搜索条件的包",
        ["No engines match the current search"] = "没有匹配的引擎",
        ["Select an extension package to inspect its details."] = "选择一个扩展包以查看详情。",
        ["Select an installed engine to inspect its details."] = "选择一个已安装引擎以查看详情。",
        ["Create a clean BEngine project. Add extension packages later from Package Manager."] = "创建一个干净的 BEngine 工程。之后可通过包管理器添加扩展包。",
        ["New projects contain only BEngine Core. Package dependencies are resolved when a package is enabled."] = "新工程仅包含 BEngine Core。启用扩展包时会解析包依赖。",
        ["Personalize this Hub. These preferences are independent of engine and project settings."] = "自定义 Hub。这些偏好独立于引擎和工程设置。",
        ["Appearance"] = "外观", ["Language"] = "语言", ["Follow system"] = "跟随系统",
        ["Theme"] = "主题", ["Dark"] = "深色", ["Light"] = "浅色", ["Accent color"] = "强调色",
        ["Teal"] = "青绿", ["Blue"] = "蓝色", ["Purple"] = "紫色", ["Orange"] = "橙色", ["Rose"] = "玫红",
        ["Projects and startup"] = "工程与启动", ["Startup page"] = "启动页", ["Project order"] = "工程排序",
        ["Name A–Z"] = "名称升序", ["Name Z–A"] = "名称降序",
        ["Show project location column"] = "显示工程位置列",
        ["Minimize Hub after the editor is ready"] = "编辑器就绪后最小化 Hub",
        ["Default project folder"] = "默认工程目录",
        ["Use the most recent project location"] = "使用最近工程的位置",
        ["Leave blank to use the most recent project location, then Documents."] = "留空时使用最近工程的位置；无历史记录时使用“文档”目录。",
        ["Apply settings"] = "应用设置", ["Restore defaults"] = "恢复默认设置",
        ["Defaults selected. Apply settings to save them."] = "已填入默认值。点击“应用设置”保存。",
        ["Changes not applied. Choose Apply settings to save."] = "更改尚未应用。点击“应用设置”保存。",
        ["Saved. Language, appearance and project options are applied; startup page takes effect next time."] = "已保存。语言、外观和工程选项已应用；启动页将在下次启动时生效。",
        ["Choose the default folder for new projects"] = "选择新建工程的默认目录",
        ["Select a BEngine project folder"] = "选择 BEngine 工程目录",
        ["Select the folder that will contain the new project"] = "选择用于存放新工程的目录",
        ["Choose an absolute folder path, or leave the default project folder blank."] = "请选择绝对目录路径，或将默认工程目录留空。",
        ["The default project folder contains invalid characters."] = "默认工程目录包含无效字符。",
        ["The default project folder points to a file. Choose a folder instead."] = "默认工程目录指向了一个文件。请选择文件夹。",
        ["Could not save settings: {0}"] = "无法保存设置：{0}",
        ["Could not load project history: {0}"] = "无法读取工程历史：{0}",
        ["Could not read available packages: {0}"] = "无法读取可用包：{0}",
        ["Could not read installed engines: {0}"] = "无法读取已安装引擎：{0}",
        ["Installed engines: {0}"] = "已安装引擎目录：{0}",
        ["Found {0} package(s) and {1} project package(s)."] = "已找到 {0} 个扩展包和 {1} 个工程包。",
        ["Found {0} engine version(s)."] = "已找到 {0} 个引擎版本。",
        ["Version {0}"] = "版本 {0}", ["Location\n{0}"] = "位置\n{0}", ["Source\n{0}"] = "来源\n{0}",
        ["Type: {0}"] = "类型：{0}", ["Import destination: {0}"] = "导入位置：{0}",
        ["Runtime assembly: {0}"] = "运行时程序集：{0}", ["Editor assembly: {0}"] = "编辑器程序集：{0}",
        ["Examples ({0}): {1}"] = "示例（{0}）：{1}", ["Documentation: {0}"] = "文档：{0}",
        ["None"] = "无", ["Included — Editor/Doc/index.html"] = "已包含 — Editor/Doc/index.html", ["Not included"] = "未包含",
        ["No introduction is included with this engine version."] = "此引擎版本未包含介绍。",
        ["The introduction could not be read."] = "无法读取介绍。",
        ["Documentation is not available for '{0}'."] = "“{0}”未提供文档。",
        ["Opened documentation for '{0}'."] = "已打开“{0}”的文档。",
        ["Could not open documentation for '{0}': {1}"] = "无法打开“{0}”的文档：{1}",
        ["Starting '{0}'..."] = "正在启动“{0}”…", ["Opened '{0}'."] = "已打开“{0}”。",
        ["Removed '{0}' from the recent projects list. Files were not deleted."] = "已将“{0}”从最近工程列表移除，未删除文件。",
        ["Enter a project name."] = "请输入工程名称。", ["Choose a location for the new project."] = "请选择新工程的位置。",
        ["The target folder is not empty: {0}"] = "目标目录非空：{0}",
        ["Project path: {0}\nThe project will contain BEngine Core only."] = "工程路径：{0}\n此工程将仅包含 BEngine Core。",
        ["Project name contains characters that cannot be used in a folder name."] = "工程名称包含不能用于文件夹名称的字符。",
        ["The editor was not found at '{0}'. Check the installed engine version."] = "未找到编辑器“{0}”。请检查已安装的引擎版本。",
        ["BEngine Editor process could not be started."] = "无法启动 BEngine 编辑器进程。",
        ["BEngine Editor failed to start."] = "BEngine 编辑器启动失败。", ["Log:"] = "日志：",
        ["BEngine Hub failed to start."] = "BEngine Hub 启动失败。",
        ["The Editor reported a startup failure."] = "编辑器报告启动失败。",
        ["The Editor process exited before its first frame (exit code {0})."] = "编辑器在首帧显示前退出（退出代码 {0}）。",
        ["The Editor startup timed out. See the log for details."] = "编辑器启动超时。请查看日志了解详情。"
    };
}
