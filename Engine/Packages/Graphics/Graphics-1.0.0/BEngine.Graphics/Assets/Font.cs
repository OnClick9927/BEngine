namespace BEngine;

[EditorIcon("Icons/Assets/AssetFont.png")]
public sealed class Font : BAsset
{
    public int defaultSize { get; internal set; } = 16;
    public bool includeKerning { get; internal set; } = true;
    public string characterSet { get; internal set; } = "Dynamic";

    internal Font() { }
}
