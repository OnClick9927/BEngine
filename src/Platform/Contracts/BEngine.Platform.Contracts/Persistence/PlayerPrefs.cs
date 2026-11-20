using BEngine.Platform;

namespace BEngine;

internal sealed class PlayerPrefsData
{
    public string Format { get; set; } = "BEngine.PlayerPrefs";
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
}

public static class PlayerPrefs
{
    private static Dictionary<string, string>? _values;
    private static IPlayerPrefsStorage? _loadedStorage;
    private static object? _loadedIdentity;
    private static PlayerPrefsScope _loadedScope;

    public static bool HasKey(string key) => Values.ContainsKey(key);
    public static void DeleteKey(string key) => Values.Remove(key);
    public static void DeleteAll() => Values.Clear();
    public static string GetString(string key, string defaultValue = "") => Values.GetValueOrDefault(key, defaultValue);
    public static int GetInt(string key, int defaultValue = 0) =>
        int.TryParse(GetString(key), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : defaultValue;
    public static Fix64 GetFloat(string key, Fix64 defaultValue = default) =>
        Fix64.TryParse(GetString(key), out var value) ? value : defaultValue;
    public static void SetString(string key, string value) => Values[key] = value ?? string.Empty;
    public static void SetInt(string key, int value) => SetString(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static void SetFloat(string key, Fix64 value) => SetString(key, value.ToString());
    public static void Save()
    {
        var values = Values;
        var contents = YamlUtility.Serialize(new PlayerPrefsData { Values = values });
        _loadedStorage!.Write(_loadedScope, contents);
    }

    private static Dictionary<string, string> Values
    {
        get
        {
            var storage = RuntimeServices.GetRequiredService<IPlayerPrefsStorage>();
            var identity = storage.Identity;
            var scope = new PlayerPrefsScope(Application.companyName, Application.productName, Application.persistentDataPath);
            if (!ReferenceEquals(_loadedStorage, storage) || !ReferenceEquals(_loadedIdentity, identity) || _loadedScope != scope)
            {
                _loadedStorage = storage;
                _loadedIdentity = identity;
                _loadedScope = scope;
                _values = null;
            }
            if (_values is not null) return _values;
            try
            {
                var contents = storage.Read(scope);
                _values = contents is not null
                    ? YamlUtility.Deserialize<PlayerPrefsData>(contents).Values ?? []
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidDataException or FormatException)
            {
                Debug.LogWarning($"Could not load PlayerPrefs: {exception.Message}");
                _values = [];
            }
            return _values;
        }
    }
}
