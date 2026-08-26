namespace BEngine.Editor;

[CustomEditor(typeof(GUISkin))]
public sealed class GUISkinEditor : BAssetEditor
{
    private readonly HashSet<string> _expandedStyles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _paletteExpanded = true;

    public override void OnInspectorGUI()
    {
        if (target is not GUISkin skin) return;
        if (skin.isBuiltIn)
            EditorGUILayout.HelpBox("Built-in GUI skins are read-only. Duplicate this skin to customize it.",
                MessageType.Info);

        EditorGUI.BeginChangeCheck();
        using (new EditorGUI.DisabledScope(skin.isReadOnly))
        {
            DrawPalette(skin);
            EditorGUILayout.LabelField("Built-in Styles", EditorStyles.boldLabel);
            foreach (var (slot, style) in skin.EnumerateBuiltInStyles()) DrawStyle(slot, style, canRename: false);

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Custom Styles", EditorStyles.boldLabel);
            DrawCustomStyles(skin);
        }

        if (EditorGUI.EndChangeCheck() && !skin.isReadOnly)
        {
            skin.Apply();
            EditorUtility.SetDirty(skin);
            hasUnsavedChanges = true;
        }
        if (!skin.isReadOnly) DrawApplyBar();
    }

    public override void SaveChanges()
    {
        var skin = target as GUISkin;
        base.SaveChanges();
        if (skin is not null && EditorAppearance.IsActiveSkin(skin))
            EditorAppearance.RefreshSkin(skin);
    }

    public override void DiscardChanges()
    {
        var skin = target as GUISkin;
        base.DiscardChanges();
        if (skin is not null && EditorAppearance.IsActiveSkin(skin))
            EditorAppearance.RefreshSkin(skin);
    }

    private void DrawPalette(GUISkin skin)
    {
        _paletteExpanded = EditorGUILayout.Foldout(_paletteExpanded, "Skin Colors");
        if (!_paletteExpanded) return;
        var value = skin.palette;
        EditorGUI.indentLevel++;
        skin.palette = new EditorThemePalette(
            EditorGUILayout.ColorField("Window", value.Window),
            EditorGUILayout.ColorField("Panel", value.Panel),
            EditorGUILayout.ColorField("Toolbar", value.Toolbar),
            EditorGUILayout.ColorField("Field", value.Field),
            EditorGUILayout.ColorField("Button", value.Button),
            EditorGUILayout.ColorField("Hover", value.Hover),
            EditorGUILayout.ColorField("Active", value.Active),
            EditorGUILayout.ColorField("Text", value.Text),
            EditorGUILayout.ColorField("Muted Text", value.MutedText),
            EditorGUILayout.ColorField("Accent", value.Accent),
            EditorGUILayout.ColorField("Border", value.Border),
            EditorGUILayout.ColorField("Panel Raised", value.PanelRaised),
            EditorGUILayout.ColorField("Title Bar", value.TitleBar),
            EditorGUILayout.ColorField("Field Hover", value.FieldHover),
            EditorGUILayout.ColorField("Field Focused", value.FieldFocused),
            EditorGUILayout.ColorField("Button Hover", value.ButtonHover),
            EditorGUILayout.ColorField("Button Pressed", value.ButtonPressed),
            EditorGUILayout.ColorField("Disabled Text", value.DisabledText),
            EditorGUILayout.ColorField("Focus Border", value.FocusBorder),
            EditorGUILayout.ColorField("Selection", value.Selection),
            EditorGUILayout.ColorField("Selection Inactive", value.SelectionInactive),
            EditorGUILayout.ColorField("Scroll Track", value.ScrollTrack),
            EditorGUILayout.ColorField("Scroll Thumb", value.ScrollThumb),
            EditorGUILayout.ColorField("Scroll Thumb Hover", value.ScrollThumbHover),
            EditorGUILayout.ColorField("Shadow", value.Shadow));
        EditorGUI.indentLevel--;
        GUILayout.Space(6);
    }

    private void DrawCustomStyles(GUISkin skin)
    {
        var custom = skin.customStyles ?? [];
        var removeIndex = -1;
        for (var index = 0; index < custom.Length; index++)
        {
            var style = custom[index] ?? new GUIStyle($"customStyle{index + 1}");
            custom[index] = style;
            DrawStyle($"Custom/{index}/{style.name}", style, canRename: true);
            if (_expandedStyles.Contains($"Custom/{index}/{style.name}") &&
                GUILayout.Button("Remove", GUILayout.Width(72))) removeIndex = index;
        }
        if (removeIndex >= 0)
        {
            skin.customStyles = custom.Where((_, index) => index != removeIndex).ToArray();
            GUI.changed = true;
        }
        if (!GUILayout.Button("Add Custom Style")) return;
        var usedNames = custom.Where(static style => style is not null)
            .Select(static style => style.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suffix = 1;
        while (usedNames.Contains($"customStyle{suffix}")) suffix++;
        skin.customStyles = [.. custom, new GUIStyle($"customStyle{suffix}")];
        GUI.changed = true;
    }

    private void DrawStyle(string slot, GUIStyle style, bool canRename)
    {
        var expanded = _expandedStyles.Contains(slot);
        var nextExpanded = EditorGUILayout.Foldout(expanded, canRename ? style.name : slot);
        if (nextExpanded) _expandedStyles.Add(slot);
        else _expandedStyles.Remove(slot);
        if (!nextExpanded) return;

        EditorGUI.indentLevel++;
        if (canRename) style.name = EditorGUILayout.TextField("Name", style.name);
        style.fixedWidth = (Fix64)Math.Max(0, EditorGUILayout.FloatField("Fixed Width", (float)style.fixedWidth));
        style.fixedHeight = (Fix64)Math.Max(0, EditorGUILayout.FloatField("Fixed Height", (float)style.fixedHeight));
        style.borderWidth = (Fix64)Math.Max(0, EditorGUILayout.FloatField("Border Width", (float)style.borderWidth));
        style.fontSize = (Fix64)Math.Max(1, EditorGUILayout.FloatField("Font Size", (float)style.fontSize));
        style.alignment = (TextAnchor)EditorGUILayout.EnumPopup("Alignment", style.alignment);
        style.wordWrap = EditorGUILayout.Toggle("Word Wrap", style.wordWrap);
        style.richText = EditorGUILayout.Toggle("Rich Text", style.richText);
        style.stretchWidth = EditorGUILayout.Toggle("Stretch Width", style.stretchWidth);
        style.stretchHeight = EditorGUILayout.Toggle("Stretch Height", style.stretchHeight);
        DrawState(slot, "Normal", style.normal);
        DrawState(slot, "Hover", style.hover);
        DrawState(slot, "Active", style.active);
        DrawState(slot, "Focused", style.focused);
        DrawState(slot, "On Normal", style.onNormal);
        DrawState(slot, "On Hover", style.onHover);
        DrawState(slot, "On Active", style.onActive);
        DrawState(slot, "On Focused", style.onFocused);
        DrawState(slot, "Disabled", style.disabled);
        EditorGUI.indentLevel--;
    }

    private void DrawState(string slot, string stateName, GUIStyleState state)
    {
        var key = $"{slot}/{stateName}";
        var expanded = _expandedStates.Contains(key);
        var nextExpanded = EditorGUILayout.Foldout(expanded, stateName);
        if (nextExpanded) _expandedStates.Add(key);
        else _expandedStates.Remove(key);
        if (!nextExpanded) return;

        EditorGUI.indentLevel++;
        state.textColor = EditorGUILayout.ColorField("Text Color", state.textColor);
        state.backgroundColor = EditorGUILayout.ColorField("Background Color", state.backgroundColor);
        state.borderColor = EditorGUILayout.ColorField("Border Color", state.borderColor);
        state.backgroundImage = EditorGUILayout.ObjectField("Background Image", state.backgroundImage,
            allowSceneObjects: false);
        EditorGUI.indentLevel--;
    }
}
