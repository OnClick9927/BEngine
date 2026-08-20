namespace BEngine.Editor;

internal static class TagLayerSettingsProvider
{
    private static string _settingsPath = string.Empty;
    private static string _tags = string.Empty;
    private static string _error = string.Empty;
    private static bool _dirty;

    [SettingsProvider]
    private static SettingsProvider Create() => new("Project/Tags", SettingsScope.Project,
        ["tag", "tags", "标签"])
    {
        label = "Tags",
        guiHandler = _ => Draw()
    };

    private static void Draw()
    {
        EnsureCache();
        var tags = EditorGUILayout.TextArea(_tags, GUILayout.Height(220));
        GUILayout.Label("Tags (one per line)", EditorStyles.miniLabel);
        if (!tags.Equals(_tags, StringComparison.Ordinal))
        {
            _tags = tags;
            _dirty = true;
            _error = Validate(out _);
        }
        if (_error.Length > 0) EditorGUILayout.HelpBox(_error, MessageType.Error);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(!_dirty);
        if (GUILayout.Button("Revert", GUILayout.Width(80))) Reload();
        EditorGUI.EndDisabledGroup();
        EditorGUI.BeginDisabledGroup(!_dirty || _error.Length > 0);
        if (GUILayout.Button("Apply", GUILayout.Width(80)) && Validate(out var parsedTags).Length == 0)
        {
            EditorProjectSettings.current.Tags = parsedTags;
            EditorProjectSettings.Save();
            _dirty = false;
        }
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();
    }

    private static void EnsureCache()
    {
        if (_settingsPath.Equals(EditorProjectSettings.settingsPath, StringComparison.OrdinalIgnoreCase)) return;
        _settingsPath = EditorProjectSettings.settingsPath;
        Reload();
    }

    private static void Reload()
    {
        _tags = string.Join(Environment.NewLine, EditorProjectSettings.current.Tags);
        _dirty = false;
        _error = string.Empty;
    }

    private static string Validate(out List<string> tags)
    {
        var parsed = _tags.Split(['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parsed.GroupBy(static tag => tag, StringComparer.Ordinal).Any(static group => group.Count() > 1))
        {
            tags = [];
            return "Tags must be unique.";
        }
        tags = parsed.Prepend("Untagged").Distinct(StringComparer.Ordinal).ToList();
        return string.Empty;
    }
}
