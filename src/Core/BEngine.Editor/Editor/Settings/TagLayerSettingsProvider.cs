namespace BEngine.Editor;

internal static class TagLayerSettingsProvider
{
    internal const string SettingsPath = "Project/Tags and Layers";
    private static readonly TagLayerSettingsDraft Draft = new();
    private static string _projectSettingsPath = string.Empty;
    private static string _operationError = string.Empty;
    private static TagLayerSettingsPage _page;
    private static bool _loaded;

    [SettingsProvider]
    private static SettingsProvider Create() => new(SettingsPath, SettingsScope.Project,
        ["tag", "tags", "layer", "layers", "sorting layer", "标签", "层级"])
    {
        label = "Tags and Layers",
        activateHandler = _ => EnsureProject(),
        guiHandler = _ => Draw(),
        footerBarGuiHandler = DrawFooter
    };

    internal static void OpenTags() => Open(TagLayerSettingsPage.Tags);

    internal static void OpenLayers() => Open(TagLayerSettingsPage.Layers);

    internal static void Open(TagLayerSettingsPage page)
    {
        _page = page;
        SettingsService.OpenProjectSettings(SettingsPath);
    }

    internal static string ResolveSettingsPath(string path)
    {
        path ??= string.Empty;
        if (path.Equals("Project/Tags", StringComparison.OrdinalIgnoreCase))
        {
            _page = TagLayerSettingsPage.Tags;
            return SettingsPath;
        }
        if (path.Equals("Project/Layers", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("Project/Sorting Layers", StringComparison.OrdinalIgnoreCase))
        {
            _page = TagLayerSettingsPage.Layers;
            return SettingsPath;
        }
        return path;
    }

    private static void Draw()
    {
        EnsureProject();
        DrawTabs();
        GUILayout.Space(6);
        var error = _operationError.Length > 0 ? _operationError : Draft.Error;
        if (error.Length > 0) EditorGUILayout.HelpBox(error, MessageType.Error);

        if (_page == TagLayerSettingsPage.Tags) DrawTags();
        else DrawLayers();
    }

    private static void DrawTabs()
    {
        GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.dockTab.fixedHeight));
        var tabWidth = Fix64.Max(44, (GUILayout.CurrentGroupWidth - 12) / 2);
        if (GUILayout.Button("Tags",
                _page == TagLayerSettingsPage.Tags ? EditorStyles.dockTabActive : EditorStyles.dockTab,
                GUILayout.Width(tabWidth)))
            _page = TagLayerSettingsPage.Tags;
        if (GUILayout.Button("Layers",
                _page == TagLayerSettingsPage.Layers ? EditorStyles.dockTabActive : EditorStyles.dockTab,
                GUILayout.Width(tabWidth)))
            _page = TagLayerSettingsPage.Layers;
        GUILayout.EndHorizontal();
    }

    private static void DrawTags()
    {
        GUILayout.Label("Tags", EditorStyles.largeLabel);
        GUILayout.Space(4);

        var tags = Draft.EditableTags;
        for (var index = 0; index < tags.Count; index++)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(EditorGUIUtility.singleLineHeight + 4));
            GUILayout.Label($"{index}", EditorStyles.miniLabel, GUILayout.Width(28));
            if (index == 0)
            {
                GUILayout.Label(new GUIContent(tags[index], EditorBuiltinIcons.Toolbar.Lock,
                        "Untagged cannot be renamed or removed"),
                    GUILayout.Width(Fix64.Max(100, GUILayout.CurrentGroupWidth - 40)));
                GUILayout.EndHorizontal();
                continue;
            }

            var fieldWidth = Fix64.Max(80, GUILayout.CurrentGroupWidth - 68);
            var name = GUILayout.TextField(tags[index], GUILayout.Width(fieldWidth));
            if (!name.Equals(tags[index], StringComparison.Ordinal))
                Draft.RenameTag(index, name);
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Delete, $"Remove tag '{tags[index]}'",
                    GUILayout.Width(24)))
            {
                Draft.RemoveTag(index);
                GUILayout.EndHorizontal();
                break;
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("Add Tag", EditorBuiltinIcons.Toolbar.Add, "Add a tag"),
                GUILayout.Width(112)))
            Draft.AddTag();
        GUILayout.EndHorizontal();
    }

    private static void DrawLayers()
    {
        GUILayout.Label("Layers", EditorStyles.largeLabel);
        GUILayout.Label("World: 2^1 - 2^58    UI: 2^59 - 2^63", EditorStyles.miniLabel);
        GUILayout.Space(4);

        var layerNames = Draft.EditableLayerNames;
        for (var offset = 0; offset < layerNames.Count; offset++)
        {
            var index = SortingLayer.MinimumIndex + offset;
            var group = index >= SortingLayer.UiBaseIndex ? "UI" : "World";
            GUILayout.BeginHorizontal(GUILayout.Height(EditorGUIUtility.singleLineHeight + 4));
            GUILayout.Label($"2^{index}", EditorStyles.miniLabel, GUILayout.Width(52));
            GUILayout.Label(group, EditorStyles.miniLabel, GUILayout.Width(48));
            var name = GUILayout.TextField(layerNames[offset],
                GUILayout.Width(Fix64.Max(100, GUILayout.CurrentGroupWidth - 116)));
            if (!name.Equals(layerNames[offset], StringComparison.Ordinal))
                Draft.SetLayerName(index, name);
            GUILayout.EndHorizontal();
        }
    }

    private static void DrawFooter()
    {
        EnsureProject();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(!Draft.IsDirty);
        if (GUILayout.Button("Revert", GUILayout.Width(84))) Reload();
        EditorGUI.EndDisabledGroup();
        EditorGUI.BeginDisabledGroup(!Draft.IsDirty || Draft.Error.Length > 0 ||
                                     !EditorAssetWritePolicy.CanWrite);
        if (GUILayout.Button("Apply", GUILayout.Width(84))) Apply();
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();
    }

    private static void Apply()
    {
        try
        {
            ProjectTagLayerSettingsApplier.Apply(Draft);
            Reload();
        }
        catch (Exception exception)
        {
            _operationError = $"Could not apply Tags and Layers: {exception.Message}";
            Debug.LogException(exception);
        }
    }

    private static void EnsureProject()
    {
        if (_loaded && _projectSettingsPath.Equals(EditorProjectSettings.settingsPath,
                StringComparison.OrdinalIgnoreCase)) return;
        Reload();
    }

    private static void Reload()
    {
        _projectSettingsPath = EditorProjectSettings.settingsPath;
        _loaded = true;
        _operationError = string.Empty;
        Draft.Reload();
    }
}

internal enum TagLayerSettingsPage
{
    Tags,
    Layers
}
