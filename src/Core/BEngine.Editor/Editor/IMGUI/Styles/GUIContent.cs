namespace BEngine.Editor;

public class GUIContent
{
    [ThreadStatic] private static GUIContent? _temporary;

    public string text { get; set; }
    public string image { get; set; }
    public string tooltip { get; set; }

    public GUIContent(string text = "", string tooltip = "")
    {
        this.text = text ?? string.Empty;
        image = string.Empty;
        this.tooltip = tooltip ?? string.Empty;
    }

    public GUIContent(string text, string image, string tooltip)
    {
        this.text = text ?? string.Empty;
        this.image = image ?? string.Empty;
        this.tooltip = tooltip ?? string.Empty;
    }

    public GUIContent(GUIContent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        text = source.text; image = source.image; tooltip = source.tooltip;
    }

    public static implicit operator GUIContent(string text) => new(text);
    public static GUIContent none { get; } = new();

    internal static GUIContent Temp(string text)
    {
        var content = _temporary ??= new GUIContent();
        content.text = text ?? string.Empty;
        content.image = string.Empty;
        content.tooltip = string.Empty;
        return content;
    }

    internal static void ClearTemporary()
    {
        if (_temporary is null) return;
        _temporary.text = string.Empty;
        _temporary.image = string.Empty;
        _temporary.tooltip = string.Empty;
    }
}
