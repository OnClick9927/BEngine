using BEngine.Documents;

namespace BEngine.Editor.Documents;

internal sealed class EditorPrefsDocument : Document
{
    public string Format { get; set; } = "BEngine.EditorPrefs";
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
}
