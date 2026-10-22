using System.Globalization;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.Editor.Documents;
using BEngine.Platform;

namespace BEngine.Editor;

public static class EditorPreferences
{
    private static EditorPreferencesDocument _current = new();
    private static bool _initialized;
    private static FileUtility FileSystem => EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServiceAccess.GetRequiredService<PathUtility>();
    public static EditorPreferencesDocument current => _current;
    public static event Action? preferencesChanged;

    public static void Initialize()
    {
        if (_initialized) return;
        try
        {
            _current = FileSystem.FileExists(EditorDataPaths.preferencesPath)
                ? YamlUtility.Load<EditorPreferencesDocument>(
                    EditorDataPaths.preferencesPath, FileSystem, Paths)
                : new EditorPreferencesDocument();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Preferences could not be loaded; defaults are active: {exception.Message}");
            _current = new EditorPreferencesDocument();
        }
        _current.EditorFontSize = EditorAppearance.DefaultFontSize;
        _current.EditorSkin ??= string.Empty;
        var skinBeforeApply = _current.EditorSkin;
        EditorAppearance.Apply(_current);
        if (!skinBeforeApply.Equals(_current.EditorSkin, StringComparison.Ordinal))
        {
            try { SaveDocument(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"Normalized editor skin preference could not be saved: {exception.Message}");
            }
        }
        _initialized = true;
    }

    public static void Save()
    {
        _current.EditorFontSize = EditorAppearance.DefaultFontSize;
        _current.EditorSkin ??= string.Empty;
        SaveDocument();
        EditorAppearance.Apply(_current);
        EditorCallbackDispatcher.Invoke(preferencesChanged, nameof(preferencesChanged));
    }

    public static void ResetToDefaults()
    {
        _current = new EditorPreferencesDocument();
        Save();
    }

    private static void SaveDocument() => FileSystem.WriteAllText(
        Paths.GetFullPath(EditorDataPaths.preferencesPath),
        YamlUtility.Serialize(_current));
}
