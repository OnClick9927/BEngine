namespace BEngine.UIElements;

public sealed partial class Style
{
    private string _fontAsset = "";
    private float _textOutlineWidth;
    private UIColor? _textOutlineColor;
    private TextDirectionMode _textDirection;
    private string _resolvedFontPath = "";
    private long _resolvedFontGeneration = -1;
    private WeakReference<SdfFontAsset>? _resolvedFont;
    /// <summary>Project path, resource path or GUID of a baked .sdfont. Empty inherits the parent.</summary>
    public string fontAsset { get => _fontAsset; set => Set(ref _fontAsset, (value ?? "").Replace('\\', '/')); }
    public float textOutlineWidth { get => _textOutlineWidth; set => Set(ref _textOutlineWidth, float.IsFinite(value) ? Math.Clamp(value, 0, 32) : 0); }
    public UIColor? textOutlineColor { get => _textOutlineColor; set => Set(ref _textOutlineColor, value); }
    /// <summary>Logical direction used by the advanced text shaper. Auto resolves each paragraph with Unicode bidi.</summary>
    public TextDirectionMode textDirection { get => _textDirection; set => Set(ref _textDirection, value); }
    internal static SdfFontAsset? ResolveFont(VisualElement? element)
    {
        for (var current = element; current is not null; current = current.parent)
            if (current.style.fontAsset.Length > 0)
                return current.style.ResolveOwnFont();
        return null;
    }
    private SdfFontAsset? ResolveOwnFont()
    {
        if (!Application.isEditor && _resolvedFontPath == _fontAsset && _resolvedFontGeneration == BAsset.cacheGeneration &&
            _resolvedFont?.TryGetTarget(out var cached) == true && !cached.isDestroyed) return cached;
        var font = BAsset.Load<SdfFontAsset>(_fontAsset) ?? Resources.Load<SdfFontAsset>(_fontAsset);
        _resolvedFontPath = _fontAsset;
        _resolvedFontGeneration = BAsset.cacheGeneration;
        _resolvedFont = font is null ? null : new(font);
        return font;
    }
    internal static TextDirectionMode ResolveTextDirection(VisualElement? element)
    {
        for (var current = element; current is not null; current = current.parent)
            if (current.style.IsInline(nameof(textDirection)) || current.style.textDirection != TextDirectionMode.Auto)
                return current.style.textDirection;
        return TextDirectionMode.Auto;
    }
    internal static string FontPath(string value)
    {
        value = value.Trim();
        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')')) value = value[4..^1].Trim();
        return value.Trim('\'', '"').Replace("%27", "'", StringComparison.OrdinalIgnoreCase).Replace('\\', '/');
    }
}
