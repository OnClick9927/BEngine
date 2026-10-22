using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BEngine.Serialization;

namespace BEngine.UIElements;

[EditorIcon("Icons/Assets/AssetStyle.png")]
public sealed class StyleSheet : ScriptableObject
{
    private string _sourceCss = string.Empty;
    protected override object? CaptureClonePayload() => _sourceCss;
    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> cloneObject)
    {
        var parsed = Parse((string)payload!);
        _sourceCss = parsed._sourceCss;
        _rules.Clear(); _rules.AddRange(parsed._rules);
        _importantRules.Clear(); _importantRules.AddRange(parsed._importantRules);
        _matchingRules.Clear();
        Destroy(parsed);
    }
    private readonly List<StyleRule> _rules = [];
    private readonly List<StyleRule> _importantRules = [];
    // Selectors compare the simple type name. Keep strings rather than Types so
    // this index cannot retain a project's collectible script assemblies.
    private readonly Dictionary<string, MatchingRules> _matchingRules = new(StringComparer.OrdinalIgnoreCase);
    private sealed record MatchingRules(StyleRule[] Rules, StyleRule[] ImportantRules);
    private MatchingRules RulesFor(VisualElement element)
    {
        var typeName = element.GetType().Name;
        if (_matchingRules.TryGetValue(typeName, out var matching)) return matching;
        return BuildMatchingRules(typeName);
    }
    private MatchingRules BuildMatchingRules(string typeName)
    {
        var matching = new MatchingRules(_rules.Where(rule => rule.CanMatchType(typeName)).ToArray(),
            _importantRules.Where(rule => rule.CanMatchType(typeName)).ToArray());
        _matchingRules.Add(typeName, matching);
        return matching;
    }
    private static readonly Lazy<StyleSheet> Default = new(() =>
    {
        var css = Resources.Load<string>("UIElements/Default.bcss");
        if (css is not null) return Parse(css);
        using var stream = typeof(StyleSheet).Assembly.GetManifestResourceStream("BEngine.UIElements.Default.bcss")
            ?? throw new InvalidOperationException("The default UI style sheet is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    internal static StyleSheet defaultStyleSheet => Default.Value;
    internal bool SupportsLocalInteractionRepaint => _rules.All(rule => rule.SupportsLocalInteractionRepaint);

    internal StyleSheet CloneAsset()
    {
        ThrowIfDestroyed();
        var clone = new StyleSheet { name = name, hideFlags = hideFlags, _sourceCss = _sourceCss };
        clone.CopyAssetReferenceFrom(this);
        // Parsed rules are immutable; each asset owns its list and lifetime.
        clone._rules.AddRange(_rules);
        clone._importantRules.AddRange(_importantRules);
        return clone;
    }

    public static StyleSheet Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RejectLegacyExtension(path);
        return Document<StyleSheet>.Read(path, LoadAsset).ToAsset();
    }

    private static StyleSheet LoadAsset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RejectLegacyExtension(path);
        var fullPath = RuntimeServiceAccess.PathService.GetFullPath(path);
        var sheet = Parse(RuntimeServiceAccess.FileSystem.ReadAllText(fullPath));
        sheet.BindAssetReference(fullPath);
        return sheet;
    }

    private static void RejectLegacyExtension(string path)
    {
        var extension = RuntimeServiceAccess.PathService.GetExtension(path);
        if (extension.Equals(".uss", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ucss", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Legacy UI style sheet extension '{extension}' is not supported; use .bss or .bcss.");
    }

    public static StyleSheet Parse(string uss)
    {
        ArgumentNullException.ThrowIfNull(uss);
        var sheet = new StyleSheet { _sourceCss = uss };
        var source = Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var sourceOrder = 0;
        void ReadRules(string text, string condition)
        {
            var offset = 0;
            while (offset < text.Length)
            {
                var open = text.IndexOf('{', offset);
                if (open < 0) break;
                var depth = 1;
                var close = open + 1;
                for (; close < text.Length && depth > 0; close++)
                    depth += text[close] == '{' ? 1 : text[close] == '}' ? -1 : 0;
                if (depth != 0) break;
                var header = text[offset..open].Trim();
                var body = text[(open + 1)..(close - 1)];
                offset = close;
                if (header.StartsWith("@media", StringComparison.OrdinalIgnoreCase))
                {
                    ReadRules(body, condition + " " + header[6..]);
                    continue;
                }
                if (header.StartsWith('@')) continue;
                var declarations = ParseDeclarations(body);
                foreach (var selector in header.Split(','))
                {
                    var normalized = selector.Trim();
                    if (normalized.Length > 0 && declarations.Count > 0)
                        sheet._rules.Add(new StyleRule(normalized, declarations, sourceOrder++, condition));
                }
            }
        }
        ReadRules(source, "");
        sheet._rules.Sort(static (left, right) =>
        {
            var specificity = left.Specificity.CompareTo(right.Specificity);
            return specificity != 0 ? specificity : left.SourceOrder.CompareTo(right.SourceOrder);
        });
        sheet._importantRules.AddRange(sheet._rules.Where(rule => rule.ImportantDeclarations.Count > 0));
        return sheet;
    }

    public void Apply(VisualElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ApplyDescendants(root, root);
    }

    private void ApplyDescendants(VisualElement root, VisualElement element)
    {
            var matching = RulesFor(element);
            foreach (var rule in matching.Rules)
                if (rule.MatchesViewport(root.styleViewportWidth, root.styleViewportHeight) && rule.Matches(element))
                    rule.Apply(element.style);
            foreach (var rule in matching.ImportantRules)
                if (rule.MatchesViewport(root.styleViewportWidth, root.styleViewportHeight) && rule.Matches(element))
                    rule.Apply(element.style, important: true);
        using var children = element.GetChildEnumerator();
        while (children.MoveNext()) ApplyDescendants(root, children.Current);
    }

    internal void ApplyInteractionPaint(VisualElement element, VisualElement owner)
    {
        var matching = RulesFor(element);
        foreach (var rule in matching.Rules)
            if (rule.PaintDeclarations.Count > 0 && rule.MatchesViewport(owner.styleViewportWidth, owner.styleViewportHeight) && rule.Matches(element))
                rule.Apply(element.style, paintOnly: true);
        foreach (var rule in matching.ImportantRules)
            if (rule.ImportantPaintDeclarations.Count > 0 && rule.MatchesViewport(owner.styleViewportWidth, owner.styleViewportHeight) && rule.Matches(element))
                rule.Apply(element.style, important: true, paintOnly: true);
    }

    internal static IReadOnlyDictionary<string, string> ParseDeclarations(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator <= 0) continue;
            values[declaration[..separator].Trim()] = declaration[(separator + 1)..].Trim();
        }
        return values;
    }

    internal static void ApplyDeclarations(
        Style style,
        IReadOnlyDictionary<string, string> declarations,
        bool fromStyleSheet = false) => ApplyDeclarationsCore(style, declarations, fromStyleSheet, null);

    private static void ApplyDeclarationsCore(Style style,
        IReadOnlyDictionary<string, string>? declarations, bool fromStyleSheet,
        PreparedDeclaration[]? prepared)
    {
        using var scope = style.BeginStyleSheetApplication(fromStyleSheet);
        if (prepared is not null)
        {
            foreach (var declaration in prepared)
                ApplyOne(style, declaration.Property, declaration.Value, declaration);
        }
        else
        {
            // Inline/UXML callers can supply a mutable dictionary. Read it
            // for each application rather than caching its keys or values.
            foreach (var (property, rawValue) in declarations!)
                ApplyOne(style, property.ToLowerInvariant(), CleanValue(rawValue), null);
        }
    }

    private static void ApplyOne(Style style, string property, string value, PreparedDeclaration? declaration)
    {
        switch (property)
        {
            case "row-gap": style.SetLength(nameof(style.rowGap), value); break;
            case "column-gap": style.SetLength(nameof(style.columnGap), value); break;
            case "gap":
                var gaps = declaration?.Parts ?? value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (gaps.Length > 0) {
                    style.SetLength(nameof(style.rowGap), gaps[0]);
                    style.SetLength(nameof(style.columnGap), gaps.Length > 1 ? gaps[1] : gaps[0]);
                }
                break;
            case "text-align": style.textAlign = value; break;
            case "transform":
                var parts = declaration?.Parts ?? TranslationParts(value);
                if (parts.Length == 2) {
                    style.SetLength(nameof(style.translateX), parts[0]);
                    style.SetLength(nameof(style.translateY), parts[1]);
                }
                break;
            case "flex-direction": style.flexDirection = declaration is null ? ParseEnum(value, FlexDirection.Column) : (FlexDirection)declaration.Parsed!; break;
            case "display": style.display = declaration is null ? ParseDisplay(value) : (DisplayStyle)declaration.Parsed!; break;
            case "align-items": style.alignItems = declaration is null ? ParseEnum(value, Align.Stretch) : (Align)declaration.Parsed!; break;
            case "justify-content": style.justifyContent = declaration is null ? ParseEnum(value, Justify.FlexStart) : (Justify)declaration.Parsed!; break;
            case "overflow": style.overflow = declaration is null ? ParseEnum(value, Overflow.Visible) : (Overflow)declaration.Parsed!; break;
            case "position": style.position = declaration is null ? ParseEnum(value, Position.Relative) : (Position)declaration.Parsed!; break;
            case "left": style.SetLength(nameof(style.left), value, float.NaN); break;
            case "top": style.SetLength(nameof(style.top), value, float.NaN); break;
            case "right": style.SetLength(nameof(style.right), value, float.NaN); break;
            case "bottom": style.SetLength(nameof(style.bottom), value, float.NaN); break;
            case "width": style.SetLength(nameof(style.width), value); break;
            case "height": style.SetLength(nameof(style.height), value); break;
            case "min-width": style.SetLength(nameof(style.minWidth), value); break;
            case "min-height": style.SetLength(nameof(style.minHeight), value); break;
            case "max-width": style.SetLength(nameof(style.maxWidth), value, float.PositiveInfinity); break;
            case "max-height": style.SetLength(nameof(style.maxHeight), value, float.PositiveInfinity); break;
            case "flex-grow": style.flexGrow = declaration is null ? ParseFloat(value) : (float)declaration.Parsed!; break;
            case "flex-shrink": style.flexShrink = declaration is null ? ParseFloat(value) : (float)declaration.Parsed!; break;
            case "font-size": style.SetLength(nameof(style.fontSize), value); break;
            case "font-asset": style.fontAsset = declaration is null ? Style.FontPath(value) : (string)declaration.Parsed!; break;
            case "text-outline-width": style.textOutlineWidth = declaration is null ? ParseLength(value) : (float)declaration.Parsed!; break;
            case "text-outline-color": style.textOutlineColor = declaration is null ? ParseColor(value) : (UIColor?)declaration.Parsed; break;
            case "text-direction": style.textDirection = declaration is null ? ParseTextDirection(value) : (TextDirectionMode)declaration.Parsed!; break;
            case "border-width": style.borderWidth = declaration is null ? ParseLength(value) : (float)declaration.Parsed!; break;
            case "color": style.color = declaration is null ? ParseColor(value) : (UIColor?)declaration.Parsed; break;
            case "background-color": style.backgroundColor = declaration is null ? ParseColor(value) : (UIColor?)declaration.Parsed; break;
            case "border-color": style.borderColor = declaration is null ? ParseColor(value) : (UIColor?)declaration.Parsed; break;
            case "margin": ApplySpacing(style, false, value, declaration?.Spacing); break;
            case "padding": ApplySpacing(style, true, value, declaration?.Spacing); break;
            case "margin-left": style.SetLength(nameof(style.marginLeft), value); break;
            case "margin-top": style.SetLength(nameof(style.marginTop), value); break;
            case "margin-right": style.SetLength(nameof(style.marginRight), value); break;
            case "margin-bottom": style.SetLength(nameof(style.marginBottom), value); break;
            case "padding-left": style.SetLength(nameof(style.paddingLeft), value); break;
            case "padding-top": style.SetLength(nameof(style.paddingTop), value); break;
            case "padding-right": style.SetLength(nameof(style.paddingRight), value); break;
            case "padding-bottom": style.SetLength(nameof(style.paddingBottom), value); break;
        }
    }

    private static string CleanValue(string value) =>
        Regex.Replace(value, @"\s*!important\s*$", "", RegexOptions.IgnoreCase).Trim();

    private static DisplayStyle ParseDisplay(string value) =>
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ? DisplayStyle.None : DisplayStyle.Flex;

    private static TextDirectionMode ParseTextDirection(string value) => value.Trim().ToLowerInvariant() switch
    {
        "ltr" or "left-to-right" => TextDirectionMode.LeftToRight,
        "rtl" or "right-to-left" => TextDirectionMode.RightToLeft,
        _ => TextDirectionMode.Auto
    };

    private static string[] TranslationParts(string value)
    {
        var match = Regex.Match(value, @"translate\(\s*([^,]+),\s*([^)]+)\)");
        return match.Success ? [match.Groups[1].Value, match.Groups[2].Value] : [];
    }

    private static void ApplySpacing(Style style, bool padding, string value, float[]? prepared = null)
    {
        var values = prepared ?? value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => ParseLength(item)).ToArray();
        if (values.Length == 0) return;
        var (top, right, bottom, left) = values.Length switch
        {
            1 => (values[0], values[0], values[0], values[0]),
            2 => (values[0], values[1], values[0], values[1]),
            3 => (values[0], values[1], values[2], values[1]),
            _ => (values[0], values[1], values[2], values[3])
        };
        if (padding) style.SetPadding(left, top, right, bottom);
        else style.SetMargin(left, top, right, bottom);
    }

    private sealed class PreparedDeclaration
    {
        internal readonly string Property, Value;
        internal readonly bool Important;
        internal readonly object? Parsed;
        internal readonly string[]? Parts;
        internal readonly float[]? Spacing;

        internal PreparedDeclaration(string property, string rawValue)
        {
            Property = property.ToLowerInvariant(); Value = CleanValue(rawValue);
            Important = rawValue.EndsWith("!important", StringComparison.OrdinalIgnoreCase);
            Parsed = Property switch
            {
                "flex-direction" => ParseEnum(Value, FlexDirection.Column),
                "display" => ParseDisplay(Value),
                "align-items" => ParseEnum(Value, Align.Stretch),
                "justify-content" => ParseEnum(Value, Justify.FlexStart),
                "overflow" => ParseEnum(Value, Overflow.Visible),
                "position" => ParseEnum(Value, Position.Relative),
                "flex-grow" or "flex-shrink" => ParseFloat(Value),
                "font-asset" => Style.FontPath(Value),
                "text-outline-width" or "border-width" => ParseLength(Value),
                "text-outline-color" or "color" or "background-color" or "border-color" => ParseColor(Value),
                "text-direction" => ParseTextDirection(Value),
                _ => null
            };
            Parts = Property switch
            {
                "gap" => Value.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                "transform" => TranslationParts(Value),
                _ => null
            };
            if (Property is "margin" or "padding")
                Spacing = Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(item => ParseLength(item)).ToArray();
        }
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<T>(normalized, true, out var parsed) ? parsed : fallback;
    }

    private static float ParseLength(string value, float fallback = 0)
    {
        var normalized = value.Trim();
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("auto", StringComparison.OrdinalIgnoreCase)) return fallback;
        if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^2];
        return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : fallback;
    }

    private static float ParseFloat(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static UIColor? ParseColor(string value)
    {
        var known = value.Trim().ToLowerInvariant() switch
        {
            "transparent" => UIColor.Clear,
            "white" => new UIColor(255, 255, 255),
            "black" => new UIColor(0, 0, 0),
            _ => (UIColor?)null
        };
        if (known is not null) return known;
        var rgb = Regex.Match(value, @"^rgba?\(([^)]+)\)$", RegexOptions.IgnoreCase);
        if (rgb.Success)
        {
            var channels = rgb.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
            if (channels.Length is 3 or 4)
            {
                byte Channel(string v) => (byte)Math.Clamp((int)Math.Round(v.EndsWith('%')
                    ? ParseFloat(v[..^1]) * 2.55f : ParseFloat(v)), 0, 255);
                return new UIColor(Channel(channels[0]), Channel(channels[1]), Channel(channels[2]),
                    channels.Length == 4 ? (byte)Math.Clamp((int)Math.Round(ParseFloat(channels[3]) * 255), 0, 255) : (byte)255);
            }
        }
        var text = value.Trim().TrimStart('#');
        if (text.Length is 3 or 4) text = string.Concat(text.Select(c => $"{c}{c}"));
        if (text.Length is not (6 or 8) || !uint.TryParse(text, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var packed)) return null;
        if (text.Length == 6) packed = packed << 8 | 0xFF;
        return new UIColor((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private sealed class StyleRule
    {
        private readonly SelectorPart[] _parts;
        private readonly PreparedDeclaration[] _prepared, _importantPrepared, _paintPrepared, _importantPaintPrepared;

        public StyleRule(
            string selector,
            IReadOnlyDictionary<string, string> declarations,
            int sourceOrder, string condition)
        {
            _parts = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(SelectorPart.Parse).ToArray();
            Declarations = declarations;
            ImportantDeclarations = declarations.Where(pair => pair.Value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            static bool IsPaint(string property) => property.ToLowerInvariant() is "color" or "background-color" or "border-color";
            PaintDeclarations = declarations.Where(pair => IsPaint(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            ImportantPaintDeclarations = ImportantDeclarations.Where(pair => IsPaint(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            // These dictionaries belong solely to the private parsed rule; no
            // mutable caller-owned declaration dictionary enters this cache.
            _prepared = declarations.Select(pair => new PreparedDeclaration(pair.Key, pair.Value)).ToArray();
            _importantPrepared = _prepared.Where(value => value.Important).ToArray();
            _paintPrepared = _prepared.Where(value => IsPaint(value.Property)).ToArray();
            _importantPaintPrepared = _importantPrepared.Where(value => IsPaint(value.Property)).ToArray();
            var interactive = selector.Contains(":hover", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains(":active", StringComparison.OrdinalIgnoreCase) || selector.Contains(":focus", StringComparison.OrdinalIgnoreCase);
            SupportsLocalInteractionRepaint = !interactive || (_parts.Length == 1 && declarations.Keys.All(IsPaint));
            Specificity = _parts.Sum(part => part.Specificity);
            SourceOrder = sourceOrder;
            Condition = condition;
        }

        internal void Apply(Style style, bool important = false, bool paintOnly = false) =>
            ApplyDeclarationsCore(style, null, true, important
                ? paintOnly ? _importantPaintPrepared : _importantPrepared
                : paintOnly ? _paintPrepared : _prepared);

        public IReadOnlyDictionary<string, string> Declarations { get; }
        public IReadOnlyDictionary<string, string> ImportantDeclarations { get; }
        public IReadOnlyDictionary<string, string> PaintDeclarations { get; }
        public IReadOnlyDictionary<string, string> ImportantPaintDeclarations { get; }
        public bool SupportsLocalInteractionRepaint { get; }
        public int Specificity { get; }
        public int SourceOrder { get; }
        private string Condition { get; }
        public bool MatchesViewport(float width, float height)
        {
            if (Condition.Length == 0) return true;
            foreach (Match match in Regex.Matches(Condition, @"\((min|max)-(width|height)\s*:\s*([^)]+)\)", RegexOptions.IgnoreCase))
            {
                var actual = match.Groups[2].Value == "width" ? width : height;
                var limit = CssLength.Resolve(match.Groups[3].Value, actual, width, height, 16);
                if (match.Groups[1].Value == "min" ? actual < limit : actual > limit) return false;
            }
            return !Condition.Contains("print", StringComparison.OrdinalIgnoreCase);
        }

        internal bool CanMatchType(string typeName) => _parts.Length > 0 &&
            (_parts[^1].Type is null || _parts[^1].Type!.Equals(typeName, StringComparison.OrdinalIgnoreCase));

        public bool Matches(VisualElement element)
        {
            if (_parts.Length == 0 || !_parts[^1].Matches(element)) return false;
            var ancestor = element.parent;
            for (var index = _parts.Length - 2; index >= 0; index--)
            {
                while (ancestor is not null && !_parts[index].Matches(ancestor)) ancestor = ancestor.parent;
                if (ancestor is null) return false;
                ancestor = ancestor.parent;
            }
            return true;
        }
    }

    private readonly record struct SelectorPart(string? Type, string? Name, IReadOnlyList<string> Classes,
        IReadOnlyList<string> States)
    {
        public int Specificity => (Name is null ? 0 : 100) + (Classes.Count + States.Count) * 10 + (Type is null ? 0 : 1);

        public bool Matches(VisualElement element)
        {
            if (Type is not null && !element.GetType().Name.Equals(Type, StringComparison.OrdinalIgnoreCase) ||
                Name is not null && !element.name.Equals(Name, StringComparison.Ordinal)) return false;
            for (var i = 0; i < Classes.Count; i++)
                if (!element.ClassListContains(Classes[i])) return false;
            for (var i = 0; i < States.Count; i++)
            {
                var matches = States[i] switch
                {
                    "hover" => element.enabledInHierarchy && element.hovered,
                    "active" => element.enabledInHierarchy && element.pressed,
                    "focus" => element.enabledInHierarchy && element.focused,
                    "enabled" => element.enabledInHierarchy,
                    "disabled" => !element.enabledInHierarchy,
                    "checked" => element is Toggle { value: true },
                    _ => false
                };
                if (!matches) return false;
            }
            return true;
        }

        public static SelectorPart Parse(string selector)
        {
            var typeMatch = Regex.Match(selector, @"^[A-Za-z_][\w-]*");
            var nameMatch = Regex.Match(selector, @"#([\w-]+)");
            var classes = Regex.Matches(selector, @"\.([\w-]+)").Select(match => match.Groups[1].Value).ToArray();
            var states = Regex.Matches(selector, @":([\w-]+)").Select(match => match.Groups[1].Value.ToLowerInvariant()).ToArray();
            return new SelectorPart(typeMatch.Success ? typeMatch.Value : null,
                nameMatch.Success ? nameMatch.Groups[1].Value : null, classes, states);
        }
    }
}
