namespace BEngine.Editor;

[CustomEditor(typeof(GUISkin))]
public sealed class GUISkinEditor : BAssetEditor
{
    private readonly HashSet<string> _expandedStyles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedStates = new(StringComparer.OrdinalIgnoreCase);

    public override void OnInspectorGUI()
    {
        if (target is not GUISkin skin) return;
        if (skin.isBuiltIn)
            EditorGUILayout.HelpBox("Built-in GUI skins are read-only. Duplicate this skin to customize it.",
                MessageType.Info);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("Built-in Styles", EditorStyles.boldLabel);
        foreach (var (slot, style) in skin.EnumerateBuiltInStyles())
            DrawStyle(slot, style, canRename: false, readOnly: skin.isReadOnly);

        GUILayout.Space(6);
        EditorGUILayout.LabelField("Custom Styles", EditorStyles.boldLabel);
        DrawCustomStyles(skin, skin.isReadOnly);

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
        if (target is not GUISkin skin || skin.isReadOnly) return;
        serializedObject.ApplyModifiedProperties();
        if (EditorSkinPreferences.SaveSkin(skin)) hasUnsavedChanges = false;
    }

    public override void DiscardChanges()
    {
        if (target is not GUISkin skin || skin.isReadOnly) return;
        var path = AssetDatabase.GetAssetPath(skin);
        var restored = false;
        if (Path.IsPathFullyQualified(path))
        {
            BAsset.Invalidate(path);
            if (BAsset.Load<GUISkin>(path) is { } persisted)
            {
                skin.CopyFrom(persisted);
                EditorUtility.ClearDirty(skin);
                restored = true;
            }
        }
        else restored = AssetDatabase.RevertAsset(skin);
        if (!restored) return;
        serializedObject.Update();
        hasUnsavedChanges = false;
        if (EditorAppearance.IsActiveSkin(skin)) EditorAppearance.RefreshSkin(skin);
    }

    private void DrawCustomStyles(GUISkin skin, bool readOnly)
    {
        var custom = skin.customStyles ?? [];
        var removeIndex = -1;
        for (var index = 0; index < custom.Length; index++)
        {
            var style = custom[index] ?? new GUIStyle($"customStyle{index + 1}");
            custom[index] = style;
            var styleKey = $"Custom/{index}";
            DrawStyle(styleKey, style, canRename: true, readOnly: readOnly);
            if (_expandedStyles.Contains(styleKey) &&
                !readOnly && GUILayout.Button("Remove", GUILayout.Width(72))) removeIndex = index;
        }
        if (removeIndex >= 0)
        {
            skin.customStyles = custom.Where((_, index) => index != removeIndex).ToArray();
            GUI.changed = true;
        }
        using var disabled = new EditorGUI.DisabledScope(readOnly);
        if (!GUILayout.Button("Add Custom Style")) return;
        var usedNames = custom.Where(static style => style is not null)
            .Select(static style => style.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suffix = 1;
        while (usedNames.Contains($"customStyle{suffix}")) suffix++;
        skin.customStyles = [.. custom, new GUIStyle($"customStyle{suffix}")];
        GUI.changed = true;
    }

    private void DrawStyle(string slot, GUIStyle style, bool canRename, bool readOnly)
    {
        var expanded = _expandedStyles.Contains(slot);
        var nextExpanded = EditorGUILayout.Foldout(expanded, canRename ? style.name : slot);
        if (nextExpanded) _expandedStyles.Add(slot);
        else _expandedStyles.Remove(slot);
        if (!nextExpanded) return;

        EditorGUI.indentLevel++;
        using (new EditorGUI.DisabledScope(readOnly))
        {
            if (canRename) style.name = EditorGUILayout.TextField("Name", style.name);
            style.fixedWidth = (Fix64)Math.Max(0,
                EditorGUILayout.FloatField("Fixed Width", (float)style.fixedWidth));
            style.fixedHeight = (Fix64)Math.Max(0,
                EditorGUILayout.FloatField("Fixed Height", (float)style.fixedHeight));
            style.borderWidth = (Fix64)Math.Max(0,
                EditorGUILayout.FloatField("Border Width", (float)style.borderWidth));
            style.fontSize = (Fix64)Math.Max(1,
                EditorGUILayout.FloatField("Font Size", (float)style.fontSize));
            style.alignment = (TextAnchor)EditorGUILayout.EnumPopup("Alignment", style.alignment);
            style.wordWrap = EditorGUILayout.Toggle("Word Wrap", style.wordWrap);
            style.richText = EditorGUILayout.Toggle("Rich Text", style.richText);
            style.stretchWidth = EditorGUILayout.Toggle("Stretch Width", style.stretchWidth);
            style.stretchHeight = EditorGUILayout.Toggle("Stretch Height", style.stretchHeight);
        }
        DrawState(slot, "Normal", style.normal, readOnly);
        DrawState(slot, "Hover", style.hover, readOnly);
        DrawState(slot, "Active", style.active, readOnly);
        DrawState(slot, "Focused", style.focused, readOnly);
        DrawState(slot, "On Normal", style.onNormal, readOnly);
        DrawState(slot, "On Hover", style.onHover, readOnly);
        DrawState(slot, "On Active", style.onActive, readOnly);
        DrawState(slot, "On Focused", style.onFocused, readOnly);
        DrawState(slot, "Disabled", style.disabled, readOnly);
        EditorGUI.indentLevel--;
    }

    private void DrawState(string slot, string stateName, GUIStyleState state, bool readOnly)
    {
        var key = $"{slot}/{stateName}";
        var expanded = _expandedStates.Contains(key);
        var nextExpanded = EditorGUILayout.Foldout(expanded, stateName);
        if (nextExpanded) _expandedStates.Add(key);
        else _expandedStates.Remove(key);
        if (!nextExpanded) return;

        EditorGUI.indentLevel++;
        using (new EditorGUI.DisabledScope(readOnly))
        {
            state.textColor = EditorGUILayout.ColorField("Text Color", state.textColor);
            state.backgroundColor = EditorGUILayout.ColorField("Background Color", state.backgroundColor);
            state.borderColor = EditorGUILayout.ColorField("Border Color", state.borderColor);
            state.backgroundImage = EditorGUILayout.ObjectField("Background Image", state.backgroundImage,
                allowSceneObjects: false);
        }
        EditorGUI.indentLevel--;
    }
}
