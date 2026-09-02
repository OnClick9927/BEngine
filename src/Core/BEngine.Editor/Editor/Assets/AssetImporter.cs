using System.Globalization;
using BEngine.Documents;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public class AssetImporter : BObject
{
    private string _sourcePath = string.Empty;

    public string assetPath { get; internal set; } = string.Empty;
    public ulong assetTimeStamp => File.Exists(_sourcePath)
        ? unchecked((ulong)File.GetLastWriteTimeUtc(_sourcePath).Ticks)
        : 0;
    public string userData { get; set; } = string.Empty;

    /// <summary>
    /// Imports Source data into an engine Artifact. Custom importers can override this method;
    /// writes made through <paramref name="context"/> are published only after the method succeeds.
    /// </summary>
    public virtual void Import(AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CopySourceToArtifact();
    }

    public static AssetImporter? GetAtPath(string path)
    {
        var record = EditorBridge.Host?.GetAsset(path);
        if (record is null) return null;
        return CreateForImport(record.Value.AssetPath, record.Value.SourcePath,
            LoadSettings(record.Value.SourcePath));
    }

    public void SaveAndReimport()
    {
        EditorAssetWritePolicy.EnsureCanWrite("Saving asset import settings");
        if (string.IsNullOrWhiteSpace(_sourcePath))
            throw new InvalidOperationException("The importer is not bound to an asset.");
        var metaPath = _sourcePath + ".meta";
        var meta = File.Exists(metaPath)
            ? YamlUtility.Load<AssetMetaDocument>(metaPath)
            : new AssetMetaDocument
            {
                Guid = AssetDatabase.AssetPathToGUID(assetPath),
                AssetType = AssetTypeRegistry.Resolve(_sourcePath) ?? "DefaultAsset"
            };
        meta.Importer = GetType().Name;
        meta.Settings ??= [];
        WriteSettings(meta.Settings);
        meta.Save(metaPath);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
    }

    public void Revert()
    {
        if (string.IsNullOrWhiteSpace(_sourcePath)) return;
        ReadSettings(LoadSettings(_sourcePath));
    }

    protected virtual void ReadSettings(IReadOnlyDictionary<string, string> settings) =>
        userData = Get(settings, "userData", string.Empty);

    protected virtual void WriteSettings(IDictionary<string, string> settings) =>
        settings["userData"] = userData ?? string.Empty;

    protected static string Get(IReadOnlyDictionary<string, string> settings, string key, string fallback) =>
        settings.TryGetValue(key, out var value) ? value : fallback;

    protected static int Get(IReadOnlyDictionary<string, string> settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : fallback;

    protected static float Get(IReadOnlyDictionary<string, string> settings, string key, float fallback) =>
        settings.TryGetValue(key, out var value) &&
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        float.IsFinite(parsed)
            ? parsed : fallback;

    protected static bool Get(IReadOnlyDictionary<string, string> settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    protected static TEnum Get<TEnum>(IReadOnlyDictionary<string, string> settings, string key, TEnum fallback)
        where TEnum : struct, Enum => settings.TryGetValue(key, out var value) &&
                                     Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) &&
                                     Enum.IsDefined(parsed) ? parsed : fallback;

    protected static void Set(IDictionary<string, string> settings, string key, object value) =>
        settings[key] = value switch
        {
            bool boolean => boolean ? bool.TrueString : bool.FalseString,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? string.Empty
        };

    internal static AssetImporter CreateForImport(
        string projectPath,
        string sourcePath,
        IReadOnlyDictionary<string, string> settings)
    {
        var importerType = ResolveImporterType(sourcePath);
        if (!RuntimeTypeCache.TryCreateInstance(importerType, out var created) || created is not AssetImporter importer)
            throw new InvalidOperationException($"Could not create asset importer {importerType.FullName}.");
        importer.Initialize(projectPath, sourcePath, settings);
        return importer;
    }

    internal static Type ResolveImporterType(string sourcePath) =>
        AssetTypeRegistry.ResolveImporterType(sourcePath) ?? BuiltInImporterType(sourcePath);

    private void Initialize(
        string projectPath,
        string sourcePath,
        IReadOnlyDictionary<string, string> settings)
    {
        assetPath = projectPath;
        _sourcePath = sourcePath;
        name = Path.GetFileName(sourcePath);
        ReadSettings(settings);
    }

    private static IReadOnlyDictionary<string, string> LoadSettings(string sourcePath)
    {
        var metaPath = sourcePath + ".meta";
        if (!File.Exists(metaPath)) return new Dictionary<string, string>();
        try { return YamlUtility.Load<AssetMetaDocument>(metaPath).Settings ?? []; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
        {
            Debug.LogWarning($"Could not read import settings for {sourcePath}: {exception.Message}");
            return new Dictionary<string, string>();
        }
    }

    private static Type BuiltInImporterType(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.EndsWith(".prefab.yaml", StringComparison.OrdinalIgnoreCase))
            return typeof(DefaultImporter);
        return Path.GetExtension(normalized).ToLowerInvariant() switch
        {
            ".png" => typeof(TextureImporter),
            ".ttf" or ".otf" or ".woff" or ".woff2" => typeof(FontImporter),
            ".shader" or ".cg" => typeof(ShaderImporter),
            ".cs" => typeof(ScriptImporter),
            _ => typeof(DefaultImporter)
        };
    }
}
