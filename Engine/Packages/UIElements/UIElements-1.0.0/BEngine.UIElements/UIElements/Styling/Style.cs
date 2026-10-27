using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

/// <summary>Deterministic Q32.32 layout and paint values. Nullable offsets mean auto; nullable maxima mean unbounded.</summary>
public sealed partial class Style
{
    private readonly Action _changed;
    private readonly Action _repaint;
    private readonly Action _textMetricsChanged;
    private readonly HashSet<string> _inlineProperties = new(StringComparer.Ordinal);
    private bool _applyingStyleSheet;
    private FlexDirection _flexDirection = FlexDirection.Column;
    private DisplayStyle _display = DisplayStyle.Flex;
    private Align _alignItems = Align.Stretch;
    private Justify _justifyContent;
    private Overflow _overflow = Overflow.Visible;
    private Position _position;
    private Fix64? _left;
    private Fix64? _top;
    private Fix64? _right;
    private Fix64? _bottom;
    private Fix64 _width;
    private Fix64 _height;
    private Fix64 _minWidth;
    private Fix64 _minHeight;
    private Fix64? _maxWidth;
    private Fix64? _maxHeight;
    private Fix64 _flexGrow;
    private Fix64 _flexShrink = 1;
    private Fix64 _marginLeft;
    private Fix64 _marginTop;
    private Fix64 _marginRight;
    private Fix64 _marginBottom;
    private Fix64 _paddingLeft;
    private Fix64 _paddingTop;
    private Fix64 _paddingRight;
    private Fix64 _paddingBottom;
    private Fix64 _fontSize;
    private Fix64 _borderWidth;
    private UIColor? _color;
    private UIColor? _backgroundColor;
    private UIColor? _borderColor;

    internal Style(Action changed, Action? repaint = null, Action? textMetricsChanged = null)
    { _changed = changed; _repaint = repaint ?? changed; _textMetricsChanged = textMetricsChanged ?? changed; }

    public FlexDirection flexDirection { get => _flexDirection; set => Set(ref _flexDirection, value); }
    public DisplayStyle display { get => _display; set => Set(ref _display, value); }
    public Align alignItems { get => _alignItems; set => Set(ref _alignItems, value); }
    public Justify justifyContent { get => _justifyContent; set => Set(ref _justifyContent, value); }
    public Overflow overflow { get => _overflow; set => Set(ref _overflow, value); }
    public Position position { get => _position; set => Set(ref _position, value); }
    public Fix64? left { get => _left; set => Set(ref _left, value); }
    public Fix64? top { get => _top; set => Set(ref _top, value); }
    public Fix64? right { get => _right; set => Set(ref _right, value); }
    public Fix64? bottom { get => _bottom; set => Set(ref _bottom, value); }
    public Fix64 width { get => _width; set => Set(ref _width, value); }
    public Fix64 height { get => _height; set => Set(ref _height, value); }
    public Fix64 minWidth { get => _minWidth; set => Set(ref _minWidth, value); }
    public Fix64 minHeight { get => _minHeight; set => Set(ref _minHeight, value); }
    public Fix64? maxWidth { get => _maxWidth; set => Set(ref _maxWidth, value); }
    public Fix64? maxHeight { get => _maxHeight; set => Set(ref _maxHeight, value); }
    public Fix64 flexGrow { get => _flexGrow; set => Set(ref _flexGrow, value); }
    public Fix64 flexShrink { get => _flexShrink; set => Set(ref _flexShrink, value); }
    public Fix64 marginLeft { get => _marginLeft; set => Set(ref _marginLeft, value); }
    public Fix64 marginTop { get => _marginTop; set => Set(ref _marginTop, value); }
    public Fix64 marginRight { get => _marginRight; set => Set(ref _marginRight, value); }
    public Fix64 marginBottom { get => _marginBottom; set => Set(ref _marginBottom, value); }
    public Fix64 paddingLeft { get => _paddingLeft; set => Set(ref _paddingLeft, value); }
    public Fix64 paddingTop { get => _paddingTop; set => Set(ref _paddingTop, value); }
    public Fix64 paddingRight { get => _paddingRight; set => Set(ref _paddingRight, value); }
    public Fix64 paddingBottom { get => _paddingBottom; set => Set(ref _paddingBottom, value); }
    public Fix64 fontSize { get => _fontSize; set => Set(ref _fontSize, value); }
    public Fix64 borderWidth { get => _borderWidth; set => Set(ref _borderWidth, Fix64.Max(0, value)); }
    public UIColor? color { get => _color; set => Set(ref _color, value); }
    public UIColor? backgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public UIColor? borderColor { get => _borderColor; set => Set(ref _borderColor, value); }

    public void SetMargin(Fix64 all) => SetMargin(all, all, all, all);

    public void SetMargin(Fix64 left, Fix64 top, Fix64 right, Fix64 bottom)
    {
        Set(ref _marginLeft, left, nameof(marginLeft));
        Set(ref _marginTop, top, nameof(marginTop));
        Set(ref _marginRight, right, nameof(marginRight));
        Set(ref _marginBottom, bottom, nameof(marginBottom));
    }

    public void SetPadding(Fix64 all) => SetPadding(all, all, all, all);

    public void SetPadding(Fix64 left, Fix64 top, Fix64 right, Fix64 bottom)
    {
        Set(ref _paddingLeft, left, nameof(paddingLeft));
        Set(ref _paddingTop, top, nameof(paddingTop));
        Set(ref _paddingRight, right, nameof(paddingRight));
        Set(ref _paddingBottom, bottom, nameof(paddingBottom));
    }

    internal void ApplyFromStyleSheet(Action apply)
    {
        using var scope = BeginStyleSheetApplication(true);
        apply();
    }

    // A value scope keeps the same nested/exception restoration as the Action
    // API without requiring a captured delegate for every matched rule.
    internal StyleSheetApplication BeginStyleSheetApplication(bool fromStyleSheet) => new(this, fromStyleSheet);

    internal readonly struct StyleSheetApplication : IDisposable
    {
        private readonly Style _style;
        private readonly bool _restore, _previous;
        internal StyleSheetApplication(Style style, bool fromStyleSheet)
        {
            _style = style;
            _restore = fromStyleSheet;
            _previous = style._applyingStyleSheet;
            if (fromStyleSheet) style._applyingStyleSheet = true;
        }
        public void Dispose()
        {
            if (_restore) _style._applyingStyleSheet = _previous;
        }
    }

    internal bool IsInline(string propertyName) => _inlineProperties.Contains(propertyName);

    internal void CopyStateFrom(Style source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _flexDirection = source._flexDirection;
        _display = source._display;
        _alignItems = source._alignItems;
        _justifyContent = source._justifyContent;
        _overflow = source._overflow;
        _position = source._position;
        _left = source._left;
        _top = source._top;
        _right = source._right;
        _bottom = source._bottom;
        _width = source._width;
        _height = source._height;
        _minWidth = source._minWidth;
        _minHeight = source._minHeight;
        _maxWidth = source._maxWidth;
        _maxHeight = source._maxHeight;
        _flexGrow = source._flexGrow;
        _flexShrink = source._flexShrink;
        _marginLeft = source._marginLeft;
        _marginTop = source._marginTop;
        _marginRight = source._marginRight;
        _marginBottom = source._marginBottom;
        _paddingLeft = source._paddingLeft;
        _paddingTop = source._paddingTop;
        _paddingRight = source._paddingRight;
        _paddingBottom = source._paddingBottom;
        _fontSize = source._fontSize;
        _borderWidth = source._borderWidth;
        _color = source._color;
        _backgroundColor = source._backgroundColor;
        _borderColor = source._borderColor;
        CopyCssState(source);
        _inlineProperties.Clear();
        _inlineProperties.UnionWith(source._inlineProperties);
        _applyingStyleSheet = false;
        _changed();
    }

    internal void ResetStyleSheetValues()
    {
        ResetCssState();
        var changed = false;
        void Reset<T>(ref T field, T fallback, string propertyName)
        {
            if (_inlineProperties.Contains(propertyName) || EqualityComparer<T>.Default.Equals(field, fallback))
                return;
            field = fallback;
            changed = true;
        }

        Reset(ref _flexDirection, FlexDirection.Column, nameof(flexDirection));
        Reset(ref _display, DisplayStyle.Flex, nameof(display));
        Reset(ref _alignItems, Align.Stretch, nameof(alignItems));
        Reset(ref _justifyContent, Justify.FlexStart, nameof(justifyContent));
        Reset(ref _overflow, Overflow.Visible, nameof(overflow));
        Reset(ref _position, Position.Relative, nameof(position));
        Reset<Fix64?>(ref _left, null, nameof(left));
        Reset<Fix64?>(ref _top, null, nameof(top));
        Reset<Fix64?>(ref _right, null, nameof(right));
        Reset<Fix64?>(ref _bottom, null, nameof(bottom));
        Reset(ref _width, Fix64.Zero, nameof(width));
        Reset(ref _height, Fix64.Zero, nameof(height));
        Reset(ref _minWidth, Fix64.Zero, nameof(minWidth));
        Reset(ref _minHeight, Fix64.Zero, nameof(minHeight));
        Reset<Fix64?>(ref _maxWidth, null, nameof(maxWidth));
        Reset<Fix64?>(ref _maxHeight, null, nameof(maxHeight));
        Reset(ref _flexGrow, Fix64.Zero, nameof(flexGrow));
        Reset(ref _flexShrink, Fix64.One, nameof(flexShrink));
        Reset(ref _marginLeft, Fix64.Zero, nameof(marginLeft));
        Reset(ref _marginTop, Fix64.Zero, nameof(marginTop));
        Reset(ref _marginRight, Fix64.Zero, nameof(marginRight));
        Reset(ref _marginBottom, Fix64.Zero, nameof(marginBottom));
        Reset(ref _paddingLeft, Fix64.Zero, nameof(paddingLeft));
        Reset(ref _paddingTop, Fix64.Zero, nameof(paddingTop));
        Reset(ref _paddingRight, Fix64.Zero, nameof(paddingRight));
        Reset(ref _paddingBottom, Fix64.Zero, nameof(paddingBottom));
        Reset(ref _fontSize, Fix64.Zero, nameof(fontSize));
        Reset(ref _borderWidth, Fix64.Zero, nameof(borderWidth));
        Reset(ref _color, null, nameof(color));
        Reset(ref _backgroundColor, null, nameof(backgroundColor));
        Reset(ref _borderColor, null, nameof(borderColor));
        if (changed) _changed();
    }

    internal void ResetInteractionPaintValues()
    {
        if (!_inlineProperties.Contains(nameof(color))) _color = null;
        if (!_inlineProperties.Contains(nameof(backgroundColor))) _backgroundColor = null;
        if (!_inlineProperties.Contains(nameof(borderColor))) _borderColor = null;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (!_resolvingLengths)
        {
            if (_applyingStyleSheet && _inlineProperties.Contains(propertyName)) return;
            if (!_applyingStyleSheet) _inlineProperties.Add(propertyName);
            _lengths.Remove(propertyName);
        }
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        if (propertyName is nameof(color) or nameof(backgroundColor) or nameof(borderColor) or
            nameof(borderWidth) or nameof(textOutlineColor) or nameof(textOutlineWidth)) _repaint();
        else if (propertyName == nameof(fontSize)) _textMetricsChanged();
        else _changed();
    }
}
