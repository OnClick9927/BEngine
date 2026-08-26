using System.Globalization;
using BEngine.Serialization;

namespace BEngine.UIElements;

internal static class UIAssetSerializer
{
    internal static UIAssetDocument CreateDefaultDocument() => ToDocument(CreateDefaultRoot());

    internal static UIAssetDocument ToDocument(VisualElement root) => new() { Root = ToElementDocument(root) };

    internal static VisualElement CreateElement(UIElementDocument document)
    {
        var element = document.Type switch
        {
            nameof(Label) => new Label(document.Text),
            nameof(Button) => new Button(text: document.Text),
            nameof(ToolbarButton) => new ToolbarButton(text: document.Text),
            nameof(ToolbarMenu) => new ToolbarMenu(document.Text),
            nameof(ToolbarSpacer) => new ToolbarSpacer(),
            nameof(Box) => new Box(),
            nameof(TextField) => new TextField { value = document.Value },
            nameof(SearchField) => new SearchField { value = document.Value },
            nameof(ToolbarSearchField) => new ToolbarSearchField { value = document.Value },
            nameof(FloatField) => new FloatField { value = ParseFloat(document.Value) },
            nameof(IntegerField) => new IntegerField { value = ParseInt(document.Value) },
            nameof(ColorField) => new ColorField { value = ParseEngineColor(document.Value) },
            nameof(Toggle) => new Toggle(document.Text) { value = ParseBool(document.Value) },
            nameof(Foldout) => new Foldout(document.Text) { value = ParseBool(document.Value) },
            nameof(Slider) => new Slider { value = ParseFloat(document.Value) },
            nameof(DropdownField) => new DropdownField { value = document.Value },
            nameof(ProgressBar) => new ProgressBar { title = document.Text, value = ParseFloat(document.Value) },
            nameof(TreeView) => new TreeView(),
            nameof(ListView) => new ListView(),
            nameof(Image) => new Image { sourcePath = document.Value },
            nameof(ScrollView) => new ScrollView(),
            nameof(Toolbar) => new Toolbar(),
            _ => new VisualElement()
        };
        element.name = document.Name;
        element.tooltip = document.Tooltip;
        element.sortingLayer = document.SortingLayer;
        element.orderInLayer = document.OrderInLayer;
        element.atlas = document.Atlas;
        if (!string.IsNullOrWhiteSpace(document.MaterialShader))
            element.material = new Material(Shader.Find(document.MaterialShader));
        foreach (var className in document.Classes.Where(value => !string.IsNullOrWhiteSpace(value)))
            element.AddToClassList(className);
        ApplyStyle(element.style, document.Style);
        foreach (var child in document.Children) element.Add(CreateElement(child));
        return element;
    }

    private static UIElementDocument ToElementDocument(VisualElement element)
    {
        var document = new UIElementDocument
        {
            Type = SupportedTypeName(element),
            Name = element.name,
            Tooltip = element.tooltip,
            Text = element is TextElement text ? text.text : element is Toggle toggle ? toggle.label : string.Empty,
            Value = ElementValue(element),
            SortingLayer = element.sortingLayer,
            OrderInLayer = element.orderInLayer,
            MaterialShader = element.material?.shader.shaderName ?? string.Empty,
            Atlas = element.atlas,
            Classes = element.classList.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            Style = ToStyleDocument(element.style)
        };
        document.Children.AddRange(element.Children.Select(ToElementDocument));
        return document;
    }

    private static string SupportedTypeName(VisualElement element) => element switch
    {
        Label => nameof(Label),
        ToolbarMenu => nameof(ToolbarMenu),
        ToolbarButton => nameof(ToolbarButton),
        Button => nameof(Button),
        ToolbarSearchField => nameof(ToolbarSearchField),
        SearchField => nameof(SearchField),
        TextField => nameof(TextField),
        FloatField => nameof(FloatField),
        IntegerField => nameof(IntegerField),
        ColorField => nameof(ColorField),
        Foldout => nameof(Foldout),
        Toggle => nameof(Toggle),
        Slider => nameof(Slider),
        DropdownField => nameof(DropdownField),
        ProgressBar => nameof(ProgressBar),
        TreeView => nameof(TreeView),
        ListView => nameof(ListView),
        Image => nameof(Image),
        ScrollView => nameof(ScrollView),
        Toolbar => nameof(Toolbar),
        ToolbarSpacer => nameof(ToolbarSpacer),
        Box => nameof(Box),
        _ => nameof(VisualElement)
    };

    private static string ElementValue(VisualElement element) => element switch
    {
        TextField field => field.value,
        FloatField field => field.value.ToString(CultureInfo.InvariantCulture),
        IntegerField field => field.value.ToString(CultureInfo.InvariantCulture),
        Toggle field => field.value.ToString(CultureInfo.InvariantCulture),
        Slider field => field.value.ToString(CultureInfo.InvariantCulture),
        ColorField field => $"{field.value.r},{field.value.g},{field.value.b},{field.value.a}",
        DropdownField field => field.value,
        ProgressBar progress => progress.value.ToString(CultureInfo.InvariantCulture),
        Image image => image.sourcePath,
        _ => string.Empty
    };

    private static BEngine.Color ParseEngineColor(string value)
    {
        var values = value.Split(',', StringSplitOptions.TrimEntries);
        return values.Length >= 3
            ? new BEngine.Color((Fix64)ParseFloat(values[0]), (Fix64)ParseFloat(values[1]),
                (Fix64)ParseFloat(values[2]), (Fix64)(values.Length >= 4 ? ParseFloat(values[3]) : 1))
            : BEngine.Color.white;
    }

    private static UIStyleDocument ToStyleDocument(Style style) => new()
    {
        FlexDirection = style.flexDirection,
        Display = style.display,
        AlignItems = style.alignItems,
        JustifyContent = style.justifyContent,
        Overflow = style.overflow,
        Width = style.width,
        Height = style.height,
        MinWidth = style.minWidth,
        MinHeight = style.minHeight,
        FlexGrow = style.flexGrow,
        FlexShrink = style.flexShrink,
        MarginLeft = style.marginLeft,
        MarginTop = style.marginTop,
        MarginRight = style.marginRight,
        MarginBottom = style.marginBottom,
        PaddingLeft = style.paddingLeft,
        PaddingTop = style.paddingTop,
        PaddingRight = style.paddingRight,
        PaddingBottom = style.paddingBottom,
        FontSize = style.fontSize,
        BorderWidth = style.borderWidth,
        Color = FormatColor(style.color),
        BackgroundColor = FormatColor(style.backgroundColor),
        BorderColor = FormatColor(style.borderColor)
    };

    private static void ApplyStyle(Style style, UIStyleDocument value)
    {
        style.flexDirection = value.FlexDirection;
        style.display = value.Display;
        style.alignItems = value.AlignItems;
        style.justifyContent = value.JustifyContent;
        style.overflow = value.Overflow;
        style.width = value.Width;
        style.height = value.Height;
        style.minWidth = value.MinWidth;
        style.minHeight = value.MinHeight;
        style.flexGrow = value.FlexGrow;
        style.flexShrink = value.FlexShrink;
        style.marginLeft = value.MarginLeft;
        style.marginTop = value.MarginTop;
        style.marginRight = value.MarginRight;
        style.marginBottom = value.MarginBottom;
        style.paddingLeft = value.PaddingLeft;
        style.paddingTop = value.PaddingTop;
        style.paddingRight = value.PaddingRight;
        style.paddingBottom = value.PaddingBottom;
        style.fontSize = value.FontSize;
        style.borderWidth = value.BorderWidth;
        style.color = ParseColor(value.Color);
        style.backgroundColor = ParseColor(value.BackgroundColor);
        style.borderColor = ParseColor(value.BorderColor);
    }

    private static string FormatColor(UIColor? color) => color is { } value
        ? $"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}"
        : string.Empty;

    internal static UIColor? ParseColor(string value)
    {
        var text = value.Trim().TrimStart('#');
        if (text.Length is not (6 or 8) || !uint.TryParse(text, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var packed)) return null;
        if (text.Length == 6) packed = packed << 8 | 0xFF;
        return new UIColor((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private static float ParseFloat(string value) => float.TryParse(value, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    private static int ParseInt(string value) => int.TryParse(value, NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    private static bool ParseBool(string value) => bool.TryParse(value, out var parsed) && parsed;

    private static VisualElement CreateDefaultRoot()
    {
        var root = new VisualElement { name = "Root" };
        root.style.flexGrow = 1;
        return root;
    }
}
