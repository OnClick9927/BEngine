namespace BEngine.Localization;

/// <summary>One language per asset, so a provider need only load the requested language and its fallbacks.</summary>
[CreateAssetMenu(fileName = "Localization Table", menuName = "Localization/Table", order = 400)]
public sealed class LocalizationTable : ScriptableObject
{
    public string language { get; set; } = "en";
    public List<LocalizationEntry> entries { get; set; } = [];

    public void EnsureCanEdit() => EnsureAssetCanBeSaved();

    public void Validate()
    {
        _ = LocalizationContext.NormalizeLanguage(language);
        if (entries is null || entries.Count > 1_000_000) throw new InvalidDataException("A localization table is limited to one million entries.");
        using var budget = LoopExecutionGuard.AllowIterations(entries.Count + 1);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.key))
                throw new InvalidDataException("Localization keys must not be empty.");
            if (!keys.Add(entry.key)) throw new InvalidDataException($"Duplicate localization key '{entry.key}'.");
            if (entry.text is null && entry.asset is null)
                throw new InvalidDataException($"Localization key '{entry.key}' has neither text nor an asset.");
        }
    }
}

public sealed class LocalizationEntry
{
    public string key { get; set; } = string.Empty;
    /// <summary>Null means no text translation; an empty string is an intentional translation.</summary>
    public string? text { get; set; }
    public BObject? asset { get; set; }
    public string comment { get; set; } = string.Empty;
}
