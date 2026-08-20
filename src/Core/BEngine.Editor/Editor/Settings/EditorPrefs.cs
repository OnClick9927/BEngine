using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Documents;
using BEngine.Editor.Documents;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class EditorPrefs
{
    private static readonly object Gate = new();
    private static string FilePath => EditorDataPaths.editorPrefsPath;
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

    private static Dictionary<string, string> Values
    {
        get
        {
            lock (Gate)
            {
                if (_values is not null) return _values;
                try
                {
                    _values = File.Exists(FilePath)
                        ? Document.Load<EditorPrefsDocument>(FilePath).Values
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException)
                {
                    Debug.LogWarning($"Could not load EditorPrefs: {exception.Message}");
                    _values = new Dictionary<string, string>(StringComparer.Ordinal);
                }
                return _values;
            }
        }
    }

    private static void Save() => new EditorPrefsDocument { Values = Values }.Save(FilePath);
}
