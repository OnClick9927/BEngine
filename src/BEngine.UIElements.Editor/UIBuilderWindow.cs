using System.Globalization;
using BEngine;
using BEngine.Editor;
using BEngine.UIElements;
using UiButton = BEngine.UIElements.Button;
using UiLabel = BEngine.UIElements.Label;
using UiTreeView = BEngine.UIElements.TreeView;

namespace BEngine.UIElements.Editor;

internal sealed class UIBuilderWindow : EditorWindow
{
    private readonly Dictionary<int, VisualElement> _hierarchyElements = [];
    private VisualElement _documentRoot = CreateDefaultDocument();
    private VisualElement _selected;
    private VisualElement? _preview;
    private UiLabel? _status;
    private string _assetPath = string.Empty;
    private bool _dirty;

    public UIBuilderWindow()
    {
        _selected = _documentRoot;
        titleContent = new GUIContent("UI Builder");
        minSize = new Vector2(760, 420);
        position = new Rect(120, 90, 1000, 650);
    }

    [MenuItem("窗口/UI Builder", false, 230)]
    public static void Open()
    {
        var window = GetWindow<UIBuilderWindow>("UI Builder", true);
        window.ShowAuxWindow();
        window.Focus();
    }

    public static void Open(string path)
    {
        var window = GetWindow<UIBuilderWindow>("UI Builder", true);
        window.ShowAuxWindow();
        window.LoadDocument(path);
    }

    protected override void CreateGUI() => RebuildView();

    public override void AddItemsToMenu(GenericMenu menu)
    {
        menu.AddItem(new GUIContent("保存"), false, SaveDocument);
        menu.AddItem(new GUIContent("新建"), false, NewDocument);
        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("重新加载"), false, () =>
        {
            if (!string.IsNullOrWhiteSpace(_assetPath)) LoadDocument(_assetPath);
        });
    }

    [ContextMenu("保存 UI 文档")]
    private void SaveFromContextMenu() => SaveDocument();

    private void RebuildView()
    {
        var root = rootVisualElement;
        root.Clear();
        root.name = "UIBuilderRoot";
        root.style.flexGrow = 1;
        root.style.backgroundColor = new UIColor(30, 32, 35);

        root.Add(BuildToolbar());
        var content = new VisualElement();
        content.style.flexDirection = FlexDirection.Row;
        content.style.flexGrow = 1;
        content.Add(BuildHierarchy());
        content.Add(BuildPreview());
        content.Add(BuildInspector());
        root.Add(content);

        _status = new UiLabel(BuildStatusText());
        _status.style.height = 24;
        _status.style.paddingLeft = 8;
        _status.style.backgroundColor = new UIColor(37, 40, 44);
        root.Add(_status);
    }

    private VisualElement BuildToolbar()
    {
        var toolbar = new Toolbar();
        toolbar.style.height = 32;
        toolbar.Add(new UiButton(NewDocument, "新建") { tooltip = "新建 UI 文档" });
        toolbar.Add(new UiButton(SaveDocument, "保存") { tooltip = "保存为 YAML" });

        var assets = FindUiAssets();
        var picker = new DropdownField("文档", assets)
        {
            value = assets.Contains(_assetPath, StringComparer.OrdinalIgnoreCase)
                ? _assetPath
                : assets.FirstOrDefault() ?? string.Empty
        };
        picker.style.width = 340;
        picker.valueChanged += value =>
        {
            if (!string.IsNullOrWhiteSpace(value) && !value.Equals(_assetPath, StringComparison.OrdinalIgnoreCase))
                LoadDocument(value);
        };
        toolbar.Add(picker);
        toolbar.Add(new UiButton(() => AddElement(nameof(VisualElement)), "容器"));
        toolbar.Add(new UiButton(() => AddElement(nameof(UiLabel)), "文本"));
        toolbar.Add(new UiButton(() => AddElement(nameof(UiButton)), "按钮"));
        toolbar.Add(new UiButton(() => AddElement(nameof(TextField)), "输入框"));
        toolbar.Add(new UiButton(DeleteSelected, "删除") { tooltip = "删除选中的元素" });
        return toolbar;
    }

    private VisualElement BuildHierarchy()
    {
        var panel = new VisualElement();
        panel.style.width = 230;
        panel.style.flexShrink = 0;
        panel.style.backgroundColor = new UIColor(34, 36, 39);
        panel.Add(new UiLabel("层级"));
        _hierarchyElements.Clear();
        var nextId = 1;
        var rootItem = BuildHierarchyItem(_documentRoot, ref nextId);
        var tree = new UiTreeView { items = [rootItem] };
        tree.style.flexGrow = 1;
        tree.selectionChanged += item =>
        {
            if (item is not null && _hierarchyElements.TryGetValue(item.Id, out var selected))
            {
                _selected = selected;
                RebuildView();
            }
        };
        tree.contextMenuRequested += (item, menu) =>
        {
            if (!_hierarchyElements.TryGetValue(item.Id, out var target)) return;
            menu.AddAction("添加/容器", () => AddElement(nameof(VisualElement), target));
            menu.AddAction("添加/文本", () => AddElement(nameof(UiLabel), target));
            menu.AddAction("添加/按钮", () => AddElement(nameof(UiButton), target));
            menu.AddAction("添加/输入框", () => AddElement(nameof(TextField), target));
            menu.AddSeparator();
            menu.AddAction("删除", () => DeleteElement(target), !ReferenceEquals(target, _documentRoot));
        };
        panel.Add(tree);
        return panel;
    }

    private TreeViewItem BuildHierarchyItem(VisualElement element, ref int nextId)
    {
        var id = nextId++;
        _hierarchyElements[id] = element;
        var title = string.IsNullOrWhiteSpace(element.name) ? element.GetType().Name : element.name;
        var children = new List<TreeViewItem>();
        foreach (var child in element.Children) children.Add(BuildHierarchyItem(child, ref nextId));
        return new TreeViewItem(id, title, element, children);
    }

    private VisualElement BuildPreview()
    {
        var column = new VisualElement();
        column.style.flexGrow = 1;
        column.style.backgroundColor = new UIColor(25, 27, 30);
        column.Add(new UiLabel("画布  1280 x 720"));
        _preview = new VisualElement { name = "PreviewSurface" };
        _preview.style.flexGrow = 1;
        _preview.style.SetMargin(8);
        _preview.style.SetPadding(12);
        _preview.style.backgroundColor = new UIColor(18, 19, 21);
        column.Add(_preview);
        RefreshPreview();
        return column;
    }

    private VisualElement BuildInspector()
    {
        var inspector = new ScrollView();
        inspector.style.width = 285;
        inspector.style.flexShrink = 0;
        inspector.style.SetPadding(6);
        inspector.style.backgroundColor = new UIColor(34, 36, 39);
        inspector.Add(new UiLabel("样式"));
        inspector.Add(new TextField("类型") { value = _selected.GetType().Name, isReadOnly = true });

        var name = new TextField("名称") { value = _selected.name };
        name.valueChanged += value => { _selected.name = value; MarkDirtyAndPreview(); };
        inspector.Add(name);
        if (_selected is TextElement textElement)
        {
            var text = new TextField("文本") { value = textElement.text };
            text.valueChanged += value => { textElement.text = value; MarkDirtyAndPreview(); };
            inspector.Add(text);
        }
        else if (_selected is TextField textField)
        {
            var value = new TextField("默认值") { value = textField.value };
            value.valueChanged += changed => { textField.value = changed; MarkDirtyAndPreview(); };
            inspector.Add(value);
        }

        var direction = new DropdownField("排列", Enum.GetNames<FlexDirection>())
        {
            value = _selected.style.flexDirection.ToString()
        };
        direction.valueChanged += value =>
        {
            if (Enum.TryParse<FlexDirection>(value, out var parsed)) _selected.style.flexDirection = parsed;
            MarkDirtyAndPreview();
        };
        inspector.Add(direction);
        inspector.Add(FloatStyleField("宽度", _selected.style.width,
            value => _selected.style.width = Math.Max(0, value)));
        inspector.Add(FloatStyleField("高度", _selected.style.height,
            value => _selected.style.height = Math.Max(0, value)));
        inspector.Add(FloatStyleField("Flex Grow", _selected.style.flexGrow,
            value => _selected.style.flexGrow = Math.Max(0, value)));
        inspector.Add(FloatStyleField("字体大小", _selected.style.fontSize,
            value => _selected.style.fontSize = Math.Max(0, value)));
        inspector.Add(FloatStyleField("内边距", _selected.style.paddingLeft,
            value => _selected.style.SetPadding(Math.Max(0, value))));
        inspector.Add(FloatStyleField("外边距", _selected.style.marginLeft,
            value => _selected.style.SetMargin(Math.Max(0, value))));

        var background = new TextField("背景色") { value = FormatColor(_selected.style.backgroundColor) };
        background.valueChanged += value =>
        {
            _selected.style.backgroundColor = ParseColor(value);
            MarkDirtyAndPreview();
        };
        inspector.Add(background);
        var foreground = new TextField("文字色") { value = FormatColor(_selected.style.color) };
        foreground.valueChanged += value =>
        {
            _selected.style.color = ParseColor(value);
            MarkDirtyAndPreview();
        };
        inspector.Add(foreground);

        var classes = new TextField("样式类") { value = string.Join(' ', _selected.classList) };
        classes.valueChanged += value =>
        {
            foreach (var className in _selected.classList.ToArray()) _selected.RemoveFromClassList(className);
            foreach (var className in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _selected.AddToClassList(className);
            MarkDirtyAndPreview();
        };
        inspector.Add(classes);
        return inspector;
    }

    private FloatField FloatStyleField(string label, float current, Action<float> setter)
    {
        var field = new FloatField(label) { value = current };
        field.valueChanged += value =>
        {
            setter(value);
            MarkDirtyAndPreview();
        };
        return field;
    }

    private void AddElement(string typeName, VisualElement? requestedParent = null)
    {
        var element = typeName switch
        {
            nameof(UiLabel) => new UiLabel("文本") { name = "Label" },
            nameof(UiButton) => new UiButton(text: "按钮") { name = "Button" },
            nameof(TextField) => new TextField { name = "TextField" },
            _ => new VisualElement { name = "VisualElement" }
        };
        if (element is VisualElement container && element.GetType() == typeof(VisualElement))
        {
            container.style.flexGrow = 1;
            container.style.SetPadding(4);
        }
        (requestedParent ?? _selected).Add(element);
        _selected = element;
        _dirty = true;
        RebuildView();
    }

    private void DeleteSelected() => DeleteElement(_selected);

    private void DeleteElement(VisualElement element)
    {
        if (ReferenceEquals(element, _documentRoot) || element.parent is null) return;
        var parent = element.parent;
        element.RemoveFromHierarchy();
        _selected = parent;
        _dirty = true;
        RebuildView();
    }

    private void NewDocument()
    {
        _documentRoot = CreateDefaultDocument();
        _selected = _documentRoot;
        _assetPath = string.Empty;
        _dirty = true;
        RebuildView();
    }

    private void LoadDocument(string path)
    {
        try
        {
            var fullPath = ResolveAssetPath(path);
            _documentRoot = VisualTreeAsset.Load(fullPath).Instantiate();
            _selected = _documentRoot;
            _assetPath = ToProjectPath(fullPath);
            _dirty = false;
            RebuildView();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Debug.LogError($"UI Builder 无法打开 '{path}': {exception.Message}");
        }
    }

    private void SaveDocument()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_assetPath))
                _assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/UI/New UI.ui.yaml");
            var fullPath = ResolveAssetPath(_assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            VisualTreeAsset.Create(_documentRoot).Save(fullPath);
            AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh();
            _dirty = false;
            UpdateStatus();
            Debug.Log($"UI Builder 已保存: {_assetPath}");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Debug.LogError($"UI Builder 保存失败: {exception.Message}");
        }
    }

    private void MarkDirtyAndPreview()
    {
        _dirty = true;
        RefreshPreview();
        UpdateStatus();
    }

    private void RefreshPreview()
    {
        if (_preview is null) return;
        _preview.Clear();
        var clone = VisualTreeAsset.Create(_documentRoot).Instantiate();
        clone.style.flexGrow = 1;
        _preview.Add(clone);
    }

    private void UpdateStatus()
    {
        if (_status is not null) _status.text = BuildStatusText();
    }

    private string BuildStatusText() =>
        $"{(_dirty ? "未保存" : "已保存")}  |  {(string.IsNullOrWhiteSpace(_assetPath) ? "新建文档" : _assetPath)}";

    private static string[] FindUiAssets() => AssetDatabase.GetAllAssetPaths()
        .Where(path => path.EndsWith(".ui.yaml", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string ResolveAssetPath(string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        var projectRoot = Directory.GetParent(Path.GetFullPath(Application.dataPath))?.FullName ??
                          Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ToProjectPath(string fullPath)
    {
        var projectRoot = Directory.GetParent(Path.GetFullPath(Application.dataPath))?.FullName ??
                          Directory.GetCurrentDirectory();
        return Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
    }

    private static VisualElement CreateDefaultDocument()
    {
        var root = new VisualElement { name = "Root" };
        root.style.flexGrow = 1;
        root.style.SetPadding(32);
        root.style.backgroundColor = new UIColor(25, 28, 32);
        var title = new UiLabel("BEngine UI") { name = "Title" };
        title.style.height = 36;
        title.style.fontSize = 20;
        root.Add(title);
        var button = new UiButton(text: "开始") { name = "StartButton" };
        button.style.width = 220;
        button.style.height = 48;
        button.style.backgroundColor = new UIColor(42, 150, 128);
        root.Add(button);
        return root;
    }

    private static string FormatColor(UIColor? color) => color is { } value
        ? $"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}"
        : string.Empty;

    private static UIColor? ParseColor(string value)
    {
        var text = value.Trim().TrimStart('#');
        if (text.Length is not (6 or 8) || !uint.TryParse(text, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var packed)) return null;
        if (text.Length == 6) packed = packed << 8 | 0xFF;
        return new UIColor((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }
}
