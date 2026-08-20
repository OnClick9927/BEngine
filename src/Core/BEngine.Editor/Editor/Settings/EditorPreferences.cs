using System.Globalization;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

public static class EditorPreferences
{
    private static EditorPreferencesDocument _current = new();
    public static EditorPreferencesDocument current => _current;
    public static event Action? preferencesChanged;

    public static void Initialize()
    {
        try
        {
            _current = File.Exists(EditorDataPaths.preferencesPath)
                ? Document.Load<EditorPreferencesDocument>(EditorDataPaths.preferencesPath)
                : new EditorPreferencesDocument();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Preferences could not be loaded; defaults are active: {exception.Message}");
            _current = new EditorPreferencesDocument();
        }
        EditorAppearance.Apply(_current);
    }

    public static void Save()
    {
        _current.Save(EditorDataPaths.preferencesPath);
        EditorAppearance.Apply(_current);
        EditorCallbackDispatcher.Invoke(preferencesChanged, nameof(preferencesChanged));
    }

    public static void ResetToDefaults()
    {
        _current = new EditorPreferencesDocument();
        Save();
    }
}
