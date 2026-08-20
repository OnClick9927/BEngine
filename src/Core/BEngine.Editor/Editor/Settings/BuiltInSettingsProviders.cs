using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

internal static class BuiltInSettingsProviders
{
    private static readonly string[] Locales = ["中文 (简体)", "English"];
    private static readonly string[] LocaleValues = ["zh-CN", "en-US"];
    private static readonly string[] Themes = ["Dark", "Light", "Classic"];
    private static readonly string[] Fonts = ["BEngine Built-in", "Segoe UI", "Microsoft YaHei"];
    private static readonly string[] Backends = Enum.GetNames<GraphicsBackend>();
    private static string _scriptingSettingsPath = string.Empty;
    private static string _scriptingDefineSymbols = string.Empty;
    private static string _scriptingError = string.Empty;
    private static bool _scriptingDirty;

    [SettingsProvider]
    private static SettingsProvider GeneralPreferences() => new("Preferences/General", SettingsScope.User,
        ["scale", "font", "language", "locale", "theme", "缩放", "字体", "语言", "主题"])
    {
        label = EditorLocalization.Tr("General"),
        guiHandler = _ => DrawGeneralPreferences(),
        footerBarGuiHandler = () =>
        {
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            if (GUILayout.Button(EditorLocalization.Tr("Reset"), GUILayout.Width(120)))
                EditorPreferences.ResetToDefaults();
            GUILayout.EndHorizontal();
        }
    };

    [SettingsProvider]
    private static SettingsProvider ExternalToolsPreferences() => new("Preferences/External Tools",
        SettingsScope.User, ["script", "code", "IDE", "脚本", "代码"])
    {
        label = EditorLocalization.Tr("External Tools"),
        guiHandler = _ =>
        {
            var preferences = EditorPreferences.current;
            var value = EditorGUILayout.TextField(EditorLocalization.Tr("External Script Editor"),
                preferences.ExternalScriptEditor);
            if (value == preferences.ExternalScriptEditor) return;
            preferences.ExternalScriptEditor = value;
            EditorPreferences.Save();
        }
    };

    [SettingsProvider]
    private static SettingsProvider PlayerProjectSettings() => new("Project/Player", SettingsScope.Project,
        ["company", "product", "resolution", "公司", "产品", "分辨率"])
    {
        label = EditorLocalization.Tr("Player"),
        guiHandler = _ => DrawPlayerSettings()
    };

    [SettingsProvider]
    private static SettingsProvider GraphicsProjectSettings() => new("Project/Graphics", SettingsScope.Project,
        ["Vulkan", "OpenGL", "Direct3D", "WebGPU", "图形", "渲染"])
    {
        label = EditorLocalization.Tr("Graphics"),
        guiHandler = _ => DrawGraphicsSettings()
    };

    [SettingsProvider]
    private static SettingsProvider EditorProjectProvider() => new("Project/Editor", SettingsScope.Project,
        ["locale", "language", "本地化", "语言"])
    {
        label = EditorLocalization.Tr("Editor"),
        guiHandler = _ => DrawProjectEditorSettings()
    };

    [SettingsProvider]
    private static SettingsProvider ScriptingProjectSettings() => new("Project/Scripting", SettingsScope.Project,
        ["assembly", "define", "symbol", "script", "程序集", "宏", "脚本"])
    {
        label = EditorLocalization.Tr("Scripting"),
        guiHandler = _ => DrawScriptingSettings()
    };

    private static void DrawGeneralPreferences()
    {
        var value = EditorPreferences.current;
        var changed = false;
        var locale = Math.Max(0, Array.IndexOf(LocaleValues, value.Locale));
        var nextLocale = EditorGUILayout.Popup(EditorLocalization.Tr("Language"), locale, Locales);
        if (nextLocale != locale) { value.Locale = LocaleValues[nextLocale]; changed = true; }
        var scale = Math.Clamp(EditorGUILayout.FloatField(EditorLocalization.Tr("Editor Scale"), value.EditorScale),
            0.75f, 2f);
        if (Math.Abs(scale - value.EditorScale) > .001f) { value.EditorScale = scale; changed = true; }
        var font = Math.Max(0, Array.IndexOf(Fonts, value.EditorFont));
        var nextFont = EditorGUILayout.Popup(EditorLocalization.Tr("Font"), font, Fonts);
        if (nextFont != font) { value.EditorFont = Fonts[nextFont]; changed = true; }
        var fontSize = Math.Clamp(EditorGUILayout.IntField(EditorLocalization.Tr("Font Size"),
            value.EditorFontSize), 10, 24);
        if (fontSize != value.EditorFontSize) { value.EditorFontSize = fontSize; changed = true; }
        var theme = Math.Max(0, Array.IndexOf(Themes, value.EditorTheme));
        var nextTheme = EditorGUILayout.Popup(EditorLocalization.Tr("Theme"), theme, Themes);
        if (nextTheme != theme) { value.EditorTheme = Themes[nextTheme]; changed = true; }
        var refresh = EditorGUILayout.Toggle(EditorLocalization.Tr("Auto Refresh Assets"), value.AutoRefreshAssets);
        if (refresh != value.AutoRefreshAssets) { value.AutoRefreshAssets = refresh; changed = true; }
        var meta = EditorGUILayout.Toggle(EditorLocalization.Tr("Show Meta Files"), value.ShowAssetMetaFiles);
        if (meta != value.ShowAssetMetaFiles) { value.ShowAssetMetaFiles = meta; changed = true; }
        EditorGUILayout.HelpBox(EditorLocalization.Tr("Built-in font"), MessageType.Info);
        if (changed) EditorPreferences.Save();
    }

    private static void DrawPlayerSettings()
    {
        var value = EditorProjectSettings.current;
        var changed = false;
        var company = EditorGUILayout.TextField(EditorLocalization.Tr("Company Name"), value.CompanyName);
        if (company != value.CompanyName && !string.IsNullOrWhiteSpace(company)) { value.CompanyName = company; changed = true; }
        var product = EditorGUILayout.TextField(EditorLocalization.Tr("Product Name"), value.ProductName);
        if (product != value.ProductName && !string.IsNullOrWhiteSpace(product)) { value.ProductName = product; changed = true; }
        var width = Math.Max(320, EditorGUILayout.IntField(EditorLocalization.Tr("Default Width"), value.DefaultScreenWidth));
        if (width != value.DefaultScreenWidth) { value.DefaultScreenWidth = width; changed = true; }
        var height = Math.Max(200, EditorGUILayout.IntField(EditorLocalization.Tr("Default Height"), value.DefaultScreenHeight));
        if (height != value.DefaultScreenHeight) { value.DefaultScreenHeight = height; changed = true; }
        var fullscreen = EditorGUILayout.Toggle(EditorLocalization.Tr("Full Screen"), value.FullScreen);
        if (fullscreen != value.FullScreen) { value.FullScreen = fullscreen; changed = true; }
        if (changed) EditorProjectSettings.Save();
    }

    private static void DrawGraphicsSettings()
    {
        var value = EditorProjectSettings.current;
        var backend = Math.Max(0, Array.FindIndex(Backends, item => item.Equals(value.GraphicsBackend,
            StringComparison.OrdinalIgnoreCase)));
        var next = EditorGUILayout.Popup(EditorLocalization.Tr("Graphics Backend"), backend, Backends);
        EditorGUILayout.HelpBox(EditorLocalization.Tr("Restart renderer"), MessageType.Info);
        if (next == backend) return;
        value.GraphicsBackend = Backends[next];
        EditorProjectSettings.Save();
    }

    private static void DrawProjectEditorSettings()
    {
        var value = EditorProjectSettings.current;
        var locale = Math.Max(0, Array.IndexOf(LocaleValues, value.Locale));
        var next = EditorGUILayout.Popup(EditorLocalization.Tr("Language"), locale, Locales);
        if (next == locale) return;
        value.Locale = LocaleValues[next];
        EditorProjectSettings.Save();
    }

    private static void DrawScriptingSettings()
    {
        if (!_scriptingSettingsPath.Equals(EditorProjectSettings.settingsPath, StringComparison.OrdinalIgnoreCase))
        {
            _scriptingSettingsPath = EditorProjectSettings.settingsPath;
            _scriptingDefineSymbols = string.Join(';', EditorProjectSettings.current.ScriptingDefineSymbols);
            _scriptingDirty = false;
            _scriptingError = string.Empty;
        }

        var next = EditorGUILayout.TextField("Scripting Define Symbols", _scriptingDefineSymbols);
        if (!next.Equals(_scriptingDefineSymbols, StringComparison.Ordinal))
        {
            _scriptingDefineSymbols = next;
            _scriptingDirty = true;
            _scriptingError = ValidateScriptingSymbols(ParseScriptingSymbols(next));
        }
        if (_scriptingError.Length > 0) EditorGUILayout.HelpBox(_scriptingError, MessageType.Error);
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(!_scriptingDirty);
        if (GUILayout.Button("Revert", GUILayout.Width(80)))
        {
            _scriptingDefineSymbols = string.Join(';', EditorProjectSettings.current.ScriptingDefineSymbols);
            _scriptingDirty = false;
            _scriptingError = string.Empty;
        }
        EditorGUI.EndDisabledGroup();
        EditorGUI.BeginDisabledGroup(!_scriptingDirty || _scriptingError.Length > 0);
        if (GUILayout.Button("Apply", GUILayout.Width(80)))
        {
            EditorProjectSettings.current.ScriptingDefineSymbols = ParseScriptingSymbols(_scriptingDefineSymbols);
            EditorProjectSettings.Save();
            _scriptingDirty = false;
            EditorApplication.delayCall += () => CompilationPipeline.RequestScriptCompilation();
        }
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();
    }

    private static List<string> ParseScriptingSymbols(string value) => value
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    private static string ValidateScriptingSymbols(IEnumerable<string> symbols)
    {
        foreach (var symbol in symbols)
        {
            if (!(char.IsLetter(symbol[0]) || symbol[0] == '_') ||
                symbol.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
                return $"Invalid scripting define symbol '{symbol}'.";
        }
        return string.Empty;
    }
}
