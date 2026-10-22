using BEngine.Editor.Documents;
using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class EditorPrefs
{
    private static readonly Lock Gate = new();
    private static string FilePath => EditorDataPaths.editorPrefsPath;
    private static FileUtility FileSystem => EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServiceAccess.GetRequiredService<PathUtility>();
    private static Dictionary<string, string>? _values;

    public static bool HasKey(string key) => Values.ContainsKey(key);
    public static void DeleteKey(string key) { lock (Gate) { Values.Remove(key); Save(); } }
    public static void DeleteAll() { lock (Gate) { Values.Clear(); Save(); } }
    public static string GetString(string key, string defaultValue = "") => Values.GetValueOrDefault(key, defaultValue);
    public static int GetInt(string key, int defaultValue = 0) =>
        int.TryParse(GetString(key), out var value) ? value : defaultValue;
    public static float GetFloat(string key, float defaultValue = 0) =>
        float.TryParse(GetString(key), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : defaultValue;
    public static bool GetBool(string key, bool defaultValue = false) =>
        bool.TryParse(GetString(key), out var value) ? value : defaultValue;
    public static void SetString(string key, string value) { lock (Gate) { Values[key] = value; Save(); } }
    public static void SetInt(string key, int value) => SetString(key, value.ToString(
        System.Globalization.CultureInfo.InvariantCulture));
    public static void SetFloat(string key, float value) => SetString(key, value.ToString(
        System.Globalization.CultureInfo.InvariantCulture));
    public static void SetBool(string key, bool value) => SetString(key, value.ToString());

    public static string GetProjectString(string projectRoot, string key, string defaultValue = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (Gate)
        {
            var values = LoadValues(EditorDataPaths.GetProjectEditorPrefsPath(projectRoot));
            return values.GetValueOrDefault(key, defaultValue);
        }
    }

    public static void SetProjectString(string projectRoot, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (Gate)
        {
            var path = EditorDataPaths.GetProjectEditorPrefsPath(projectRoot);
            var values = LoadValues(path);
            values[key] = value;
            FileSystem.WriteAllText(Paths.GetFullPath(path),
                YamlUtility.Serialize(new EditorPrefsDocument { Values = values }));
        }
    }

    private static Dictionary<string, string> Values
    {
        get
        {
            lock (Gate)
            {
                return _values ??= LoadValues(FilePath);
            }
        }
    }

    private static Dictionary<string, string> LoadValues(string path)
    {
        if (!FileSystem.FileExists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var values = YamlUtility.Load<EditorPrefsDocument>(path, FileSystem, Paths).Values;
            return new Dictionary<string, string>(values, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            Debug.LogWarning($"Could not load EditorPrefs: {exception.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void Save() => FileSystem.WriteAllText(
        Paths.GetFullPath(FilePath),
        YamlUtility.Serialize(new EditorPrefsDocument { Values = Values }));
}
