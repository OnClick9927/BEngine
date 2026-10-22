namespace BEngine.Localization.Editor;

/// <summary>Projects supply credentials and transport; the engine does not send text to a service automatically.</summary>
public interface ILocalizationTranslator
{
    BValueTask<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, BCancellationToken cancellationToken);
}

public static class LocalizationTranslation
{
    /// <summary>Builds a separate review table. Source and target assets remain untouched.</summary>
    public static async BValueTask<LocalizationTable> CreateMissingTranslationsAsync(LocalizationTable source,
        LocalizationTable target, ILocalizationTranslator translator, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(translator);
        cancellationToken.ThrowIfCancellationRequested();
        source.Validate(); target.Validate();
        var sourceLanguage = LocalizationContext.NormalizeLanguage(source.language);
        var targetLanguage = LocalizationContext.NormalizeLanguage(target.language);
        var existing = target.entries.Where(entry => entry.text is not null).Select(entry => entry.key).ToHashSet(StringComparer.Ordinal);
        var missing = source.entries.Where(entry => entry.text is not null && !existing.Contains(entry.key))
            .Select(entry => (entry.key, entry.text, entry.comment)).ToArray();
        var entries = new List<LocalizationEntry>();
        foreach (var entry in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var translated = await translator.TranslateAsync(entry.text!, sourceLanguage, targetLanguage, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (translated is null) throw new InvalidDataException($"Translator returned no text for '{entry.key}'.");
            entries.Add(new LocalizationEntry { key = entry.key, text = translated, comment = entry.comment });
        }
        return new LocalizationTable { language = targetLanguage, entries = entries };
    }
}
