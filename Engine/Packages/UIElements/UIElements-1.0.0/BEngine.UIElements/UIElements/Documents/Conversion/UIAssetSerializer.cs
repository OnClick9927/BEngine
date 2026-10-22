using System.Globalization;
using BEngine.Serialization;

namespace BEngine.UIElements;

internal static class UIAssetSerializer
{
    internal static UIAssetDocument CreateDefaultDocument() => ToDocument(CreateDefaultRoot());

    internal static UIAssetDocument ToDocument(VisualElement root) => new() { Root = ToElementDocument(root) };

    internal static void Validate(UIAssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.Format.Equals("BEngine.UI", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported UI asset format '{document.Format}'.");
        if (document.Version != 1)
            throw new InvalidDataException($"Unsupported UI asset version {document.Version}.");
        if (document.Root is null) throw new InvalidDataException("UI asset has no root element.");
    }

    internal static VisualElement CreateElement(UIElementDocument document)
    {
        if (!string.IsNullOrEmpty(document.CustomUxml)) return UxmlSerializer.DeserializeElement(document.CustomUxml);
        var element = document.Type switch
        {
            nameof(Vector2Field) => new Vector2Field(document.Text) { value = StandardControlValues.Vector2(document.Value) },
            nameof(Vector4Field) => new Vector4Field(document.Text) { value = StandardControlValues.Vector4(document.Value) },
            nameof(RadioButton) => new RadioButton(document.Text) { value = ParseBool(document.Value) },
            nameof(ToolbarToggle) => new ToolbarToggle(document.Text) { value = ParseBool(document.Value) },
            nameof(PasswordField) => new PasswordField(document.Text) { value = document.Value },
            nameof(TextArea) => new TextArea(document.Text) { value = document.Value },
            nameof(HelpBox) => new HelpBox(document.Text),
            nameof(Separator) => new Separator(),
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
        element.accessibilityRole = document.AccessibilityRole;
        element.accessibilityName = document.AccessibilityName;
        element.accessibilityDescription = document.AccessibilityDescription;
        element.accessibilityValue = document.AccessibilityValue;
        element.accessibilityHidden = document.AccessibilityHidden;
        element.accessibilityLiveRegion = document.AccessibilityLiveRegion;
        element.focusable = document.Focusable;
        element.tabIndex = document.TabIndex;
        if (element is TextField textField)
        {
            textField.maxLength = document.TextMaxLength;
            textField.placeholderText = document.TextPlaceholder;
            // Specialized controls keep their constructor defaults when loading older assets.
            textField.multiline = document.TextMultiline || textField is TextArea;
            textField.isPasswordField = document.TextPassword || textField is PasswordField;
            textField.isReadOnly = document.TextReadOnly;
            textField.scrollToEnd = document.TextScrollToEnd;
            textField.maskCharacter = document.TextMaskCharacter;
        }
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
        if (UxmlFactoryRegistry.IsRegistered(element.GetType()))
            return new UIElementDocument { Type = element.GetType().Name, Name = element.name, CustomUxml = UxmlSerializer.SerializeElement(element) };
        var document = new UIElementDocument
        {
            Type = SupportedTypeName(element),
            Name = element.name,
            Tooltip = element.tooltip,
            AccessibilityRole = element.accessibilityRole,
            AccessibilityName = element.accessibilityName,
            AccessibilityDescription = element.accessibilityDescription,
            AccessibilityValue = element.accessibilityValue,
            AccessibilityHidden = element.accessibilityHidden,
            AccessibilityLiveRegion = element.accessibilityLiveRegion,
            Focusable = element.focusable,
            TabIndex = element.tabIndex,
            Text = element is TextElement text ? text.text : element is Toggle toggle ? toggle.label :
                element is Vector2Field vector2 ? vector2.label : element is Vector4Field vector4 ? vector4.label :
                element is TextField field ? field.label : string.Empty,
            Value = ElementValue(element),
            SortingLayer = element.sortingLayer,
            OrderInLayer = element.orderInLayer,
            MaterialShader = element.material?.shader.shaderName ?? string.Empty,
            Atlas = element.atlas,
            Classes = element.classList.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            Style = ToStyleDocument(element.style)
        };
        if (element is TextField textField)
        {
            document.TextMaxLength = textField.maxLength;
            document.TextPlaceholder = textField.placeholderText;
            document.TextMultiline = textField.multiline;
            document.TextPassword = textField.isPasswordField;
            document.TextReadOnly = textField.isReadOnly;
            document.TextScrollToEnd = textField.scrollToEnd;
            document.TextMaskCharacter = textField.maskCharacter;
        }
        if (element is not (Vector2Field or Vector4Field))
            document.Children.AddRange(element.Children.Select(ToElementDocument));
        return document;
    }

    private static string SupportedTypeName(VisualElement element) => element switch
    {
        Vector2Field => nameof(Vector2Field),
        Vector4Field => nameof(Vector4Field),
        RadioButton => nameof(RadioButton),
        ToolbarToggle => nameof(ToolbarToggle),
        PasswordField => nameof(PasswordField),
        TextArea => nameof(TextArea),
        HelpBox => nameof(HelpBox),
        Separator => nameof(Separator),
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
        Vector2Field field => StandardControlValues.Format(field.value),
        Vector4Field field => StandardControlValues.Format(field.value),
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
        CssDeclarations = style.ExportCssDeclarations(),
        FlexDirection = style.IsInline(nameof(Style.flexDirection)) ? style.flexDirection : null,
        Display = style.IsInline(nameof(Style.display)) ? style.display : null,
        AlignItems = style.IsInline(nameof(Style.alignItems)) ? style.alignItems : null,
        JustifyContent = style.IsInline(nameof(Style.justifyContent)) ? style.justifyContent : null,
        Overflow = style.IsInline(nameof(Style.overflow)) ? style.overflow : null,
        Position = style.IsInline(nameof(Style.position)) ? style.position : null,
        Left = style.IsInline(nameof(Style.left)) ? style.left : null,
        Top = style.IsInline(nameof(Style.top)) ? style.top : null,
        Right = style.IsInline(nameof(Style.right)) ? style.right : null,
        Bottom = style.IsInline(nameof(Style.bottom)) ? style.bottom : null,
        Width = style.IsInline(nameof(Style.width)) ? style.width : null,
        Height = style.IsInline(nameof(Style.height)) ? style.height : null,
        MinWidth = style.IsInline(nameof(Style.minWidth)) ? style.minWidth : null,
        MinHeight = style.IsInline(nameof(Style.minHeight)) ? style.minHeight : null,
        MaxWidth = style.IsInline(nameof(Style.maxWidth)) ? style.maxWidth : null,
        MaxHeight = style.IsInline(nameof(Style.maxHeight)) ? style.maxHeight : null,
        FlexGrow = style.IsInline(nameof(Style.flexGrow)) ? style.flexGrow : null,
        FlexShrink = style.IsInline(nameof(Style.flexShrink)) ? style.flexShrink : null,
        MarginLeft = style.IsInline(nameof(Style.marginLeft)) ? style.marginLeft : null,
        MarginTop = style.IsInline(nameof(Style.marginTop)) ? style.marginTop : null,
        MarginRight = style.IsInline(nameof(Style.marginRight)) ? style.marginRight : null,
        MarginBottom = style.IsInline(nameof(Style.marginBottom)) ? style.marginBottom : null,
        PaddingLeft = style.IsInline(nameof(Style.paddingLeft)) ? style.paddingLeft : null,
        PaddingTop = style.IsInline(nameof(Style.paddingTop)) ? style.paddingTop : null,
        PaddingRight = style.IsInline(nameof(Style.paddingRight)) ? style.paddingRight : null,
        PaddingBottom = style.IsInline(nameof(Style.paddingBottom)) ? style.paddingBottom : null,
        FontSize = style.IsInline(nameof(Style.fontSize)) ? style.fontSize : null,
        BorderWidth = style.IsInline(nameof(Style.borderWidth)) ? style.borderWidth : null,
        Color = style.IsInline(nameof(Style.color)) ? FormatColor(style.color) : string.Empty,
        BackgroundColor = style.IsInline(nameof(Style.backgroundColor)) ? FormatColor(style.backgroundColor) : string.Empty,
        BorderColor = style.IsInline(nameof(Style.borderColor)) ? FormatColor(style.borderColor) : string.Empty
    };

    private static void ApplyStyle(Style style, UIStyleDocument value)
    {
        if (value.FlexDirection is { } flexDirection) style.flexDirection = flexDirection;
        if (value.Display is { } display) style.display = display;
        if (value.AlignItems is { } alignItems) style.alignItems = alignItems;
        if (value.JustifyContent is { } justifyContent) style.justifyContent = justifyContent;
        if (value.Overflow is { } overflow) style.overflow = overflow;
        if (value.Position is { } position) style.position = position;
        if (value.Left is { } left) style.left = left;
        if (value.Top is { } top) style.top = top;
        if (value.Right is { } right) style.right = right;
        if (value.Bottom is { } bottom) style.bottom = bottom;
        if (value.Width is { } width) style.width = width;
        if (value.Height is { } height) style.height = height;
        if (value.MinWidth is { } minWidth) style.minWidth = minWidth;
        if (value.MinHeight is { } minHeight) style.minHeight = minHeight;
        if (value.MaxWidth is { } maxWidth) style.maxWidth = maxWidth;
        if (value.MaxHeight is { } maxHeight) style.maxHeight = maxHeight;
        if (value.FlexGrow is { } flexGrow) style.flexGrow = flexGrow;
        if (value.FlexShrink is { } flexShrink) style.flexShrink = flexShrink;
        if (value.MarginLeft is { } marginLeft) style.marginLeft = marginLeft;
        if (value.MarginTop is { } marginTop) style.marginTop = marginTop;
        if (value.MarginRight is { } marginRight) style.marginRight = marginRight;
        if (value.MarginBottom is { } marginBottom) style.marginBottom = marginBottom;
        if (value.PaddingLeft is { } paddingLeft) style.paddingLeft = paddingLeft;
        if (value.PaddingTop is { } paddingTop) style.paddingTop = paddingTop;
        if (value.PaddingRight is { } paddingRight) style.paddingRight = paddingRight;
        if (value.PaddingBottom is { } paddingBottom) style.paddingBottom = paddingBottom;
        if (value.FontSize is { } fontSize) style.fontSize = fontSize;
        if (value.BorderWidth is { } borderWidth) style.borderWidth = borderWidth;
        if (ParseColor(value.Color) is { } color) style.color = color;
        if (ParseColor(value.BackgroundColor) is { } backgroundColor) style.backgroundColor = backgroundColor;
        if (ParseColor(value.BorderColor) is { } borderColor) style.borderColor = borderColor;
        StyleSheet.ApplyDeclarations(style, value.CssDeclarations);
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
