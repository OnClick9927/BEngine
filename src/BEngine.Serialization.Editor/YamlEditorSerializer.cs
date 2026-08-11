using BEngine.Serialization.Editor.Documents;

namespace BEngine.Serialization.Editor;

public sealed class YamlEditorSerializer
{
    public EditorSettingsDocument LoadEditorSettings(string path)
    {
        var document = YamlUtility.Load<EditorSettingsDocument>(path);
        ValidateEditorSettings(document);
        return document;
    }

    public void SaveEditorSettings(EditorSettingsDocument document, string path)
    {
        ValidateEditorSettings(document);
        YamlUtility.Save(document, path);
    }

    public EditorLayoutDocument LoadEditorLayout(string path)
    {
        var document = YamlUtility.Load<EditorLayoutDocument>(path);
        ValidateEditorLayout(document);
        return document;
    }

    public void SaveEditorLayout(EditorLayoutDocument document, string path)
    {
        ValidateEditorLayout(document);
        YamlUtility.Save(document, path);
    }

    public EditorPreferencesDocument LoadEditorPreferences(string path)
    {
        var document = YamlUtility.Load<EditorPreferencesDocument>(path);
        ValidateEditorPreferences(document);
        return document;
    }

    public void SaveEditorPreferences(EditorPreferencesDocument document, string path)
    {
        ValidateEditorPreferences(document);
        YamlUtility.Save(document, path);
    }

    public LauncherSettingsDocument LoadLauncherSettings(string path)
    {
        var document = YamlUtility.Load<LauncherSettingsDocument>(path);
        ValidateLauncherSettings(document);
        return document;
    }

    public void SaveLauncherSettings(LauncherSettingsDocument document, string path)
    {
        ValidateLauncherSettings(document);
        YamlUtility.Save(document, path);
    }

    private static void ValidateEditorSettings(EditorSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Format != "BEngine.EditorSettings" || document.Version != 1)
        {
            throw new InvalidDataException(
                $"Unsupported editor settings '{document.Format}' v{document.Version}.");
        }
    }

    private static void ValidateEditorPreferences(EditorPreferencesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Format != "BEngine.Preferences" || document.Version != 1)
        {
            throw new InvalidDataException(
                $"Unsupported editor preferences '{document.Format}' v{document.Version}.");
        }
        if (document.EditorFontSize <= 0)
            throw new InvalidDataException("Editor font size must be positive.");
    }

    private static void ValidateLauncherSettings(LauncherSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Format != "BEngine.LauncherSettings" || document.Version != 1)
        {
            throw new InvalidDataException(
                $"Unsupported launcher settings '{document.Format}' v{document.Version}.");
        }
        if (string.IsNullOrWhiteSpace(document.LastProjectDirectory) ||
            !Path.IsPathFullyQualified(document.LastProjectDirectory))
        {
            throw new InvalidDataException("The last project directory must be an absolute path.");
        }
    }

    private static void ValidateEditorLayout(EditorLayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Format != "BEngine.EditorLayout" || document.Version != 1)
        {
            throw new InvalidDataException(
                $"Unsupported editor layout '{document.Format}' v{document.Version}.");
        }
        if (document.WindowWidth < 640 || document.WindowHeight < 480)
            throw new InvalidDataException("The editor window size is too small.");
        if (!float.IsFinite(document.HierarchyWidth) || !float.IsFinite(document.InspectorWidth) ||
            !float.IsFinite(document.BottomHeight) || !float.IsFinite(document.ProjectFoldersWidth))
        {
            throw new InvalidDataException("Editor layout panel sizes must be finite numbers.");
        }

        float[] sceneCameraValues =
        [
            document.SceneCameraPositionX,
            document.SceneCameraPositionY,
            document.SceneCameraPositionZ,
            document.SceneCameraPitch,
            document.SceneCameraYaw,
            document.SceneCameraFieldOfView,
            document.SceneCameraNearClipPlane,
            document.SceneCameraFarClipPlane,
            document.SceneCameraMoveSpeed,
            document.SceneCameraFastMoveMultiplier,
            document.SceneCameraLookSensitivity,
            document.SceneCameraBackgroundR,
            document.SceneCameraBackgroundG,
            document.SceneCameraBackgroundB
        ];
        if (sceneCameraValues.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Scene camera settings must be finite numbers.");
        if (document.SceneCameraFieldOfView is < 10f or > 170f ||
            document.SceneCameraNearClipPlane < 0.001f ||
            document.SceneCameraFarClipPlane <= document.SceneCameraNearClipPlane ||
            document.SceneCameraMoveSpeed <= 0 || document.SceneCameraFastMoveMultiplier < 1 ||
            document.SceneCameraLookSensitivity <= 0)
        {
            throw new InvalidDataException("Scene camera settings are outside the supported range.");
        }
    }
}
