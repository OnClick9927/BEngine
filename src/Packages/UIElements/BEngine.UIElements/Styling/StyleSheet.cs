using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BEngine.UIElements;

public sealed class StyleSheet : ScriptableObject
{
    private readonly List<StyleRule> _rules = [];

    public static StyleSheet Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var sheet = Parse(File.ReadAllText(fullPath));
        sheet.BindAssetReference(fullPath);
        return sheet;
    }

    public static StyleSheet Parse(string uss)
    {
        ArgumentNullException.ThrowIfNull(uss);
        var sheet = new StyleSheet();
        var source = Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        foreach (Match match in Regex.Matches(source, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}"))
        {
            var declarations = ParseDeclarations(match.Groups["body"].Value);
            foreach (var selector in match.Groups["selector"].Value.Split(','))
            {
                var normalized = selector.Trim();
                if (normalized.Length > 0 && declarations.Count > 0)
                    sheet._rules.Add(new StyleRule(normalized, declarations));
            }
        }
        return sheet;
    }

    public void Apply(VisualElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var rule in _rules.Where(rule => rule.Matches(element))
                         .OrderBy(rule => rule.Specificity))
                ApplyDeclarations(element.style, rule.Declarations, fromStyleSheet: true);
        }
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
        bool fromStyleSheet = false)
    {
        void Apply()
        {
            foreach (var (property, rawValue) in declarations)
            {
                var value = rawValue.Trim();
                switch (property.ToLowerInvariant())
                {
                    case "flex-direction": style.flexDirection = ParseEnum(value, FlexDirection.Column); break;
                    case "display": style.display = value.Equals("none", StringComparison.OrdinalIgnoreCase)
                        ? DisplayStyle.None : DisplayStyle.Flex; break;
                    case "align-items": style.alignItems = ParseEnum(value, Align.Stretch); break;
                    case "justify-content": style.justifyContent = ParseEnum(value, Justify.FlexStart); break;
                    case "overflow": style.overflow = ParseEnum(value, Overflow.Visible); break;
                    case "width": style.width = ParseLength(value); break;
                    case "height": style.height = ParseLength(value); break;
                    case "min-width": style.minWidth = ParseLength(value); break;
                    case "min-height": style.minHeight = ParseLength(value); break;
                    case "max-width": style.maxWidth = ParseLength(value, float.PositiveInfinity); break;
                    case "max-height": style.maxHeight = ParseLength(value, float.PositiveInfinity); break;
                    case "flex-grow": style.flexGrow = ParseFloat(value); break;
                    case "flex-shrink": style.flexShrink = ParseFloat(value); break;
                    case "font-size": style.fontSize = ParseLength(value); break;
                    case "border-width": style.borderWidth = ParseLength(value); break;
                    case "color": style.color = ParseColor(value); break;
                    case "background-color": style.backgroundColor = ParseColor(value); break;
                    case "border-color": style.borderColor = ParseColor(value); break;
                    case "margin": ApplySpacing(value, style.SetMargin); break;
                    case "padding": ApplySpacing(value, style.SetPadding); break;
                    case "margin-left": style.marginLeft = ParseLength(value); break;
                    case "margin-top": style.marginTop = ParseLength(value); break;
                    case "margin-right": style.marginRight = ParseLength(value); break;
                    case "margin-bottom": style.marginBottom = ParseLength(value); break;
                    case "padding-left": style.paddingLeft = ParseLength(value); break;
                    case "padding-top": style.paddingTop = ParseLength(value); break;
                    case "padding-right": style.paddingRight = ParseLength(value); break;
                    case "padding-bottom": style.paddingBottom = ParseLength(value); break;
                }
            }
        }
        if (fromStyleSheet) style.ApplyFromStyleSheet(Apply);
        else Apply();
    }

    private static void ApplySpacing(string value, Action<float, float, float, float> apply)
    {
        var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => ParseLength(item)).ToArray();
        if (values.Length == 0) return;
        var (top, right, bottom, left) = values.Length switch
        {
            1 => (values[0], values[0], values[0], values[0]),
            2 => (values[0], values[1], values[0], values[1]),
            3 => (values[0], values[1], values[2], values[1]),
            _ => (values[0], values[1], values[2], values[3])
        };
        apply(left, top, right, bottom);
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
        var text = value.Trim().TrimStart('#');
        if (text.Length is not (6 or 8) || !uint.TryParse(text, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var packed)) return null;
        if (text.Length == 6) packed = packed << 8 | 0xFF;
        return new UIColor((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private sealed class StyleRule
    {
        private readonly SelectorPart[] _parts;

        public StyleRule(string selector, IReadOnlyDictionary<string, string> declarations)
        {
            _parts = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(SelectorPart.Parse).ToArray();
            Declarations = declarations;
            Specificity = _parts.Sum(part => part.Specificity);
        }

        public IReadOnlyDictionary<string, string> Declarations { get; }
        public int Specificity { get; }

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

    private readonly record struct SelectorPart(string? Type, string? Name, IReadOnlyList<string> Classes)
    {
        public int Specificity => (Name is null ? 0 : 100) + Classes.Count * 10 + (Type is null ? 0 : 1);

        public bool Matches(VisualElement element) =>
            (Type is null || element.GetType().Name.Equals(Type, StringComparison.OrdinalIgnoreCase)) &&
            (Name is null || element.name.Equals(Name, StringComparison.Ordinal)) &&
            Classes.All(element.ClassListContains);

        public static SelectorPart Parse(string selector)
        {
            var typeMatch = Regex.Match(selector, @"^[A-Za-z_][\w-]*");
            var nameMatch = Regex.Match(selector, @"#([\w-]+)");
            var classes = Regex.Matches(selector, @"\.([\w-]+)").Select(match => match.Groups[1].Value).ToArray();
            return new SelectorPart(typeMatch.Success ? typeMatch.Value : null,
                nameMatch.Success ? nameMatch.Groups[1].Value : null, classes);
        }
    }
}
