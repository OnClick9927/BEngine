namespace BEngine.Editor;

public sealed class FontImporter : AssetImporter
{
    public int defaultSize { get; set; } = 16;
    public bool includeKerning { get; set; } = true;
    public string characterSet { get; set; } = "Dynamic";

    protected override void ReadSettings(IReadOnlyDictionary<string, string> settings)
    {
        base.ReadSettings(settings);
        defaultSize = Math.Clamp(Get(settings, nameof(defaultSize), 16), 1, 512);
        includeKerning = Get(settings, nameof(includeKerning), true);
        characterSet = Get(settings, nameof(characterSet), "Dynamic");
    }

    protected override void WriteSettings(IDictionary<string, string> settings)
    {
        base.WriteSettings(settings);
        Set(settings, nameof(defaultSize), Math.Clamp(defaultSize, 1, 512));
        Set(settings, nameof(includeKerning), includeKerning);
        Set(settings, nameof(characterSet), characterSet);
    }

    internal void ApplyTo(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        font.defaultSize = Math.Clamp(defaultSize, 1, 512);
        font.includeKerning = includeKerning;
        font.characterSet = characterSet;
    }
}
