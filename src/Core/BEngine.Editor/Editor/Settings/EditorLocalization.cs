using System.Globalization;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

public static class EditorLocalization
{
    private static readonly Dictionary<string, (string Chinese, string English)> Strings = new(StringComparer.Ordinal)
    {
        ["Preferences"] = ("偏好设置", "Preferences"),
        ["Project Settings"] = ("项目设置", "Project Settings"),
        ["General"] = ("常规", "General"),
        ["External Tools"] = ("外部工具", "External Tools"),
        ["Player"] = ("播放器", "Player"),
        ["Graphics"] = ("图形", "Graphics"),
        ["Editor"] = ("编辑器", "Editor"),
        ["Search"] = ("搜索", "Search"),
        ["Language"] = ("语言", "Language"),
        ["Editor Scale"] = ("编辑器缩放", "Editor Scale"),
        ["Font"] = ("字体", "Font"),
        ["Font Size"] = ("字体大小", "Font Size"),
        ["Theme"] = ("编辑器主题", "Theme"),
        ["Auto Refresh Assets"] = ("自动刷新资源", "Auto Refresh Assets"),
        ["Show Meta Files"] = ("显示 Meta 文件", "Show Meta Files"),
        ["External Script Editor"] = ("外部脚本编辑器", "External Script Editor"),
        ["Company Name"] = ("公司名称", "Company Name"),
        ["Product Name"] = ("产品名称", "Product Name"),
        ["Default Width"] = ("默认宽度", "Default Width"),
        ["Default Height"] = ("默认高度", "Default Height"),
        ["Full Screen"] = ("全屏", "Full Screen"),
        ["Graphics Backend"] = ("图形后端", "Graphics Backend"),
        ["Reset"] = ("恢复默认", "Reset"),
        ["Package"] = ("扩展包", "Package"),
        ["Built-in font"] = ("文本由所选字体生成缓存字形，再由 GPU 合成；BEngine Built-in 会回退到系统 UI 字体。",
            "Text is cached from the selected font and composed by the GPU; BEngine Built-in falls back to the system UI font."),
        ["Restart renderer"] = ("图形后端会在下次启动编辑器或播放器时生效。",
            "The graphics backend takes effect after restarting the editor or player.")
    };

    public static string locale { get; private set; } = "zh-CN";
    public static event Action? localeChanged;
    public static string Tr(string key) => Strings.TryGetValue(key, out var value)
        ? locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? value.Chinese : value.English
        : key;

    internal static void SetLocale(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "zh-CN" : value;
        if (locale.Equals(value, StringComparison.OrdinalIgnoreCase)) return;
        locale = value;
        try
        {
            var culture = CultureInfo.GetCultureInfo(value);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException) { }
        EditorCallbackDispatcher.Invoke(localeChanged, nameof(localeChanged));
    }
}
