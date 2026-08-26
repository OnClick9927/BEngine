using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BEngine.UIElements;

internal static class UxmlSerializer
{
    internal static UxmlTemplate LoadTemplate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var document = XDocument.Load(fullPath, LoadOptions.SetLineInfo);
        var documentRoot = document.Root ?? throw new InvalidDataException("UXML document has no root element.");
        var baseDirectory = Path.GetDirectoryName(fullPath)!;
        var rootNode = documentRoot.Name.LocalName.Equals("UXML", StringComparison.OrdinalIgnoreCase)
            ? documentRoot.Elements().FirstOrDefault(element =>
                !element.Name.LocalName.Equals("Style", StringComparison.OrdinalIgnoreCase))
            : documentRoot;
        if (rootNode is null) throw new InvalidDataException("UXML document has no VisualElement root.");

        var styles = new List<(string Path, StyleSheet Sheet)>();
        foreach (var styleElement in documentRoot.Descendants().Where(element =>
                     element.Name.LocalName.Equals("Style", StringComparison.OrdinalIgnoreCase)))
        {
            var source = Attribute(styleElement, "src");
            if (string.IsNullOrWhiteSpace(source)) continue;
            var stylePath = Path.GetFullPath(Path.Combine(baseDirectory, source));
            styles.Add((stylePath, StyleSheet.Load(stylePath)));
        }
        return new UxmlTemplate(fullPath, new XElement(rootNode), styles);
    }

    internal static VisualElement Load(string path) => LoadTemplate(path).Instantiate();

    internal sealed class UxmlTemplate
    {
        private readonly XElement _rootNode;
        private readonly IReadOnlyList<(string Path, StyleSheet Sheet)> _styles;
        private readonly IReadOnlyDictionary<string, DateTime> _writeTimes;

        internal UxmlTemplate(
            string path,
            XElement rootNode,
            IReadOnlyList<(string Path, StyleSheet Sheet)> styles)
        {
            _rootNode = rootNode;
            _styles = styles;
            _writeTimes = styles.Select(style => style.Path).Append(path).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(file => file, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
        }

        internal bool IsCurrent => _writeTimes.All(pair =>
            File.Exists(pair.Key) && File.GetLastWriteTimeUtc(pair.Key) == pair.Value);

        internal VisualElement Instantiate()
        {
            var inlineStyles = new Dictionary<VisualElement, string>();
            var root = CreateElement(_rootNode, inlineStyles);
            foreach (var (_, sheet) in _styles) root.styleSheets.Add(sheet);
            root.ApplyStyleSheets();
            foreach (var (element, declarations) in inlineStyles)
                StyleSheet.ApplyDeclarations(element.style, StyleSheet.ParseDeclarations(declarations));
            return root;
        }
    }

    internal static void Save(VisualElement root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("UXML", ToElement(root)));
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        document.Save(fullPath);
    }

    private static VisualElement CreateElement(XElement node, IDictionary<VisualElement, string> inlineStyles)
    {
        var type = node.Name.LocalName;
        var label = Attribute(node, "label") ?? string.Empty;
        var text = Attribute(node, "text") ?? node.Value.Trim();
        var value = Attribute(node, "value") ?? string.Empty;
        var element = type switch
        {
            nameof(Label) => new Label(text),
            nameof(Button) => new Button(text: text),
            nameof(ToolbarButton) => new ToolbarButton(text: text),
            nameof(ToolbarMenu) => new ToolbarMenu(text),
            nameof(ToolbarSpacer) => new ToolbarSpacer(),
            nameof(Box) => new Box(),
            nameof(TextField) => new TextField(label) { value = value },
            nameof(SearchField) => new SearchField(label) { value = value },
            nameof(ToolbarSearchField) => new ToolbarSearchField(label) { value = value },
            nameof(FloatField) => new FloatField(label) { value = ParseFloat(value) },
            nameof(IntegerField) => new IntegerField(label) { value = ParseInt(value) },
            nameof(ColorField) => new ColorField(label) { value = ParseEngineColor(value) },
            nameof(Toggle) => new Toggle(label) { value = ParseBool(value) },
            nameof(Foldout) => new Foldout(label) { value = ParseBool(value) },
            nameof(Slider) => new Slider(label, ParseFloat(Attribute(node, "low-value") ?? "0"),
                ParseFloat(Attribute(node, "high-value") ?? "1")) { value = ParseFloat(value) },
            nameof(DropdownField) => CreateDropdown(label, value,
                Attribute(node, "choices") ?? string.Empty),
            nameof(ScrollView) => new ScrollView(),
            nameof(Toolbar) => new Toolbar(),
            nameof(Image) => new Image { sourcePath = Attribute(node, "src") ?? value },
            nameof(ProgressBar) => new ProgressBar
            {
                lowValue = ParseFloat(Attribute(node, "low-value") ?? "0"),
                highValue = ParseFloat(Attribute(node, "high-value") ?? "100"),
                value = ParseFloat(value),
                title = Attribute(node, "title") ?? text
            },
            nameof(TreeView) => CreateTreeView(node),
            nameof(ListView) => CreateListView(node),
            _ => new VisualElement()
        };
        element.name = Attribute(node, "name") ?? string.Empty;
        element.tooltip = Attribute(node, "tooltip") ?? string.Empty;
        element.sourceTag = Attribute(node, "source-tag") ?? string.Empty;
        element.sourceText = Attribute(node, "source-text") ?? string.Empty;
        var sortingLayerText = Attribute(node, "sorting-layer");
        if (ulong.TryParse(sortingLayerText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var sortingLayer) && SortingLayer.IsUi(sortingLayer))
            element.sortingLayer = sortingLayer;
        element.orderInLayer = ParseInt(Attribute(node, "order-in-layer") ?? "0");
        element.atlas = Attribute(node, "atlas") ?? string.Empty;
        if (Attribute(node, "material-shader") is { Length: > 0 } materialShader)
            element.material = new Material(Shader.Find(materialShader));
        foreach (var sourceAttribute in node.Elements().Where(child =>
                     child.Name.LocalName.Equals("SourceAttribute", StringComparison.OrdinalIgnoreCase)))
        {
            var sourceName = Attribute(sourceAttribute, "name");
            if (!string.IsNullOrWhiteSpace(sourceName))
                element.SetSourceAttribute(sourceName, Attribute(sourceAttribute, "value") ?? string.Empty);
        }
        element.visible = ParseBool(Attribute(node, "visible") ?? "true", true);
        element.SetEnabled(ParseBool(Attribute(node, "enabled") ?? "true", true));
        if (element is TextField textField)
        {
            textField.multiline = ParseBool(Attribute(node, "multiline") ?? "false");
            textField.scrollToEnd = ParseBool(Attribute(node, "scroll-to-end") ?? "false");
        }
        if (element is SearchField searchField)
        {
            searchField.placeholderText = Attribute(node, "placeholder-text") ?? "Search";
            searchField.showClearButton = ParseBool(Attribute(node, "show-clear-button") ?? "true", true);
        }
        if (element is TreeView treeView)
        {
            treeView.selectionType = ParseEnum(Attribute(node, "selection-type"), SelectionType.Single);
            treeView.fixedItemHeight = ParseFloat(Attribute(node, "fixed-item-height") ?? "22");
            treeView.showAlternatingRowBackgrounds = ParseBool(
                Attribute(node, "show-alternating-row-backgrounds") ?? "false");
            treeView.showBorder = ParseBool(Attribute(node, "show-border") ?? "false");
        }
        if (element is ListView listView)
        {
            listView.selectionType = ParseEnum(Attribute(node, "selection-type"), SelectionType.Single);
            listView.fixedItemHeight = ParseFloat(Attribute(node, "fixed-item-height") ?? "22");
            listView.showAlternatingRowBackgrounds = ParseBool(
                Attribute(node, "show-alternating-row-backgrounds") ?? "true", true);
            listView.showBorder = ParseBool(Attribute(node, "show-border") ?? "false");
        }
        if (element is ColorField colorField)
        {
            colorField.showAlpha = ParseBool(Attribute(node, "show-alpha") ?? "true", true);
            colorField.hdr = ParseBool(Attribute(node, "hdr") ?? "false");
        }
        if (element is BaseField<string> stringField)
            stringField.isReadOnly = ParseBool(Attribute(node, "read-only") ?? "false");
        else if (element is FloatField floatField)
            floatField.isReadOnly = ParseBool(Attribute(node, "read-only") ?? "false");
        else if (element is IntegerField integerField)
            integerField.isReadOnly = ParseBool(Attribute(node, "read-only") ?? "false");
        else if (element is ColorField colorValueField)
            colorValueField.isReadOnly = ParseBool(Attribute(node, "read-only") ?? "false");
        else if (element is Toggle toggleField)
            toggleField.isReadOnly = ParseBool(Attribute(node, "read-only") ?? "false");
        else if (element is Slider sliderField)
            sliderField.isReadOnly = ParseBool(Attribute(node, "read-only") ?? "false");
        foreach (var className in (Attribute(node, "class") ?? string.Empty)
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries)) element.AddToClassList(className);
        if (Attribute(node, "style") is { Length: > 0 } inlineStyle) inlineStyles[element] = inlineStyle;
        foreach (var child in node.Elements().Where(child =>
                     !child.Name.LocalName.Equals("Style", StringComparison.OrdinalIgnoreCase) &&
                     !child.Name.LocalName.Equals("SourceAttribute", StringComparison.OrdinalIgnoreCase) &&
                     !child.Name.LocalName.Equals(nameof(TreeViewItem), StringComparison.OrdinalIgnoreCase)))
            element.Add(CreateElement(child, inlineStyles));
        return element;
    }

    private static XElement ToElement(VisualElement element)
    {
        var node = new XElement(element.GetType().Name);
        if (!string.IsNullOrWhiteSpace(element.name)) node.SetAttributeValue("name", element.name);
        if (!string.IsNullOrWhiteSpace(element.tooltip)) node.SetAttributeValue("tooltip", element.tooltip);
        if (!string.IsNullOrWhiteSpace(element.sourceTag)) node.SetAttributeValue("source-tag", element.sourceTag);
        if (!string.IsNullOrEmpty(element.sourceText)) node.SetAttributeValue("source-text", element.sourceText);
        if (element.classList.Count > 0) node.SetAttributeValue("class", string.Join(' ', element.classList));
        if (!element.visible) node.SetAttributeValue("visible", false);
        if (!element.enabledSelf) node.SetAttributeValue("enabled", false);
        if (element.sortingLayer != SortingLayer.Ui)
            node.SetAttributeValue("sorting-layer", element.sortingLayer);
        if (element.orderInLayer != 0) node.SetAttributeValue("order-in-layer", element.orderInLayer);
        if (!string.IsNullOrWhiteSpace(element.atlas)) node.SetAttributeValue("atlas", element.atlas);
        if (element.material is { } material)
            node.SetAttributeValue("material-shader", material.shader.shaderName);
        switch (element)
        {
            case TextElement text: node.SetAttributeValue("text", text.text); break;
            case TextField field:
                WriteField(node, field.label, field.value, field.isReadOnly);
                if (field.multiline) node.SetAttributeValue("multiline", true);
                if (field.scrollToEnd) node.SetAttributeValue("scroll-to-end", true);
                if (field is SearchField search)
                {
                    if (search.placeholderText != "Search")
                        node.SetAttributeValue("placeholder-text", search.placeholderText);
                    if (!search.showClearButton) node.SetAttributeValue("show-clear-button", false);
                }
                break;
            case FloatField field:
                WriteField(node, field.label, field.value.ToString(CultureInfo.InvariantCulture), field.isReadOnly);
                break;
            case IntegerField field:
                WriteField(node, field.label, field.value.ToString(CultureInfo.InvariantCulture), field.isReadOnly);
                break;
            case ColorField field:
                WriteField(node, field.label, FormatEngineColor(field.value), field.isReadOnly);
                if (!field.showAlpha) node.SetAttributeValue("show-alpha", false);
                if (field.hdr) node.SetAttributeValue("hdr", true);
                break;
            case DropdownField field:
                WriteField(node, field.label, field.value, field.isReadOnly);
                node.SetAttributeValue("choices", string.Join(',', field.choices));
                break;
            case Slider field:
                WriteField(node, field.label, field.value.ToString(CultureInfo.InvariantCulture), field.isReadOnly);
                node.SetAttributeValue("low-value", field.lowValue.ToString(CultureInfo.InvariantCulture));
                node.SetAttributeValue("high-value", field.highValue.ToString(CultureInfo.InvariantCulture));
                break;
            case Toggle field:
                WriteField(node, field.label, field.value.ToString(CultureInfo.InvariantCulture), field.isReadOnly);
                break;
            case Image image: node.SetAttributeValue("src", image.sourcePath); break;
            case ProgressBar progress:
                node.SetAttributeValue("value", progress.value.ToString(CultureInfo.InvariantCulture));
                node.SetAttributeValue("low-value", progress.lowValue.ToString(CultureInfo.InvariantCulture));
                node.SetAttributeValue("high-value", progress.highValue.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(progress.title)) node.SetAttributeValue("title", progress.title);
                break;
            case TreeView tree:
                WriteCollectionAttributes(node, tree.selectionType, tree.fixedItemHeight,
                    tree.showAlternatingRowBackgrounds, tree.showBorder);
                foreach (var item in tree.items) node.Add(ToTreeItemElement(item));
                break;
            case ListView list:
                WriteCollectionAttributes(node, list.selectionType, list.fixedItemHeight,
                    list.showAlternatingRowBackgrounds, list.showBorder);
                node.SetAttributeValue("items", string.Join(',', list.itemsSource.Cast<object?>()
                    .Select(list.makeItemText)));
                break;
        }
        var inlineStyle = SerializeStyle(element.style);
        if (inlineStyle.Length > 0) node.SetAttributeValue("style", inlineStyle);
        foreach (var attribute in element.sourceAttributes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            node.Add(new XElement("SourceAttribute", new XAttribute("name", attribute.Key),
                new XAttribute("value", attribute.Value)));
        foreach (var child in element.Children) node.Add(ToElement(child));
        return node;
    }

    private static void WriteField(XElement node, string label, string value, bool readOnly)
    {
        if (!string.IsNullOrWhiteSpace(label)) node.SetAttributeValue("label", label);
        node.SetAttributeValue("value", value);
        if (readOnly) node.SetAttributeValue("read-only", true);
    }

    private static DropdownField CreateDropdown(string label, string value, string choices)
    {
        var field = new DropdownField(label,
            choices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        field.SetValueWithoutNotify(value);
        return field;
    }

    private static TreeView CreateTreeView(XElement node) => new()
    {
        items = node.Elements().Where(child => child.Name.LocalName.Equals(nameof(TreeViewItem),
                StringComparison.OrdinalIgnoreCase)).Select(ParseTreeItem).ToArray()
    };

    private static TreeViewItem ParseTreeItem(XElement node)
    {
        var text = Attribute(node, "text") ?? node.Value.Trim();
        var id = ParseInt(Attribute(node, "id") ??
                          StringComparer.Ordinal.GetHashCode(text).ToString(CultureInfo.InvariantCulture));
        var children = node.Elements().Where(child => child.Name.LocalName.Equals(nameof(TreeViewItem),
            StringComparison.OrdinalIgnoreCase)).Select(ParseTreeItem).ToArray();
        return new TreeViewItem(id, text, text, children,
            Attribute(node, "icon") ?? string.Empty,
            Attribute(node, "expanded-icon") ?? string.Empty);
    }

    private static ListView CreateListView(XElement node) => new()
    {
        itemsSource = (Attribute(node, "items") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    };

    private static XElement ToTreeItemElement(TreeViewItem item)
    {
        var node = new XElement(nameof(TreeViewItem),
            new XAttribute("id", item.Id), new XAttribute("text", item.Text));
        if (!string.IsNullOrWhiteSpace(item.IconPath)) node.SetAttributeValue("icon", item.IconPath);
        if (!string.IsNullOrWhiteSpace(item.ExpandedIconPath))
            node.SetAttributeValue("expanded-icon", item.ExpandedIconPath);
        foreach (var child in item.Children ?? []) node.Add(ToTreeItemElement(child));
        return node;
    }

    private static void WriteCollectionAttributes(
        XElement node,
        SelectionType selectionType,
        float fixedItemHeight,
        bool alternatingRows,
        bool showBorder)
    {
        if (selectionType != SelectionType.Single) node.SetAttributeValue("selection-type", selectionType);
        if (Math.Abs(fixedItemHeight - 22) > float.Epsilon)
            node.SetAttributeValue("fixed-item-height", fixedItemHeight.ToString(CultureInfo.InvariantCulture));
        if (alternatingRows) node.SetAttributeValue("show-alternating-row-backgrounds", true);
        if (showBorder) node.SetAttributeValue("show-border", true);
    }

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;

    private static string SerializeStyle(Style style)
    {
        var declarations = new List<string>();
        void Add(string name, object value) => declarations.Add($"{name}: {value}");
        static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        static string EnumName<T>(T value) where T : struct, Enum =>
            Regex.Replace(value.ToString(), "([a-z])([A-Z])", "$1-$2").ToLowerInvariant();
        if (style.flexDirection != FlexDirection.Column) Add("flex-direction", EnumName(style.flexDirection));
        if (style.display != DisplayStyle.Flex) Add("display", EnumName(style.display));
        if (style.alignItems != Align.Stretch) Add("align-items", EnumName(style.alignItems));
        if (style.justifyContent != Justify.FlexStart) Add("justify-content", EnumName(style.justifyContent));
        if (style.overflow != Overflow.Visible) Add("overflow", EnumName(style.overflow));
        if (style.width != 0) Add("width", $"{Number(style.width)}px");
        if (style.height != 0) Add("height", $"{Number(style.height)}px");
        if (style.minWidth != 0) Add("min-width", $"{Number(style.minWidth)}px");
        if (style.minHeight != 0) Add("min-height", $"{Number(style.minHeight)}px");
        if (!float.IsPositiveInfinity(style.maxWidth)) Add("max-width", $"{Number(style.maxWidth)}px");
        if (!float.IsPositiveInfinity(style.maxHeight)) Add("max-height", $"{Number(style.maxHeight)}px");
        if (style.flexGrow != 0) Add("flex-grow", Number(style.flexGrow));
        if (style.flexShrink != 1) Add("flex-shrink", Number(style.flexShrink));
        if (style.marginLeft != 0) Add("margin-left", $"{Number(style.marginLeft)}px");
        if (style.marginTop != 0) Add("margin-top", $"{Number(style.marginTop)}px");
        if (style.marginRight != 0) Add("margin-right", $"{Number(style.marginRight)}px");
        if (style.marginBottom != 0) Add("margin-bottom", $"{Number(style.marginBottom)}px");
        if (style.paddingLeft != 0) Add("padding-left", $"{Number(style.paddingLeft)}px");
        if (style.paddingTop != 0) Add("padding-top", $"{Number(style.paddingTop)}px");
        if (style.paddingRight != 0) Add("padding-right", $"{Number(style.paddingRight)}px");
        if (style.paddingBottom != 0) Add("padding-bottom", $"{Number(style.paddingBottom)}px");
        if (style.fontSize != 0) Add("font-size", $"{Number(style.fontSize)}px");
        if (style.borderWidth != 0) Add("border-width", $"{Number(style.borderWidth)}px");
        if (style.color is { } color) Add("color", FormatColor(color));
        if (style.backgroundColor is { } background) Add("background-color", FormatColor(background));
        if (style.borderColor is { } border) Add("border-color", FormatColor(border));
        return string.Join("; ", declarations);
    }

    private static string FormatColor(UIColor value) =>
        $"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}";

    private static string FormatEngineColor(BEngine.Color value) =>
        $"#{ToByte(value.r):X2}{ToByte(value.g):X2}{ToByte(value.b):X2}{ToByte(value.a):X2}";

    private static byte ToByte(Fix64 value) => (byte)Math.Clamp((int)Math.Round(
        Math.Clamp((double)value, 0, 1) * 255), 0, 255);

    private static BEngine.Color ParseEngineColor(string value)
    {
        var text = value.Trim().TrimStart('#');
        if (text.Length is not (6 or 8) || !uint.TryParse(text, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var packed)) return BEngine.Color.white;
        if (text.Length == 6) packed = packed << 8 | 0xFF;
        return new BEngine.Color((Fix64)((byte)(packed >> 24) / 255f),
            (Fix64)((byte)(packed >> 16) / 255f), (Fix64)((byte)(packed >> 8) / 255f),
            (Fix64)((byte)packed / 255f));
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    private static float ParseFloat(string value) => float.TryParse(value, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    private static int ParseInt(string value) => int.TryParse(value, NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    private static bool ParseBool(string value, bool fallback = false) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;
}
