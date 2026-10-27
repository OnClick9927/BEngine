using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BEngine.UIElements;

internal static class UxmlSerializer
{
    internal static string SerializeElement(VisualElement root) => ToElement(root).ToString();

    internal static VisualElement DeserializeElement(string xml) =>
        new UxmlTemplate(string.Empty, XElement.Parse(xml), [], fileBacked: false).Instantiate();

    internal static UxmlTemplate LoadTemplate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fileBacked = RuntimeServiceAccess.FileSystem.FileExists(path);
        var sourcePath = fileBacked
            ? RuntimeServiceAccess.PathService.GetFullPath(path)
            : path.Replace('\\', '/').TrimStart('/');
        var document = fileBacked
            ? LoadFileDocument(sourcePath)
            : XDocument.Parse(Resources.Load<string>(sourcePath) ??
                              throw new FileNotFoundException("Bundled UXML document was not found.", sourcePath),
                LoadOptions.SetLineInfo);
        return CreateTemplate(document, sourcePath, fileBacked);
    }

    internal static UxmlTemplate ParseTemplate(string xml, string sourcePath) =>
        CreateTemplate(XDocument.Parse(xml, LoadOptions.SetLineInfo), sourcePath,
            fileBacked: RuntimeServiceAccess.PathService.IsPathRooted(sourcePath));

    private static UxmlTemplate CreateTemplate(XDocument document, string sourcePath, bool fileBacked)
    {
        if (!fileBacked) sourcePath = sourcePath.Replace('\\', '/');
        var documentRoot = document.Root ?? throw new InvalidDataException("UXML document has no root element.");
        var baseDirectory = fileBacked
            ? RuntimeServiceAccess.PathService.GetDirectoryName(sourcePath)!
            : ResourceDirectory(sourcePath);
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
            var stylePath = fileBacked
                ? source.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                  source.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                    ? AssetReferencePath.Resolve(source)
                    : RuntimeServiceAccess.PathService.GetFullPath(
                    RuntimeServiceAccess.PathService.Combine(baseDirectory, source))
                : CombineResourcePath(baseDirectory, source);
            var sheet = fileBacked
                ? StyleSheet.Load(stylePath)
                : BAsset.Load<StyleSheet>(stylePath) ?? StyleSheet.Parse(Resources.Load<string>(stylePath) ??
                                   throw new FileNotFoundException("Bundled USS document was not found.", stylePath));
            styles.Add((stylePath, sheet));
        }
        return new UxmlTemplate(sourcePath, new XElement(rootNode), styles, fileBacked);
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
            IReadOnlyList<(string Path, StyleSheet Sheet)> styles,
            bool fileBacked = true)
        {
            _rootNode = rootNode;
            _styles = styles;
            _writeTimes = fileBacked
                ? styles.Select(style => style.Path).Append(path).Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(file => file,
                        file => RuntimeServiceAccess.FileSystem.GetLastWriteTimeUtc(file),
                        StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        internal object Capture() => new object[] { _rootNode.ToString(),
            _styles.Select(item => new object[] { item.Path, item.Sheet }).ToArray() };
        internal static UxmlTemplate Restore(object data, Func<BObject, BObject> cloneObject, ICollection<StyleSheet> ownedStyles)
        {
            var values = (object[])data;
            var styles = new List<(string Path, StyleSheet Sheet)>();
            foreach (var item in (object[][])values[1])
            {
                var original = (BObject)item[1];
                var sheet = (StyleSheet)cloneObject(original);
                if (ReferenceEquals(sheet, original)) sheet = (StyleSheet)InstantiateSheet(original);
                ownedStyles.Add(sheet);
                styles.Add(((string)item[0], sheet));
            }
            return new UxmlTemplate(string.Empty, XElement.Parse((string)values[0]), styles, fileBacked: false);

            static BObject InstantiateSheet(BObject source) => BObject.Instantiate(source);
        }

        internal bool IsCurrent => _writeTimes.Count == 0 || _writeTimes.All(pair =>
            RuntimeServiceAccess.FileSystem.FileExists(pair.Key) &&
            RuntimeServiceAccess.FileSystem.GetLastWriteTimeUtc(pair.Key) == pair.Value);

        internal UxmlTemplate Clone(ICollection<StyleSheet> ownedStyles)
        {
            var styles = new List<(string Path, StyleSheet Sheet)>();
            foreach (var (path, sheet) in _styles)
            {
                var clone = sheet.CloneAsset();
                ownedStyles.Add(clone);
                styles.Add((path, clone));
            }
            return new UxmlTemplate(string.Empty, new XElement(_rootNode), styles, fileBacked: false);
        }

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

    private static string ResourceDirectory(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static string CombineResourcePath(string directory, string relative)
    {
        var normalized = relative.Replace('\\', '/').Trim();
        if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return normalized;
        var combined = string.IsNullOrEmpty(directory) ? normalized : $"{directory}/{normalized}";
        var segments = new List<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    throw new InvalidDataException("USS path escapes the bundled UXML directory.");
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    internal static void Save(VisualElement root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        var fullPath = RuntimeServiceAccess.PathService.GetFullPath(path);
        var directory = RuntimeServiceAccess.PathService.GetDirectoryName(fullPath) ??
                        RuntimeServiceAccess.PlatformService.CurrentDirectory;
        var documentRoot = new XElement("UXML");
        foreach (var sheet in root.styleSheets.DistinctBy(
                     static sheet => sheet.assetPath, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(sheet.assetPath)) continue;
            var sheetPath = AssetReferencePath.Resolve(sheet.assetPath);
            var relative = RuntimeServiceAccess.PathService.GetRelativePath(directory, sheetPath)
                .Replace('\\', '/');
            documentRoot.Add(new XElement("Style", new XAttribute("src", relative)));
        }
        documentRoot.Add(ToElement(root));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), documentRoot);
        RuntimeServiceAccess.FileSystem.CreateDirectory(directory);
        using var stream = RuntimeServiceAccess.FileSystem.OpenFile(
            fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan);
        document.Save(stream);
        RuntimeServiceAccess.FileSystem.Flush(stream, flushToDisk: true);
    }

    private static XDocument LoadFileDocument(string path)
    {
        using var stream = RuntimeServiceAccess.FileSystem.OpenRead(path);
        return XDocument.Load(stream, LoadOptions.SetLineInfo);
    }

    private static VisualElement CreateElement(XElement node, IDictionary<VisualElement, string> inlineStyles)
    {
        var type = node.Name.LocalName;
        var label = Attribute(node, "label") ?? string.Empty;
        var text = Attribute(node, "text") ?? node.Value.Trim();
        var value = Attribute(node, "value") ?? string.Empty;
        var element = UxmlFactoryRegistry.Create(node) ?? (type switch
        {
            nameof(VisualElement) => new VisualElement(),
            nameof(Vector2Field) => new Vector2Field(label) { value = StandardControlValues.Vector2(value) },
            nameof(Vector4Field) => new Vector4Field(label) { value = StandardControlValues.Vector4(value) },
            nameof(RadioButton) => new RadioButton(label) { value = ParseBool(value) },
            nameof(ToolbarToggle) => new ToolbarToggle(label) { value = ParseBool(value) },
            nameof(PasswordField) => new PasswordField(label) { value = value },
            nameof(TextArea) => new TextArea(label) { value = value },
            nameof(HelpBox) => new HelpBox(text),
            nameof(Separator) => new Separator(),
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
            _ => node.Name.NamespaceName.Length == 0 ? new VisualElement() :
                throw new InvalidDataException($"No UXML factory is registered for '{node.Name}'.")
        });
        element.name = Attribute(node, "name") ?? string.Empty;
        element.bindingPath = Attribute(node, "binding-path") ?? string.Empty;
        element.tooltip = Attribute(node, "tooltip") ?? string.Empty;
        element.accessibilityRole = ParseEnum(Attribute(node, "accessibility-role"), AccessibilityRole.Auto);
        element.accessibilityName = Attribute(node, "accessibility-name") ?? string.Empty;
        element.accessibilityDescription = Attribute(node, "accessibility-description") ?? string.Empty;
        element.accessibilityValue = Attribute(node, "accessibility-value") ?? string.Empty;
        element.accessibilityHidden = ParseBool(Attribute(node, "accessibility-hidden") ?? "false");
        element.accessibilityLiveRegion = ParseEnum(
            Attribute(node, "accessibility-live"), AccessibilityLiveRegion.Off);
        element.focusable = ParseBool(Attribute(node, "focusable") ?? "false");
        element.tabIndex = ParseInt(Attribute(node, "tab-index") ?? "-1");
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
            textField.placeholderText = Attribute(node, "placeholder-text") ?? textField.placeholderText;
            textField.isPasswordField = ParseBool(Attribute(node, "password") ?? "false");
            textField.multiline = ParseBool(Attribute(node, "multiline") ?? "false");
            textField.scrollToEnd = ParseBool(Attribute(node, "scroll-to-end") ?? "false");
            textField.maxLength = ParseInt(Attribute(node, "max-length") ?? "-1");
            var mask = Attribute(node, "mask-character");
            if (mask is not null)
            {
                if (mask.Length != 1) throw new InvalidDataException("Text field mask-character must be one character.");
                textField.maskCharacter = mask[0];
            }
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
        if (element is Vector2Field vector2Field)
            vector2Field.isReadOnly = ParseBool(Attribute(node, "read-only") ?? "false");
        if (element is Vector4Field vector4Field)
            vector4Field.isReadOnly = ParseBool(Attribute(node, "read-only") ?? "false");
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
        var node = UxmlFactoryRegistry.Serialize(element) ?? new XElement(element.GetType().Name);
        if (!string.IsNullOrWhiteSpace(element.name)) node.SetAttributeValue("name", element.name);
        if (!string.IsNullOrWhiteSpace(element.bindingPath)) node.SetAttributeValue("binding-path", element.bindingPath);
        if (!string.IsNullOrWhiteSpace(element.tooltip)) node.SetAttributeValue("tooltip", element.tooltip);
        if (element.accessibilityRole != AccessibilityRole.Auto)
            node.SetAttributeValue("accessibility-role", element.accessibilityRole);
        if (element.accessibilityName.Length > 0)
            node.SetAttributeValue("accessibility-name", element.accessibilityName);
        if (element.accessibilityDescription.Length > 0)
            node.SetAttributeValue("accessibility-description", element.accessibilityDescription);
        if (element.accessibilityValue.Length > 0)
            node.SetAttributeValue("accessibility-value", element.accessibilityValue);
        if (element.accessibilityHidden) node.SetAttributeValue("accessibility-hidden", true);
        if (element.accessibilityLiveRegion != AccessibilityLiveRegion.Off)
            node.SetAttributeValue("accessibility-live", element.accessibilityLiveRegion);
        if (element.focusable) node.SetAttributeValue("focusable", true);
        if (element.tabIndex >= 0) node.SetAttributeValue("tab-index", element.tabIndex);
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
            case Vector2Field field:
                WriteField(node, field.label, StandardControlValues.Format(field.value), field.isReadOnly); break;
            case Vector4Field field:
                WriteField(node, field.label, StandardControlValues.Format(field.value), field.isReadOnly); break;
            case TextElement text: node.SetAttributeValue("text", text.text); break;
            case TextField field:
                WriteField(node, field.label, field.value, field.isReadOnly);
                if (field is not SearchField && field.placeholderText.Length > 0)
                    node.SetAttributeValue("placeholder-text", field.placeholderText);
                if (field.isPasswordField) node.SetAttributeValue("password", true);
                if (field.multiline) node.SetAttributeValue("multiline", true);
                if (field.scrollToEnd) node.SetAttributeValue("scroll-to-end", true);
                if (field.maxLength >= 0) node.SetAttributeValue("max-length", field.maxLength);
                if (field.maskCharacter != '*') node.SetAttributeValue("mask-character", field.maskCharacter);
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
        if (element is not (Vector2Field or Vector4Field) && UxmlFactoryRegistry.SerializeChildren(element))
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
        static string Number(Fix64 value) => value.ToString(null, CultureInfo.InvariantCulture);
        static string EnumName<T>(T value) where T : struct, Enum =>
            Regex.Replace(value.ToString(), "([a-z])([A-Z])", "$1-$2").ToLowerInvariant();
        if (style.IsInline(nameof(style.flexDirection))) Add("flex-direction", EnumName(style.flexDirection));
        if (style.IsInline(nameof(style.display))) Add("display", EnumName(style.display));
        if (style.IsInline(nameof(style.alignItems))) Add("align-items", EnumName(style.alignItems));
        if (style.IsInline(nameof(style.justifyContent))) Add("justify-content", EnumName(style.justifyContent));
        if (style.IsInline(nameof(style.overflow))) Add("overflow", EnumName(style.overflow));
        if (style.IsInline(nameof(style.position))) Add("position", EnumName(style.position));
        if (style.IsInline(nameof(style.left)) && style.left is { } left) Add("left", $"{Number(left)}px");
        if (style.IsInline(nameof(style.top)) && style.top is { } top) Add("top", $"{Number(top)}px");
        if (style.IsInline(nameof(style.right)) && style.right is { } right) Add("right", $"{Number(right)}px");
        if (style.IsInline(nameof(style.bottom)) && style.bottom is { } bottom) Add("bottom", $"{Number(bottom)}px");
        if (style.IsInline(nameof(style.width))) Add("width", $"{Number(style.width)}px");
        if (style.IsInline(nameof(style.height))) Add("height", $"{Number(style.height)}px");
        if (style.IsInline(nameof(style.minWidth))) Add("min-width", $"{Number(style.minWidth)}px");
        if (style.IsInline(nameof(style.minHeight))) Add("min-height", $"{Number(style.minHeight)}px");
        if (style.IsInline(nameof(style.maxWidth)) && style.maxWidth is { } maxWidth) Add("max-width", $"{Number(maxWidth)}px");
        if (style.IsInline(nameof(style.maxHeight)) && style.maxHeight is { } maxHeight) Add("max-height", $"{Number(maxHeight)}px");
        if (style.IsInline(nameof(style.flexGrow))) Add("flex-grow", Number(style.flexGrow));
        if (style.IsInline(nameof(style.flexShrink))) Add("flex-shrink", Number(style.flexShrink));
        if (style.IsInline(nameof(style.marginLeft))) Add("margin-left", $"{Number(style.marginLeft)}px");
        if (style.IsInline(nameof(style.marginTop))) Add("margin-top", $"{Number(style.marginTop)}px");
        if (style.IsInline(nameof(style.marginRight))) Add("margin-right", $"{Number(style.marginRight)}px");
        if (style.IsInline(nameof(style.marginBottom))) Add("margin-bottom", $"{Number(style.marginBottom)}px");
        if (style.IsInline(nameof(style.paddingLeft))) Add("padding-left", $"{Number(style.paddingLeft)}px");
        if (style.IsInline(nameof(style.paddingTop))) Add("padding-top", $"{Number(style.paddingTop)}px");
        if (style.IsInline(nameof(style.paddingRight))) Add("padding-right", $"{Number(style.paddingRight)}px");
        if (style.IsInline(nameof(style.paddingBottom))) Add("padding-bottom", $"{Number(style.paddingBottom)}px");
        if (style.IsInline(nameof(style.fontSize))) Add("font-size", $"{Number(style.fontSize)}px");
        if (style.IsInline(nameof(style.borderWidth))) Add("border-width", $"{Number(style.borderWidth)}px");
        if (style.IsInline(nameof(style.color)) && style.color is { } color) Add("color", FormatColor(color));
        if (style.IsInline(nameof(style.backgroundColor)) && style.backgroundColor is { } background)
            Add("background-color", FormatColor(background));
        if (style.IsInline(nameof(style.borderColor)) && style.borderColor is { } border)
            Add("border-color", FormatColor(border));
        foreach (var (property, expression) in style.ExportCssDeclarations()) Add(property, expression);
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
