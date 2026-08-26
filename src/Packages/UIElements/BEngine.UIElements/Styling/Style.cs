using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public sealed class Style
{
    private readonly Action _changed;
    private readonly HashSet<string> _inlineProperties = new(StringComparer.Ordinal);
    private bool _applyingStyleSheet;
    private FlexDirection _flexDirection = FlexDirection.Column;
    private DisplayStyle _display = DisplayStyle.Flex;
    private Align _alignItems = Align.Stretch;
    private Justify _justifyContent;
    private Overflow _overflow = Overflow.Visible;
    private Position _position;
    private float _left = float.NaN;
    private float _top = float.NaN;
    private float _right = float.NaN;
    private float _bottom = float.NaN;
    private float _width;
    private float _height;
    private float _minWidth;
    private float _minHeight;
    private float _maxWidth = float.PositiveInfinity;
    private float _maxHeight = float.PositiveInfinity;
    private float _flexGrow;
    private float _flexShrink = 1;
    private float _marginLeft;
    private float _marginTop;
    private float _marginRight;
    private float _marginBottom;
    private float _paddingLeft;
    private float _paddingTop;
    private float _paddingRight;
    private float _paddingBottom;
    private float _fontSize;
    private float _borderWidth;
    private UIColor? _color;
    private UIColor? _backgroundColor;
    private UIColor? _borderColor;

    internal Style(Action changed) => _changed = changed;

    public FlexDirection flexDirection { get => _flexDirection; set => Set(ref _flexDirection, value); }
    public DisplayStyle display { get => _display; set => Set(ref _display, value); }
    public Align alignItems { get => _alignItems; set => Set(ref _alignItems, value); }
    public Justify justifyContent { get => _justifyContent; set => Set(ref _justifyContent, value); }
    public Overflow overflow { get => _overflow; set => Set(ref _overflow, value); }
    public Position position { get => _position; set => Set(ref _position, value); }
    public float left { get => _left; set => Set(ref _left, value); }
    public float top { get => _top; set => Set(ref _top, value); }
    public float right { get => _right; set => Set(ref _right, value); }
    public float bottom { get => _bottom; set => Set(ref _bottom, value); }
    public float width { get => _width; set => Set(ref _width, value); }
    public float height { get => _height; set => Set(ref _height, value); }
    public float minWidth { get => _minWidth; set => Set(ref _minWidth, value); }
    public float minHeight { get => _minHeight; set => Set(ref _minHeight, value); }
    public float maxWidth { get => _maxWidth; set => Set(ref _maxWidth, value); }
    public float maxHeight { get => _maxHeight; set => Set(ref _maxHeight, value); }
    public float flexGrow { get => _flexGrow; set => Set(ref _flexGrow, value); }
    public float flexShrink { get => _flexShrink; set => Set(ref _flexShrink, value); }
    public float marginLeft { get => _marginLeft; set => Set(ref _marginLeft, value); }
    public float marginTop { get => _marginTop; set => Set(ref _marginTop, value); }
    public float marginRight { get => _marginRight; set => Set(ref _marginRight, value); }
    public float marginBottom { get => _marginBottom; set => Set(ref _marginBottom, value); }
    public float paddingLeft { get => _paddingLeft; set => Set(ref _paddingLeft, value); }
    public float paddingTop { get => _paddingTop; set => Set(ref _paddingTop, value); }
    public float paddingRight { get => _paddingRight; set => Set(ref _paddingRight, value); }
    public float paddingBottom { get => _paddingBottom; set => Set(ref _paddingBottom, value); }
    public float fontSize { get => _fontSize; set => Set(ref _fontSize, value); }
    public float borderWidth { get => _borderWidth; set => Set(ref _borderWidth, Math.Max(0, value)); }
    public UIColor? color { get => _color; set => Set(ref _color, value); }
    public UIColor? backgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public UIColor? borderColor { get => _borderColor; set => Set(ref _borderColor, value); }

    public void SetMargin(float all) => SetMargin(all, all, all, all);

    public void SetMargin(float left, float top, float right, float bottom)
    {
        Set(ref _marginLeft, left, nameof(marginLeft));
        Set(ref _marginTop, top, nameof(marginTop));
        Set(ref _marginRight, right, nameof(marginRight));
        Set(ref _marginBottom, bottom, nameof(marginBottom));
    }

    public void SetPadding(float all) => SetPadding(all, all, all, all);

    public void SetPadding(float left, float top, float right, float bottom)
    {
        Set(ref _paddingLeft, left, nameof(paddingLeft));
        Set(ref _paddingTop, top, nameof(paddingTop));
        Set(ref _paddingRight, right, nameof(paddingRight));
        Set(ref _paddingBottom, bottom, nameof(paddingBottom));
    }

    internal void ApplyFromStyleSheet(Action apply)
    {
        var previous = _applyingStyleSheet;
        _applyingStyleSheet = true;
        try { apply(); }
        finally { _applyingStyleSheet = previous; }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (_applyingStyleSheet && _inlineProperties.Contains(propertyName)) return;
        if (!_applyingStyleSheet) _inlineProperties.Add(propertyName);
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        _changed();
    }
}
