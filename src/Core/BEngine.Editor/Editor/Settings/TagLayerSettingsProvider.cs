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
        DrawError();

        if (_page == TagLayerSettingsPage.Tags) DrawTags();
        else DrawLayers();
    }

    private static void DrawTabs()
    {
        GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.dockTab.fixedHeight));
        var tabWidth = Fix64.Max(44, (GUILayout.CurrentGroupWidth - 12) / 2);
        DrawTab(TagLayerSettingsPage.Tags, "Tags", "Edit the project's GameObject tags", tabWidth);
        DrawTab(TagLayerSettingsPage.Layers, "Layers", "Edit the project's rendering layers", tabWidth);
        GUILayout.EndHorizontal();
    }

    private static void DrawTab(TagLayerSettingsPage page, string label, string tooltip, Fix64 width)
    {
        var selected = _page == page;
        if (GUILayout.Button(new GUIContent(label, tooltip: tooltip),
                selected ? EditorStyles.dockTabActive : EditorStyles.dockTab,
                GUILayout.Width(width)))
            _page = page;

        if (!selected) return;
        var rect = GUILayoutUtility.GetLastRect();
        GUI.DrawRect(new Rect(rect.x, Fix64.Max(rect.y, rect.yMax - 2), rect.width, 2),
            EditorStyles.progressBarBar.normal.backgroundColor);
    }

    private static void DrawError()
    {
        var error = _operationError.Length > 0 ? _operationError : Draft.Error;
        if (error.Length > 0) EditorGUILayout.HelpBox(error, MessageType.Error);
    }

    private static void DrawTags()
    {
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
        GUILayout.Label("Layer values are one-based indices. Built-in layers cannot be removed.",
            EditorStyles.miniLabel);
        GUILayout.Space(4);

        var layerNames = Draft.EditableLayerNames;
        for (var offset = 0; offset < layerNames.Count; offset++)
        {
            var index = SortingLayer.MinimumIndex + offset;
            var layer = Draft.SortingLayers[offset];
            GUILayout.BeginHorizontal(GUILayout.Height(EditorGUIUtility.singleLineHeight + 4));
            GUILayout.Label($"{index}", EditorStyles.miniLabel, GUILayout.Width(28));
            if (layer.BuiltIn)
                GUILayout.Label(new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Lock,
                    "Built-in layers cannot be removed"), GUILayout.Width(24));
            else
                GUILayout.Space(24);
            var name = GUILayout.TextField(layerNames[offset],
                GUILayout.Width(Fix64.Max(100, GUILayout.CurrentGroupWidth - 142)));
            if (!name.Equals(layerNames[offset], StringComparison.Ordinal))
                Draft.SetLayerName(index, name);
            EditorGUI.BeginDisabledGroup(!Draft.CanMoveLayer(index, -1));
            if (GUILayout.Button("^", EditorStyles.toolbarButton, GUILayout.Width(24)))
            {
                Draft.MoveLayer(index, -1);
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();
                break;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!Draft.CanMoveLayer(index, 1));
            if (GUILayout.Button("v", EditorStyles.toolbarButton, GUILayout.Width(24)))
            {
                Draft.MoveLayer(index, 1);
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();
                break;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!Draft.CanRemoveLayer(index));
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Delete,
                    $"Remove layer '{layerNames[offset]}'", GUILayout.Width(24)))
            {
                Draft.RemoveLayer(index);
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();
                break;
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(4);
        EditorGUI.BeginDisabledGroup(layerNames.Count >= SortingLayer.MaximumIndex);
        if (GUILayout.Button(new GUIContent("Add Layer", EditorBuiltinIcons.Toolbar.Add, "Add a layer"),
                GUILayout.Width(112)))
            Draft.AddLayer();
        EditorGUI.EndDisabledGroup();
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
