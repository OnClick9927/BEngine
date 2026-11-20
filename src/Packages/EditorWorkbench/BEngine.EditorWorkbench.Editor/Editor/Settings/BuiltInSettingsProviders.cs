using BEngine.Rendering.Rhi;
using BEngine.Build;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal static class BuiltInSettingsProviders
{
    private static readonly string[] Locales = ["中文 (简体)", "English"];
    private static readonly string[] LocaleValues = ["zh-CN", "en-US"];
    private static readonly string[] Fonts = ["BEngine Built-in", "Segoe UI", "Microsoft YaHei"];
    private static readonly string[] Backends = Enum.GetNames<GraphicsBackend>();
    private static string _scriptingSettingsPath = string.Empty;
    private static string _scriptingDefineSymbols = string.Empty;
    private static string _scriptingError = string.Empty;
    private static bool _scriptingDirty;

    [SettingsProvider]
    private static SettingsProvider GeneralPreferences() => new("Preferences/General", SettingsScope.User,
        ["scale", "font", "language", "locale", "缩放", "字体", "语言"])
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
    private static SettingsProvider ThemePreferences() => new("Preferences/Theme", SettingsScope.User,
        ["theme", "skin", "GUISkin", "Light", "Dark", "Classic", "主题", "皮肤"])
    {
        label = EditorLocalization.Tr("Theme"),
        guiHandler = _ =>
        {
            if (!EditorSkinPreferences.Draw(EditorPreferences.current)) return;
            EditorPreferences.Save();
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
    private static SettingsProvider GraphicsProjectSettings() => new("Project/Graphics", SettingsScope.Project,
        ["Vulkan", "OpenGL", "Direct3D", "WebGPU", "图形", "渲染"])
    {
        label = EditorLocalization.Tr("Graphics"),
        guiHandler = _ => DrawGraphicsSettings()
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
        var scale = EditorGUILayout.Slider(EditorLocalization.Tr("Editor Scale"), value.EditorScale,
            EditorAppearance.MinimumScale, EditorAppearance.MaximumScale);
        if (Math.Abs(scale - value.EditorScale) > .001f) { value.EditorScale = scale; changed = true; }
        var font = Math.Max(0, Array.IndexOf(Fonts, value.EditorFont));
        var nextFont = EditorGUILayout.Popup(EditorLocalization.Tr("Font"), font, Fonts);
        if (nextFont != font) { value.EditorFont = Fonts[nextFont]; changed = true; }
        EditorGUI.BeginDisabledGroup(true);
        _ = EditorGUILayout.IntField(EditorLocalization.Tr("Font Size"), EditorAppearance.DefaultFontSize);
        EditorGUI.EndDisabledGroup();
        var refresh = EditorGUILayout.Toggle(EditorLocalization.Tr("Auto Refresh Assets"), value.AutoRefreshAssets);
        if (refresh != value.AutoRefreshAssets) { value.AutoRefreshAssets = refresh; changed = true; }
        var meta = EditorGUILayout.Toggle(EditorLocalization.Tr("Show Meta Files"), value.ShowAssetMetaFiles);
        if (meta != value.ShowAssetMetaFiles) { value.ShowAssetMetaFiles = meta; changed = true; }
        EditorGUILayout.HelpBox(EditorLocalization.Tr("Built-in font"), MessageType.Info);
        if (changed) EditorPreferences.Save();
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

    private static void DrawScriptingSettings()
    {
        if (!_scriptingSettingsPath.Equals(EditorProjectSettings.settingsPath, StringComparison.OrdinalIgnoreCase))
        {
            _scriptingSettingsPath = EditorProjectSettings.settingsPath;
            _scriptingDefineSymbols = string.Join(';', EditorProjectSettings.current.ScriptingDefineSymbols);
            _scriptingDirty = false;
            _scriptingError = string.Empty;
        }

        if (!_scriptingDirty)
            _scriptingDefineSymbols = string.Join(';', EditorScriptingDefines.symbols);
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
            EditorScriptingDefines.SetSymbols(ParseScriptingSymbols(_scriptingDefineSymbols));
            _scriptingDirty = false;
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
            if (!(char.IsLetter(symbol[0]) || symbol[0] == '_') || Matches7850())
                return $"Invalid scripting define symbol '{symbol}'.";
            [global::BEngine.SkipLoopGuard]
            bool Matches7850()
            {
                var matchSource7850 = symbol.Skip(1);
                ArgumentNullException.ThrowIfNull(matchSource7850, "source");
                global::System.ReadOnlySpan<char> matchValues7850;
                if (matchSource7850.GetType() == typeof(char[]))
                {
                    matchValues7850 = (char[])(object)matchSource7850;
                }
                else if (matchSource7850.GetType() == typeof(global::System.Collections.Generic.List<char>))
                {
                    matchValues7850 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<char>)(object)matchSource7850);
                }
                else
                {
                    foreach (var character in (global::System.Collections.Generic.IEnumerable<char>)matchSource7850)
                    {
                        if (!(char.IsLetterOrDigit(character) || character == '_'))
                            return true;
                    }
            
                    return false;
                }
            
                foreach (var character in matchValues7850)
                {
                    if (!(char.IsLetterOrDigit(character) || character == '_'))
                        return true;
                }
            
                return false;
            }
        }
        return string.Empty;
    }
}
