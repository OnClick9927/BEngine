using System.Globalization;
using BEngine.Serialization;

namespace BEngine.UIElements;

public enum PanelScaleMode
{
    ConstantPixelSize,
    ScaleWithScreenSize
}

public sealed class PanelSettings : ScriptableObject
{
    public PanelScaleMode scaleMode { get; set; } = PanelScaleMode.ScaleWithScreenSize;
    public int referenceWidth { get; set; } = 1280;
    public int referenceHeight { get; set; } = 720;

    public Fix64 ResolveScale(int width, int height)
    {
        if (scaleMode == PanelScaleMode.ConstantPixelSize) return Fix64.One;
        var widthScale = (Fix64)Math.Max(1, width) / Math.Max(1, referenceWidth);
        var heightScale = (Fix64)Math.Max(1, height) / Math.Max(1, referenceHeight);
        return (widthScale + heightScale) / 2;
    }
}

[AddComponentMenu("UI Toolkit/UI Document")]
[DisallowMultipleComponent]
public sealed class UIDocument : MonoBehaviour
{
    private VisualElement? _root;
    private string _loadedSource = string.Empty;

    public string sourceAsset { get; set; } = string.Empty;
    public int sortingOrder { get; set; }
    public bool interactable { get; set; } = true;
    public PanelScaleMode scaleMode { get; set; } = PanelScaleMode.ScaleWithScreenSize;
    public int referenceWidth { get; set; } = 1280;
    public int referenceHeight { get; set; } = 720;
    public VisualElement rootVisualElement => EnsureRoot();

    public override void OnEnable() => Reload();

    public override void Update()
    {
        if (!interactable || !Input.GetMouseButtonDown(0)) return;
        var scene = gameObject.scene;
        if (scene is null) return;
        var documents = scene.gameObjects
            .Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<UIDocument>())
            .Where(item => item.enabled && item.interactable)
            .OrderByDescending(item => item.sortingOrder)
            .ToArray();
        if (!ReferenceEquals(documents.FirstOrDefault(), this)) return;

        var position = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        _ = DispatchPointerDown(position, Screen.width, Screen.height);
    }

    public void Reload()
    {
        _loadedSource = sourceAsset;
        if (string.IsNullOrWhiteSpace(sourceAsset))
        {
            _root = CreateFallbackRoot();
            return;
        }

        try
        {
            _root = VisualTreeAsset.Load(ResolveSourcePath(sourceAsset)).Instantiate();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Debug.LogError($"Could not load UI document '{sourceAsset}': {exception.Message}");
            _root = CreateFallbackRoot();
        }
    }

    public void SetVisualTree(VisualElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        _loadedSource = sourceAsset;
    }

    public bool DispatchPointerDown(Vector2 position, int viewportWidth, int viewportHeight)
    {
        if (!interactable) return false;
        var renderList = UIRenderListBuilder.Build(
            EnsureRoot(), viewportWidth, viewportHeight, ResolveScale(viewportWidth, viewportHeight));
        for (var target = renderList.Pick(position); target is not null; target = target.parent)
        {
            if (target is not Button button) continue;
            button.Click();
            return true;
        }
        return false;
    }

    internal Fix64 ResolveScale(int width, int height)
    {
        if (scaleMode == PanelScaleMode.ConstantPixelSize) return Fix64.One;
        var widthScale = (Fix64)Math.Max(1, width) / Math.Max(1, referenceWidth);
        var heightScale = (Fix64)Math.Max(1, height) / Math.Max(1, referenceHeight);
        return (widthScale + heightScale) / 2;
    }

    private VisualElement EnsureRoot()
    {
        if (_root is null || !string.Equals(_loadedSource, sourceAsset, StringComparison.Ordinal)) Reload();
        return _root!;
    }

    private static VisualElement CreateFallbackRoot()
    {
        var root = new VisualElement { name = "Root" };
        root.style.flexGrow = 1;
        return root;
    }

    private static string ResolveSourcePath(string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            var dataPath = Path.GetFullPath(Application.dataPath);
            var projectRoot = Path.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(dataPath)?.FullName ?? Directory.GetCurrentDirectory()
                : Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(projectRoot, normalized));
        }
        return Path.GetFullPath(Path.Combine(Application.dataPath, normalized));
    }
}

public sealed class VisualTreeAsset : ScriptableObject
{
    private UIAssetDocument _document = UIAssetSerializer.CreateDefaultDocument();

    public string assetPath { get; private set; } = string.Empty;

    public static VisualTreeAsset Create(VisualElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new VisualTreeAsset { _document = UIAssetSerializer.ToDocument(root) };
    }

    public static VisualTreeAsset Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var asset = new VisualTreeAsset
        {
            assetPath = Path.GetFullPath(path),
            _document = YamlUtility.Load<UIAssetDocument>(path)
        };
        asset.Validate();
        return asset;
    }

    public VisualElement Instantiate() => UIAssetSerializer.CreateElement(_document.Root);

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate();
        YamlUtility.Save(_document, path);
        assetPath = Path.GetFullPath(path);
    }

    private void Validate()
    {
        if (_document.Format != "BEngine.UI")
            throw new InvalidDataException($"Unsupported UI document format '{_document.Format}'.");
        if (_document.Version != 1)
            throw new InvalidDataException($"Unsupported UI document version {_document.Version}.");
        if (_document.Root is null) throw new InvalidDataException("UI document has no root element.");
    }
}

public readonly record struct UIElementRect(Fix64 X, Fix64 Y, Fix64 Width, Fix64 Height)
{
    public bool Contains(Vector2 point) => point.x >= X && point.x <= X + Width &&
                                           point.y >= Y && point.y <= Y + Height;
}

public readonly record struct UIElementLayout(VisualElement Element, UIElementRect Rect);

public static class UIElementLayoutEngine
{
    public static IReadOnlyList<UIElementLayout> Calculate(
        VisualElement root,
        int viewportWidth,
        int viewportHeight,
        Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var result = new List<UIElementLayout>();
        Layout(root, new UIElementRect(0, 0, Math.Max(1, viewportWidth), Math.Max(1, viewportHeight)),
            scale ?? Fix64.One, result);
        return result;
    }

    private static void Layout(
        VisualElement element,
        UIElementRect rect,
        Fix64 scale,
        ICollection<UIElementLayout> output)
    {
        if (!element.visible || element.style.display == DisplayStyle.None) return;
        output.Add(new UIElementLayout(element, rect));
        var children = element.Children.Where(child => child.visible && child.style.display != DisplayStyle.None).ToArray();
        if (children.Length == 0) return;

        var left = Scale(element.style.paddingLeft, scale);
        var top = Scale(element.style.paddingTop, scale);
        var right = Scale(element.style.paddingRight, scale);
        var bottom = Scale(element.style.paddingBottom, scale);
        var content = new UIElementRect(rect.X + left, rect.Y + top,
            Fix64.Max(0, rect.Width - left - right), Fix64.Max(0, rect.Height - top - bottom));
        var horizontal = element.style.flexDirection == FlexDirection.Row;
        var availableMain = horizontal ? content.Width : content.Height;
        var fixedMain = Fix64.Zero;
        var totalGrow = Fix64.Zero;
        foreach (var child in children)
        {
            fixedMain += MainMargins(child, horizontal, scale);
            var explicitSize = MainSize(child, horizontal, scale);
            if (explicitSize > 0) fixedMain += explicitSize;
            else if (child.style.flexGrow <= 0) fixedMain += DefaultMainSize(child, horizontal, scale);
            else totalGrow += (Fix64)child.style.flexGrow;
        }

        var remaining = Fix64.Max(0, availableMain - fixedMain);
        var allocatedGrow = totalGrow > 0 ? remaining : Fix64.Zero;
        var unusedMain = Fix64.Max(0, remaining - allocatedGrow);
        var mainOffset = element.style.justifyContent switch
        {
            Justify.Center => unusedMain / 2,
            Justify.FlexEnd => unusedMain,
            _ => Fix64.Zero
        };
        var gap = element.style.justifyContent == Justify.SpaceBetween && children.Length > 1
            ? unusedMain / (children.Length - 1)
            : Fix64.Zero;
        var cursor = (horizontal ? content.X : content.Y) + mainOffset;
        foreach (var child in children)
        {
            var leadingMargin = Scale(horizontal ? child.style.marginLeft : child.style.marginTop, scale);
            var trailingMargin = Scale(horizontal ? child.style.marginRight : child.style.marginBottom, scale);
            cursor += leadingMargin;
            var main = MainSize(child, horizontal, scale);
            if (main <= 0)
            {
                main = child.style.flexGrow > 0 && totalGrow > 0
                    ? remaining * (Fix64)child.style.flexGrow / totalGrow
                    : DefaultMainSize(child, horizontal, scale);
            }

            var crossLeading = Scale(horizontal ? child.style.marginTop : child.style.marginLeft, scale);
            var crossTrailing = Scale(horizontal ? child.style.marginBottom : child.style.marginRight, scale);
            var crossAvailable = (horizontal ? content.Height : content.Width) - crossLeading - crossTrailing;
            var cross = CrossSize(child, horizontal, scale);
            if (cross <= 0)
            {
                cross = element.style.alignItems == Align.Stretch
                    ? Fix64.Max(0, crossAvailable)
                    : DefaultCrossSize(child, horizontal, scale, crossAvailable);
            }
            var crossOffset = element.style.alignItems switch
            {
                Align.Center => Fix64.Max(0, crossAvailable - cross) / 2,
                Align.FlexEnd => Fix64.Max(0, crossAvailable - cross),
                _ => Fix64.Zero
            };
            var childRect = horizontal
                ? new UIElementRect(cursor, content.Y + crossLeading + crossOffset, main, cross)
                : new UIElementRect(content.X + crossLeading + crossOffset, cursor, cross, main);
            Layout(child, childRect, scale, output);
            cursor += main + trailingMargin + gap;
        }
    }

    private static Fix64 MainSize(VisualElement element, bool horizontal, Fix64 scale) =>
        Scale(horizontal ? element.style.width : element.style.height, scale);

    private static Fix64 CrossSize(VisualElement element, bool horizontal, Fix64 scale) =>
        Scale(horizontal ? element.style.height : element.style.width, scale);

    private static Fix64 MainMargins(VisualElement element, bool horizontal, Fix64 scale) =>
        Scale(horizontal
            ? element.style.marginLeft + element.style.marginRight
            : element.style.marginTop + element.style.marginBottom, scale);

    private static Fix64 DefaultMainSize(VisualElement element, bool horizontal, Fix64 scale)
    {
        if (horizontal) return Scale(element is Image ? 100 : 120, scale);
        return Scale(element switch
        {
            Label => 24,
            Button => 32,
            TextField => 28,
            Toggle => 24,
            Slider => 28,
            Image => 100,
            _ => 32
        }, scale);
    }

    private static Fix64 DefaultCrossSize(
        VisualElement element,
        bool horizontal,
        Fix64 scale,
        Fix64 available) => Fix64.Min(available, horizontal
        ? DefaultMainSize(element, horizontal: false, scale)
        : DefaultMainSize(element, horizontal: true, scale));

    private static Fix64 Scale(float value, Fix64 scale) => value <= 0 ? Fix64.Zero : (Fix64)value * scale;
}

public sealed class UIAssetDocument
{
    public string Format { get; set; } = "BEngine.UI";
    public int Version { get; set; } = 1;
    public UIElementDocument Root { get; set; } = new();
}

public sealed class UIElementDocument
{
    public string Type { get; set; } = nameof(VisualElement);
    public string Name { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public List<string> Classes { get; set; } = [];
    public UIStyleDocument Style { get; set; } = new();
    public List<UIElementDocument> Children { get; set; } = [];
}

public sealed class UIStyleDocument
{
    public FlexDirection FlexDirection { get; set; }
    public DisplayStyle Display { get; set; }
    public Align AlignItems { get; set; } = Align.Stretch;
    public Justify JustifyContent { get; set; }
    public Overflow Overflow { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float MinWidth { get; set; }
    public float MinHeight { get; set; }
    public float FlexGrow { get; set; }
    public float FlexShrink { get; set; } = 1;
    public float MarginLeft { get; set; }
    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }
    public float PaddingLeft { get; set; }
    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }
    public float FontSize { get; set; }
    public string Color { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
}

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
            nameof(TextField) => new TextField { value = document.Value },
            nameof(FloatField) => new FloatField { value = ParseFloat(document.Value) },
            nameof(IntegerField) => new IntegerField { value = ParseInt(document.Value) },
            nameof(Toggle) => new Toggle(document.Text) { value = ParseBool(document.Value) },
            nameof(Slider) => new Slider { value = ParseFloat(document.Value) },
            nameof(Image) => new Image { sourcePath = document.Value },
            nameof(ScrollView) => new ScrollView(),
            nameof(Toolbar) => new Toolbar(),
            _ => new VisualElement()
        };
        element.name = document.Name;
        element.tooltip = document.Tooltip;
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
            Classes = element.classList.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            Style = ToStyleDocument(element.style)
        };
        document.Children.AddRange(element.Children.Select(ToElementDocument));
        return document;
    }

    private static string SupportedTypeName(VisualElement element) => element switch
    {
        Label => nameof(Label),
        Button => nameof(Button),
        TextField => nameof(TextField),
        FloatField => nameof(FloatField),
        IntegerField => nameof(IntegerField),
        Toggle => nameof(Toggle),
        Slider => nameof(Slider),
        Image => nameof(Image),
        ScrollView => nameof(ScrollView),
        Toolbar => nameof(Toolbar),
        _ => nameof(VisualElement)
    };

    private static string ElementValue(VisualElement element) => element switch
    {
        TextField field => field.value,
        FloatField field => field.value.ToString(CultureInfo.InvariantCulture),
        IntegerField field => field.value.ToString(CultureInfo.InvariantCulture),
        Toggle field => field.value.ToString(CultureInfo.InvariantCulture),
        Slider field => field.value.ToString(CultureInfo.InvariantCulture),
        Image image => image.sourcePath,
        _ => string.Empty
    };

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
        Color = FormatColor(style.color),
        BackgroundColor = FormatColor(style.backgroundColor)
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
        style.color = ParseColor(value.Color);
        style.backgroundColor = ParseColor(value.BackgroundColor);
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
