namespace BEngine;

public static class PlayerPrefs
{
    private static Dictionary<string, string>? _values;
    private static string FilePath => Path.Combine(Application.persistentDataPath, "PlayerPrefs.yaml");
    public static bool HasKey(string key) => Values.ContainsKey(key);
    public static void DeleteKey(string key) => Values.Remove(key);
    public static void DeleteAll() => Values.Clear();
    public static string GetString(string key, string defaultValue = "") => Values.GetValueOrDefault(key, defaultValue);
    public static int GetInt(string key, int defaultValue = 0) => int.TryParse(GetString(key), out var value) ? value : defaultValue;
    public static Fix64 GetFloat(string key, Fix64 defaultValue = default) =>
        Fix64.TryParse(GetString(key), out var value) ? value : defaultValue;
    public static void SetString(string key, string value) => Values[key] = value ?? string.Empty;
    public static void SetInt(string key, int value) => SetString(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static void SetFloat(string key, Fix64 value) => SetString(key, value.ToString());
    public static void Save() => YamlUtility.Save(new PlayerPrefsData { Values = Values }, FilePath);

    private static Dictionary<string, string> Values
    {
        get
        {
            if (_values is not null) return _values;
            try { _values = File.Exists(FilePath) ? YamlUtility.Load<PlayerPrefsData>(FilePath).Values : []; }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                Debug.LogWarning($"Could not load PlayerPrefs: {exception.Message}");
                _values = [];
            }
            return _values;
        }
    }
}
