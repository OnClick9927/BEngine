namespace BEngine.UIElements;

public sealed partial class Style
{
    private readonly Dictionary<string, string> _lengths = new(StringComparer.Ordinal);
    private bool _resolvingLengths;
    private Fix64 _rowGap, _columnGap, _translateX, _translateY;
    private string _textAlign = "";
    public Fix64 rowGap { get => _rowGap; set => Set(ref _rowGap, value); }
    public Fix64 columnGap { get => _columnGap; set => Set(ref _columnGap, value); }
    public Fix64 translateX { get => _translateX; set => Set(ref _translateX, value); }
    public Fix64 translateY { get => _translateY; set => Set(ref _translateY, value); }
    public string textAlign { get => _textAlign; set => Set(ref _textAlign, value); }
    internal IReadOnlyDictionary<string, string> lengthExpressions => _lengths;
    internal bool hasContextDependentLengths
    {
        get
        {
            foreach (var expression in _lengths.Values)
                if (CssLength.DependsOnContainerOrFont(expression)) return true;
            return false;
        }
    }
    internal Dictionary<string, string> ExportCssDeclarations()
    {
        var result = new Dictionary<string, string>();
        string Number(Fix64 value) => value.ToString(null, System.Globalization.CultureInfo.InvariantCulture) + "px";
        if (IsInline(nameof(left)) && left is null) result["left"] = "auto";
        if (IsInline(nameof(top)) && top is null) result["top"] = "auto";
        if (IsInline(nameof(right)) && right is null) result["right"] = "auto";
        if (IsInline(nameof(bottom)) && bottom is null) result["bottom"] = "auto";
        if (IsInline(nameof(maxWidth)) && maxWidth is null) result["max-width"] = "none";
        if (IsInline(nameof(maxHeight)) && maxHeight is null) result["max-height"] = "none";
        if (IsInline(nameof(rowGap))) result["row-gap"] = Number(rowGap);
        if (IsInline(nameof(columnGap))) result["column-gap"] = Number(columnGap);
        if (IsInline(nameof(textAlign))) result["text-align"] = textAlign;
        if (IsInline(nameof(fontAsset))) result["font-asset"] = $"url('{fontAsset.Replace("'", "%27", StringComparison.Ordinal)}')";
        if (IsInline(nameof(textOutlineWidth))) result["text-outline-width"] = Number(textOutlineWidth);
        if (IsInline(nameof(textOutlineColor)) && textOutlineColor is { } outline) result["text-outline-color"] = $"#{outline.R:X2}{outline.G:X2}{outline.B:X2}{outline.A:X2}";
        if (IsInline(nameof(textDirection))) result["text-direction"] = textDirection switch { TextDirectionMode.LeftToRight => "ltr", TextDirectionMode.RightToLeft => "rtl", _ => "auto" };
        foreach (var (property, expression) in _lengths)
            if (IsInline(property) && property is not (nameof(translateX) or nameof(translateY)))
                result[System.Text.RegularExpressions.Regex.Replace(property, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant()] = expression;
        if (IsInline(nameof(translateX)) || IsInline(nameof(translateY)))
            result["transform"] = $"translate({_lengths.GetValueOrDefault(nameof(translateX), Number(translateX))}, {_lengths.GetValueOrDefault(nameof(translateY), Number(translateY))})";
        return result;
    }

    internal void SetLength(string property, string expression, Fix64? fallback = null)
    {
        if (_applyingStyleSheet && _inlineProperties.Contains(property)) return;
        var hasValue = CssLength.TryResolve(expression, 0, 0, 0, 16, out var resolved);
        var value = hasValue ? resolved : fallback ?? Fix64.Zero;
        // Use the normal setters: callbacks may reenter stylesheet application
        // or throw, and storing the expression must still happen afterwards.
        switch (property)
        {
            case nameof(rowGap): rowGap = value; break;
            case nameof(columnGap): columnGap = value; break;
            case nameof(translateX): translateX = value; break;
            case nameof(translateY): translateY = value; break;
            case nameof(left): left = hasValue ? value : fallback; break;
            case nameof(top): top = hasValue ? value : fallback; break;
            case nameof(right): right = hasValue ? value : fallback; break;
            case nameof(bottom): bottom = hasValue ? value : fallback; break;
            case nameof(width): width = value; break;
            case nameof(height): height = value; break;
            case nameof(minWidth): minWidth = value; break;
            case nameof(minHeight): minHeight = value; break;
            case nameof(maxWidth): maxWidth = hasValue ? value : fallback; break;
            case nameof(maxHeight): maxHeight = hasValue ? value : fallback; break;
            case nameof(fontSize): fontSize = value; break;
            case nameof(marginLeft): marginLeft = value; break;
            case nameof(marginTop): marginTop = value; break;
            case nameof(marginRight): marginRight = value; break;
            case nameof(marginBottom): marginBottom = value; break;
            case nameof(paddingLeft): paddingLeft = value; break;
            case nameof(paddingTop): paddingTop = value; break;
            case nameof(paddingRight): paddingRight = value; break;
            case nameof(paddingBottom): paddingBottom = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(property));
        }
        if (hasValue) _lengths[property] = expression;
    }

    internal void ResolveLengths(Fix64 parentWidth, Fix64 parentHeight, Fix64 vw, Fix64 vh, Fix64 font)
    {
        _resolvingLengths = true;
        try
        {
            // font-size is relative to the inherited font; other em lengths use
            // this element's resolved font, independent of declaration order.
            if (_lengths.TryGetValue(nameof(fontSize), out var fontExpression) &&
                CssLength.TryResolve(fontExpression, font, vw, vh, font, out var resolvedFont))
                _fontSize = resolvedFont;
            var effectiveFont = _fontSize > 0 ? _fontSize : font;
            foreach (var (property, expression) in _lengths)
            {
                if (property is nameof(translateX) or nameof(translateY) or nameof(fontSize)) continue;
                var vertical = property is nameof(height) or nameof(minHeight) or nameof(maxHeight) or
                    nameof(top) or nameof(bottom) or nameof(rowGap);
                if (!CssLength.TryResolve(expression, vertical ? parentHeight : parentWidth, vw, vh, effectiveFont, out var value)) continue;
                switch (property)
                {
                    case nameof(width): _width = value; break;
                    case nameof(height): _height = value; break;
                    case nameof(minWidth): _minWidth = value; break;
                    case nameof(minHeight): _minHeight = value; break;
                    case nameof(maxWidth): _maxWidth = value; break;
                    case nameof(maxHeight): _maxHeight = value; break;
                    case nameof(left): _left = value; break;
                    case nameof(top): _top = value; break;
                    case nameof(right): _right = value; break;
                    case nameof(bottom): _bottom = value; break;
                    case nameof(marginLeft): _marginLeft = value; break;
                    case nameof(marginTop): _marginTop = value; break;
                    case nameof(marginRight): _marginRight = value; break;
                    case nameof(marginBottom): _marginBottom = value; break;
                    case nameof(paddingLeft): _paddingLeft = value; break;
                    case nameof(paddingTop): _paddingTop = value; break;
                    case nameof(paddingRight): _paddingRight = value; break;
                    case nameof(paddingBottom): _paddingBottom = value; break;
                    case nameof(rowGap): _rowGap = value; break;
                    case nameof(columnGap): _columnGap = value; break;
                }
            }
        }
        finally { _resolvingLengths = false; }
    }

    internal Fix64 ResolveTranslation(bool horizontal, Fix64 size, Fix64 vw, Fix64 vh, Fix64 font) =>
        _lengths.TryGetValue(horizontal ? nameof(translateX) : nameof(translateY), out var expression)
            ? CssLength.Resolve(expression, size, vw, vh, font)
            : horizontal ? _translateX : _translateY;

    private void CopyCssState(Style source)
    {
        _lengths.Clear();
        foreach (var pair in source._lengths) _lengths.Add(pair.Key, pair.Value);
        _rowGap = source._rowGap; _columnGap = source._columnGap;
        _translateX = source._translateX; _translateY = source._translateY;
        _textAlign = source._textAlign;
        _fontAsset = source._fontAsset; _textOutlineWidth = source._textOutlineWidth; _textOutlineColor = source._textOutlineColor; _textDirection = source._textDirection;
    }
    private void ResetCssState()
    {
        // Dictionary removal does not invalidate its enumerator on the .NET
        // runtime targeted by the engine. This private cleanup invokes no user
        // code, so a snapshot of every length key is unnecessary.
        foreach (var property in _lengths.Keys)
            if (!_inlineProperties.Contains(property)) _lengths.Remove(property);
        if (!IsInline(nameof(rowGap))) _rowGap = 0;
        if (!IsInline(nameof(columnGap))) _columnGap = 0;
        if (!IsInline(nameof(translateX))) _translateX = 0;
        if (!IsInline(nameof(translateY))) _translateY = 0;
        if (!IsInline(nameof(textAlign))) _textAlign = "";
        if (!IsInline(nameof(fontAsset))) _fontAsset = "";
        if (!IsInline(nameof(textOutlineWidth))) _textOutlineWidth = 0;
        if (!IsInline(nameof(textOutlineColor))) _textOutlineColor = null;
        if (!IsInline(nameof(textDirection))) _textDirection = TextDirectionMode.Auto;
    }
}
