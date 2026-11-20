using BEngine.Editor.Documents;
namespace BEngine.Serialization;
internal static class EditorPreferenceValidation
{
    internal static void ValidateEditorSettings(EditorSettingsDocument document)
    {
        if (document.Format != "BEngine.EditorSettings" || document.Version != 1)
            throw new InvalidDataException(
                $"Unsupported editor settings '{document.Format}' v{document.Version}.");
    }

    internal static void ValidateEditorPreferences(EditorPreferencesDocument document)
    {
        if (document.Format != "BEngine.Preferences" || document.Version != 1)
            throw new InvalidDataException(
                $"Unsupported editor preferences '{document.Format}' v{document.Version}.");
        // Legacy files may contain a different positive size. They are migrated to the
        // fixed editor font size when loaded, while malformed values remain invalid.
        if (document.EditorFontSize <= 0)
            throw new InvalidDataException("Editor font size must be positive.");
        if (!float.IsFinite(document.EditorScale) ||
            document.EditorScale < 0.5f ||
            document.EditorScale > 3f)
            throw new InvalidDataException(
                $"Editor scale must be between {0.5f:0.0} and " +
                $"{3f:0.0}.");
        if (string.IsNullOrWhiteSpace(document.Locale) || string.IsNullOrWhiteSpace(document.EditorFont) ||
            string.IsNullOrWhiteSpace(document.EditorTheme))
            throw new InvalidDataException("Editor locale, font, and theme cannot be empty.");
    }

}
