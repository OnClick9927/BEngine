using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal static class EditorSkinPreferences
{
    internal const string BuiltInPrefix = "builtin:";
    internal const string EditorDataPrefix = "editor-data:";

    internal static bool Draw(EditorPreferencesDocument preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(EditorLocalization.Tr("Theme"), EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(EditorLocalization.Tr("New"), GUILayout.Width(72))) CreateSkinFromUi();
        GUILayout.EndHorizontal();

        var changed = false;
        foreach (var skin in EditorAppearance.builtInSkins)
            changed |= DrawRow(preferences, skin, canDelete: false);
        foreach (var skin in FindCustomSkins())
        {
            changed |= DrawRow(preferences, skin, canDelete: true);
            changed |= DrawPresetRow(skin);
        }
        return changed;
    }

    internal static string GetToken(GUISkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (skin.isBuiltIn) return BuiltInPrefix + skin.name;
        var path = AssetDatabase.GetAssetPath(skin);
        if (TryGetEditorDataRelativePath(path, out var relative))
            return EditorDataPrefix + relative.Replace('\\', '/');
        return path.Replace('\\', '/');
    }

    internal static string ResolveTokenPath(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var value = token.Trim();
        if (!value.StartsWith(EditorDataPrefix, StringComparison.OrdinalIgnoreCase)) return value;

        var relative = value[EditorDataPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        if (relative.Length == 0 || Path.IsPathRooted(relative) || Path.IsPathFullyQualified(relative))
            throw new InvalidDataException($"Invalid editor-data GUI skin path '{token}'.");
        var resolved = Path.GetFullPath(Path.Combine(EditorDataPaths.rootPath, relative));
        if (!IsInsideDirectory(resolved, EditorDataPaths.rootPath))
            throw new InvalidDataException($"GUI skin path '{token}' escapes EditorData.");
        return resolved;
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
        return token.Length == 0 ? null : BAsset.Load<GUISkin>(ResolveTokenPath(token));
    }

    internal static void Select(EditorPreferencesDocument preferences, GUISkin skin)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(skin);
        preferences.EditorSkin = GetToken(skin);
        preferences.EditorTheme = skin.isBuiltIn ? skin.name : nameof(EditorTheme.Custom);
        EditorAppearance.SetSkin(skin);
    }

    internal static IReadOnlyList<GUISkin> FindCustomSkins()
    {
        var paths = Directory.EnumerateFiles(EditorDataPaths.themesPath, $"*{GUISkin.FileExtension}",
                SearchOption.AllDirectories)
            .Concat(AssetDatabase.GetAllAssetPaths().Where(path =>
                path.EndsWith(GUISkin.FileExtension, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
        var result = new List<GUISkin>();
        foreach (var path in paths)
        {
            try
            {
                var skin = Path.IsPathFullyQualified(path)
                    ? BAsset.Load<GUISkin>(path)
                    : AssetDatabase.LoadAssetAtPath<GUISkin>(path);
                if (skin is { isBuiltIn: false }) result.Add(skin);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidDataException or FormatException or
                                              YamlDotNet.Core.YamlException)
            {
                Debug.LogWarning($"Could not load GUI skin '{path}': {exception.Message}");
            }
        }
        return result;
    }

    internal static GUISkin CreateSkin(EditorTheme? preset = null)
    {
        var source = preset is { } requested
            ? GetBuiltInSkin(requested)
            : EditorAppearance.activeSkin;
        var path = GenerateUniqueSkinPath("New GUI Skin");
        source.Clone("New GUI Skin").Save(path);
        BAsset.Invalidate(path);
        return BAsset.Load<GUISkin>(path) ??
               throw new InvalidDataException($"Could not load the created GUI skin '{path}'.");
    }

    internal static bool ApplyPreset(GUISkin skin, EditorTheme preset)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (skin.isReadOnly) return false;
        var customStyles = (skin.customStyles ?? [])
            .Where(static style => style is not null)
            .Select(static style => style.Clone())
            .ToArray();
        skin.CopyFrom(GetBuiltInSkin(preset));
        skin.customStyles = customStyles;
        skin.Apply();
        EditorUtility.SetDirty(skin);
        return SaveSkin(skin);
    }

    internal static bool SaveSkin(GUISkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (skin.isReadOnly) return false;
        var path = AssetDatabase.GetAssetPath(skin);
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var saved = false;
            if (IsEditorDataSkinPath(path))
            {
                skin.Save(path);
                BAsset.Invalidate(path);
                EditorUtility.ClearDirty(skin);
                saved = true;
            }
            else saved = AssetDatabase.SaveAsset(skin);
            if (saved) EditorAppearance.RefreshSkin(skin);
            return saved;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or InvalidOperationException)
        {
            Debug.LogException(exception);
            return false;
        }
    }

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
            using var disabled = new EditorGUI.DisabledScope(!CanWrite(skin));
            if (GUI.Button(delete, EditorLocalization.Tr("Delete")))
                changed |= DeleteSkin(preferences, skin);
        }
        return changed;
    }

    private static bool DrawPresetRow(GUISkin skin)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(116);
        GUILayout.Label(EditorLocalization.Tr("Theme Preset"), GUILayout.Width(92));
        var applied = false;
        using (new EditorGUI.DisabledScope(!CanWrite(skin)))
            foreach (var preset in new[] { EditorTheme.Light, EditorTheme.Dark, EditorTheme.Classic })
                if (GUILayout.Button(preset.ToString(), GUILayout.Width(72)))
                    applied |= ApplyPreset(skin, preset);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        return applied;
    }

    private static bool IsSelected(EditorPreferencesDocument preferences, GUISkin skin)
    {
        var selected = preferences.EditorSkin?.Trim() ?? string.Empty;
        if (selected.Length > 0) return selected.Equals(GetToken(skin), StringComparison.OrdinalIgnoreCase);
        return skin.isBuiltIn && skin.name.Equals(preferences.EditorTheme, StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateSkinFromUi()
    {
        try
        {
            var created = CreateSkin();
            Selection.activeObject = created;
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
        var deleted = false;
        if (IsEditorDataSkinPath(path))
        {
            try
            {
                File.Delete(path);
                BAsset.Invalidate(path);
                deleted = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogException(exception);
            }
        }
        else deleted = AssetDatabase.DeleteAsset(path);
        if (!deleted) return false;
        if (wasSelected) Select(preferences, GetBuiltInSkin(EditorTheme.Dark));
        if (clearSelection) Selection.activeObject = null;
        return true;
    }

    private static GUISkin GetBuiltInSkin(EditorTheme preset)
    {
        if (preset is not (EditorTheme.Light or EditorTheme.Dark or EditorTheme.Classic))
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Only built-in presets can be copied.");
        return EditorAppearance.builtInSkins.First(skin =>
            skin.name.Equals(preset.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateUniqueSkinPath(string name)
    {
        var directory = EditorDataPaths.themesPath;
        var candidate = Path.Combine(directory, name + GUISkin.FileExtension);
        for (var suffix = 1; File.Exists(candidate) || Directory.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{name} {suffix}{GUISkin.FileExtension}");
        return candidate;
    }

    private static bool CanWrite(GUISkin skin) => IsEditorDataSkinPath(AssetDatabase.GetAssetPath(skin)) ||
                                                   EditorAssetWritePolicy.CanWrite;

    private static bool IsEditorDataSkinPath(string path) =>
        TryGetEditorDataRelativePath(path, out var relative) &&
        IsInsideDirectory(Path.Combine(EditorDataPaths.rootPath, relative), EditorDataPaths.themesPath);

    private static bool TryGetEditorDataRelativePath(string path, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        var fullPath = Path.GetFullPath(path);
        if (!IsInsideDirectory(fullPath, EditorDataPaths.rootPath)) return false;
        relative = Path.GetRelativePath(EditorDataPaths.rootPath, fullPath);
        return true;
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
