namespace BEngine.Editor;

public sealed class EditorColorPickerWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public Color Original { get; internal set; } = Color.white;
    public Color Value { get; internal set; } = Color.white;
    public bool ShowAlpha { get; internal set; } = true;
    public bool Hdr { get; internal set; }
    public int Mode { get; internal set; } = 1;
    public string Hex { get; internal set; } = "FFFFFF";
    public bool DefaultsExpanded { get; internal set; } = true;
    public bool SwatchesExpanded { get; internal set; } = true;
    internal Action<Color>? Changed { get; set; }
}
