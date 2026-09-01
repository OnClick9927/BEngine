namespace BEngine;

internal sealed class PlayerPrefsData
{
    public string Format { get; set; } = "BEngine.PlayerPrefs";
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
}
