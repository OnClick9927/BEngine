using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal static class EditorSkinPreferences
{
    internal const string BuiltInPrefix = "builtin:";

    internal static bool Draw(EditorPreferencesDocument preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(EditorLocalization.Tr("Theme"), EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(!CanCreateSkin()))
            if (GUILayout.Button(EditorLocalization.Tr("New"), GUILayout.Width(72))) CreateSkin();
        GUILayout.EndHorizontal();

        var changed = false;
        foreach (var skin in EditorAppearance.builtInSkins)
            changed |= DrawRow(preferences, skin, canDelete: false);
        foreach (var skin in FindCustomSkins())
            changed |= DrawRow(preferences, skin, canDelete: true);
        return changed;
    }

    internal static string GetToken(GUISkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (skin.isBuiltIn) return BuiltInPrefix + skin.name;
        return AssetDatabase.GetAssetPath(skin).Replace('\\', '/');
    }

    internal static GUISkin? Resolve(EditorPreferencesDocument preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var token = preferences.EditorSkin?.Trim() ?? string.Empty;
        if (token.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = token[BuiltInPrefix.Length..];
            return EditorAppearance.builtInSkins.FirstOrDefault(skin =>
                skin.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        return token.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<GUISkin>(token);
    }

    internal static void Select(EditorPreferencesDocument preferences, GUISkin skin)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(skin);
        preferences.EditorSkin = GetToken(skin);
        preferences.EditorTheme = skin.isBuiltIn ? skin.name : nameof(EditorTheme.Custom);
        EditorAppearance.SetSkin(skin);
    }

    internal static IReadOnlyList<GUISkin> FindCustomSkins() => AssetDatabase.GetAllAssetPaths()
        .Where(path => path.EndsWith(GUISkin.FileExtension, StringComparison.OrdinalIgnoreCase))
        .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
        .Select(AssetDatabase.LoadAssetAtPath<GUISkin>)
        .Where(static skin => skin is { isBuiltIn: false })
        .Cast<GUISkin>()
        .ToArray();

    private static bool DrawRow(EditorPreferencesDocument preferences, GUISkin skin, bool canDelete)
    {
        var changed = false;
        const int spacing = 4;
        const int setWidth = 58;
        const int deleteWidth = 64;
        var row = GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
        var actionsWidth = setWidth + (canDelete ? deleteWidth + spacing : 0);
        var contentWidth = Fix64.Max(0, row.width - actionsWidth - spacing * 2);
        var labelWidth = Fix64.Min(112, contentWidth / 4);
        var fieldWidth = Fix64.Max(0, contentWidth - labelWidth);
        var label = new Rect(row.x, row.y, labelWidth, row.height);
        var field = new Rect(label.xMax + spacing, row.y, fieldWidth, row.height);
        var set = new Rect(field.xMax + spacing, row.y, setWidth, row.height);
        var delete = new Rect(set.xMax + spacing, row.y, deleteWidth, row.height);

        GUI.Label(label, skin.name);
        var fieldObjectRect = new Rect(field.x, field.y, Fix64.Max(0, field.width - 18), field.height);
        if (Event.current.type == EventType.MouseDown && fieldObjectRect.Contains(Event.current.mousePosition))
            Selection.activeObject = skin;
        var picked = EditorGUI.ObjectField(field, skin, typeof(GUISkin), allowSceneObjects: false) as GUISkin;
        if (picked is not null && !ReferenceEquals(picked, skin)) Selection.activeObject = picked;

        var active = IsSelected(preferences, skin);
        using (new EditorGUI.DisabledScope(active))
        {
            if (GUI.Button(set, EditorLocalization.Tr("Set")))
            {
                Select(preferences, skin);
                changed = true;
            }
        }
        if (canDelete)
        {
            using var disabled = new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite);
            if (GUI.Button(delete, EditorLocalization.Tr("Delete")))
                changed |= DeleteSkin(preferences, skin);
        }
        return changed;
    }

    private static bool IsSelected(EditorPreferencesDocument preferences, GUISkin skin)
    {
        var selected = preferences.EditorSkin?.Trim() ?? string.Empty;
        if (selected.Length > 0) return selected.Equals(GetToken(skin), StringComparison.OrdinalIgnoreCase);
        return skin.isBuiltIn && skin.name.Equals(preferences.EditorTheme, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanCreateSkin() => EditorAssetWritePolicy.CanWrite &&
                                           EditorBridge.Host?.ActiveProjectFolderPath is not null;

    private static void CreateSkin()
    {
        var host = EditorBridge.Host;
        var folder = host?.ActiveProjectFolderPath;
        if (host is null || string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            var created = EditorAppearance.activeSkin.Clone("New GUI Skin");
            var path = AssetDatabase.GenerateUniqueAssetPath(
                $"{folder.TrimEnd('/', '\\')}/New GUI Skin{GUISkin.FileExtension}");
            AssetDatabase.CreateAsset(created, path);
            var imported = AssetDatabase.LoadAssetAtPath<GUISkin>(path) ?? created;
            Selection.activeObject = imported;
            host.RevealProjectAsset(path, beginRename: true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static bool DeleteSkin(EditorPreferencesDocument preferences, GUISkin skin)
    {
        var path = AssetDatabase.GetAssetPath(skin);
        if (skin.isBuiltIn || string.IsNullOrWhiteSpace(path)) return false;
        if (!EditorUtility.DisplayDialog(EditorLocalization.Tr("Delete GUI Skin"),
                $"Delete '{skin.name}'?", EditorLocalization.Tr("Delete"), EditorLocalization.Tr("Cancel")))
            return false;
        var wasSelected = IsSelected(preferences, skin);
        var clearSelection = Selection.activeObject is GUISkin selected &&
                             AssetDatabase.GetAssetPath(selected).Equals(path,
                                 StringComparison.OrdinalIgnoreCase);
        if (!AssetDatabase.DeleteAsset(path)) return false;
        if (wasSelected)
            Select(preferences, EditorAppearance.builtInSkins.First(item =>
                item.name.Equals(nameof(EditorTheme.Dark), StringComparison.OrdinalIgnoreCase)));
        if (clearSelection) Selection.activeObject = null;
        return true;
    }
}
