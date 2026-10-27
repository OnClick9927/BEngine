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
    private static FileUtility? _loadedFileSystem;
    private static PathUtility? _loadedPathService;
    private static string _loadedPath = string.Empty;
    private static IPlayerPrefsStorage? _loadedStorage;
    private static string _loadedCompanyName = string.Empty;
    private static string _loadedProductName = string.Empty;

    private static string FilePath => RuntimeServiceAccess.PathService.Combine(
        Application.persistentDataPath,
        "PlayerPrefs.yaml");
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
        if (_loadedStorage is { } storage)
        {
            storage.Write(_loadedCompanyName, _loadedProductName, contents);
            return;
        }
        var fileSystem = RuntimeServiceAccess.FileSystem;
        var paths = RuntimeServiceAccess.PathService;
        var fullPath = paths.GetFullPath(_loadedPath);
        var directory = paths.GetDirectoryName(fullPath) ?? RuntimeServiceAccess.PlatformService.CurrentDirectory;
        fileSystem.CreateDirectory(directory);
        var temporaryPath = paths.Combine(
            directory,
            $".{paths.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            fileSystem.WriteAllText(temporaryPath, contents);
            fileSystem.MoveFile(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (fileSystem.FileExists(temporaryPath)) fileSystem.DeleteFile(temporaryPath);
        }
    }

    private static Dictionary<string, string> Values
    {
        get
        {
            var fileSystem = RuntimeServiceAccess.FileSystem;
            var paths = RuntimeServiceAccess.PathService;
            var path = paths.GetFullPath(FilePath);
            var storage = RuntimeServiceAccess.GetService<IPlayerPrefsStorage>();
            var companyName = Application.companyName;
            var productName = Application.productName;
            if (!ReferenceEquals(_loadedFileSystem, fileSystem) ||
                !ReferenceEquals(_loadedPathService, paths) ||
                !_loadedPath.Equals(path, StringComparison.Ordinal) ||
                !ReferenceEquals(_loadedStorage, storage) ||
                !_loadedCompanyName.Equals(companyName, StringComparison.Ordinal) ||
                !_loadedProductName.Equals(productName, StringComparison.Ordinal))
            {
                _loadedFileSystem = fileSystem;
                _loadedPathService = paths;
                _loadedPath = path;
                _loadedStorage = storage;
                _loadedCompanyName = companyName;
                _loadedProductName = productName;
                _values = null;
            }
            if (_values is not null) return _values;
            try
            {
                var contents = storage is not null
                    ? storage.Read(companyName, productName)
                    : fileSystem.FileExists(path) ? fileSystem.ReadAllText(path) : null;
                _values = contents is not null
                    ? YamlUtility.Deserialize<PlayerPrefsData>(contents).Values ?? []
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidDataException or FormatException or
                                              YamlDotNet.Core.YamlException)
            {
                Debug.LogWarning($"Could not load PlayerPrefs: {exception.Message}");
                _values = [];
            }
            return _values;
        }
    }
}
