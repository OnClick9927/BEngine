namespace BEngine.Localization;

public interface ILocalizationTableProvider
{
    BValueTask<IReadOnlyList<LocalizationTable>> LoadAsync(string language, BCancellationToken cancellationToken);
}

public interface ILocalizationPreferenceRecorder
{
    string? ReadLanguage();
    void WriteLanguage(string language);
}

public sealed class PlayerPrefsLocalizationRecorder(string key = "BEngine.Localization.Language") : ILocalizationPreferenceRecorder
{
    public string? ReadLanguage() => PlayerPrefs.GetString(key);
    public void WriteLanguage(string language) { PlayerPrefs.SetString(key, language); PlayerPrefs.Save(); }
}

/// <summary>Paths are resolved through BAsset and the active AssetBundle provider, never direct runtime disk IO.</summary>
public sealed class AssetLocalizationTableProvider : ILocalizationTableProvider
{
    private readonly Dictionary<string, string[]> _paths = new(StringComparer.OrdinalIgnoreCase);

    public void SetPaths(string language, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var snapshot = paths.ToArray();
        if (snapshot.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("A table path is empty.", nameof(paths));
        _paths[LocalizationContext.NormalizeLanguage(language)] = snapshot;
    }

    public BValueTask<IReadOnlyList<LocalizationTable>> LoadAsync(string language, BCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_paths.TryGetValue(LocalizationContext.NormalizeLanguage(language), out var paths))
            return new BValueTask<IReadOnlyList<LocalizationTable>>([]);
        var tables = new List<LocalizationTable>(paths.Length);
        using var budget = LoopExecutionGuard.AllowIterations(paths.Length + 1);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tables.Add(BAsset.Load<LocalizationTable>(path) ?? throw new InvalidDataException($"Localization table not found: {path}"));
        }
        return new BValueTask<IReadOnlyList<LocalizationTable>>(tables);
    }
}
