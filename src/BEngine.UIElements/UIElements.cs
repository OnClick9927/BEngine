using System.Collections;

namespace BEngine.UIElements;

public enum FlexDirection
{
    Column,
    Row
}

public enum DisplayStyle
{
    Flex,
    None
}

public enum Align
{
    Auto,
    FlexStart,
    Center,
    FlexEnd,
    Stretch
}

public enum Justify
{
    FlexStart,
    Center,
    FlexEnd,
    SpaceBetween
}

public enum Overflow
{
    Visible,
    Hidden,
    Scroll
}

public readonly record struct UIColor(byte R, byte G, byte B, byte A = 255)
{
    public static UIColor FromRgb(byte red, byte green, byte blue) => new(red, green, blue);
    public static UIColor FromArgb(byte alpha, byte red, byte green, byte blue) => new(red, green, blue, alpha);
    public static UIColor Clear => new(0, 0, 0, 0);
}

public sealed class Style
{
    private readonly Action _changed;
    private FlexDirection _flexDirection = FlexDirection.Column;
    private DisplayStyle _display = DisplayStyle.Flex;
    private Align _alignItems = Align.Stretch;
    private Justify _justifyContent;
    private Overflow _overflow = Overflow.Visible;
    private float _width;
    private float _height;
    private float _minWidth;
    private float _minHeight;
    private float _maxWidth = float.PositiveInfinity;
    private float _maxHeight = float.PositiveInfinity;
    private float _flexGrow;
    private float _flexShrink = 1;
    private float _marginLeft;
    private float _marginTop;
    private float _marginRight;
    private float _marginBottom;
    private float _paddingLeft;
    private float _paddingTop;
    private float _paddingRight;
    private float _paddingBottom;
    private float _fontSize;
    private UIColor? _color;
    private UIColor? _backgroundColor;

    internal Style(Action changed) => _changed = changed;

    public FlexDirection flexDirection { get => _flexDirection; set => Set(ref _flexDirection, value); }
    public DisplayStyle display { get => _display; set => Set(ref _display, value); }
    public Align alignItems { get => _alignItems; set => Set(ref _alignItems, value); }
    public Justify justifyContent { get => _justifyContent; set => Set(ref _justifyContent, value); }
    public Overflow overflow { get => _overflow; set => Set(ref _overflow, value); }
    public float width { get => _width; set => Set(ref _width, value); }
    public float height { get => _height; set => Set(ref _height, value); }
    public float minWidth { get => _minWidth; set => Set(ref _minWidth, value); }
    public float minHeight { get => _minHeight; set => Set(ref _minHeight, value); }
    public float maxWidth { get => _maxWidth; set => Set(ref _maxWidth, value); }
    public float maxHeight { get => _maxHeight; set => Set(ref _maxHeight, value); }
    public float flexGrow { get => _flexGrow; set => Set(ref _flexGrow, value); }
    public float flexShrink { get => _flexShrink; set => Set(ref _flexShrink, value); }
    public float marginLeft { get => _marginLeft; set => Set(ref _marginLeft, value); }
    public float marginTop { get => _marginTop; set => Set(ref _marginTop, value); }
    public float marginRight { get => _marginRight; set => Set(ref _marginRight, value); }
    public float marginBottom { get => _marginBottom; set => Set(ref _marginBottom, value); }
    public float paddingLeft { get => _paddingLeft; set => Set(ref _paddingLeft, value); }
    public float paddingTop { get => _paddingTop; set => Set(ref _paddingTop, value); }
    public float paddingRight { get => _paddingRight; set => Set(ref _paddingRight, value); }
    public float paddingBottom { get => _paddingBottom; set => Set(ref _paddingBottom, value); }
    public float fontSize { get => _fontSize; set => Set(ref _fontSize, value); }
    public UIColor? color { get => _color; set => Set(ref _color, value); }
    public UIColor? backgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }

    public void SetMargin(float all) => SetMargin(all, all, all, all);

    public void SetMargin(float left, float top, float right, float bottom)
    {
        _marginLeft = left;
        _marginTop = top;
        _marginRight = right;
        _marginBottom = bottom;
        _changed();
    }

    public void SetPadding(float all) => SetPadding(all, all, all, all);

    public void SetPadding(float left, float top, float right, float bottom)
    {
        _paddingLeft = left;
        _paddingTop = top;
        _paddingRight = right;
        _paddingBottom = bottom;
        _changed();
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        _changed();
    }
}

public class VisualElement
{
    private readonly List<VisualElement> _children = [];
    private readonly HashSet<string> _classList = new(StringComparer.Ordinal);
    private string _name = string.Empty;
    private string _tooltip = string.Empty;
    private bool _visible = true;
    private bool _enabledSelf = true;

    public VisualElement() => style = new Style(MarkDirty);

    public string name { get => _name; set => Set(ref _name, value ?? string.Empty); }
    public string tooltip { get => _tooltip; set => Set(ref _tooltip, value ?? string.Empty); }
    public bool visible { get => _visible; set => Set(ref _visible, value); }
    public bool enabledSelf => _enabledSelf;
    public bool enabledInHierarchy => _enabledSelf && (parent?.enabledInHierarchy ?? true);
    public Style style { get; }
    public object? userData { get; set; }
    public VisualElement? parent { get; private set; }
    public IReadOnlyList<VisualElement> Children => _children;
    public IReadOnlyCollection<string> classList => _classList;

    internal event Action<VisualElement>? changed;
    internal event Action<VisualElement>? hierarchyChanged;

    public void Add(VisualElement child) => Insert(_children.Count, child);

    public void Insert(int index, VisualElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (ReferenceEquals(child, this) || IsDescendantOf(child))
            throw new InvalidOperationException("A VisualElement cannot contain itself or one of its ancestors.");
        child.RemoveFromHierarchy();
        child.parent = this;
        _children.Insert(Math.Clamp(index, 0, _children.Count), child);
        child.changed += OnDescendantChanged;
        child.hierarchyChanged += OnDescendantHierarchyChanged;
        hierarchyChanged?.Invoke(this);
    }

    public bool Remove(VisualElement child)
    {
        if (!_children.Remove(child)) return false;
        child.changed -= OnDescendantChanged;
        child.hierarchyChanged -= OnDescendantHierarchyChanged;
        child.parent = null;
        hierarchyChanged?.Invoke(this);
        return true;
    }

    public void Clear()
    {
        foreach (var child in _children.ToArray()) Remove(child);
    }

    public void RemoveFromHierarchy() => parent?.Remove(this);

    public void SetEnabled(bool enabled) => Set(ref _enabledSelf, enabled);

    public void AddToClassList(string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        if (_classList.Add(className)) MarkDirty();
    }

    public void RemoveFromClassList(string className)
    {
        if (_classList.Remove(className)) MarkDirty();
    }

    public bool ClassListContains(string className) => _classList.Contains(className);

    public T? Q<T>(string? elementName = null) where T : VisualElement
    {
        foreach (var element in DescendantsAndSelf())
        {
            if (element is T typed && (elementName is null || element.name == elementName)) return typed;
        }
        return null;
    }

    public IEnumerable<VisualElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        foreach (var descendant in child.DescendantsAndSelf())
            yield return descendant;
    }

    internal void MarkDirty() => changed?.Invoke(this);

    protected void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        MarkDirty();
    }

    private bool IsDescendantOf(VisualElement candidate)
    {
        for (var current = this; current is not null; current = current.parent)
        {
            if (ReferenceEquals(current, candidate)) return true;
        }
        return false;
    }

    private void OnDescendantChanged(VisualElement element) => changed?.Invoke(element);
    private void OnDescendantHierarchyChanged(VisualElement element) => hierarchyChanged?.Invoke(element);
}

public class TextElement : VisualElement
{
    private string _text = string.Empty;
    public string text { get => _text; set => Set(ref _text, value ?? string.Empty); }
    protected TextElement(string text = "") => _text = text;
}

public class Label : TextElement
{
    public event Action? doubleClicked;
    public Label(string text = "") : base(text) { }
    internal void RaiseDoubleClicked() => doubleClicked?.Invoke();
}

public class Button : TextElement
{
    public event Action? clicked;
    public Button(Action? clickEvent = null, string text = "") : base(text)
    {
        if (clickEvent is not null) clicked += clickEvent;
    }
    internal void Click() => clicked?.Invoke();
}

public class ToolbarButton : Button
{
    public ToolbarButton(Action? clickEvent = null, string text = "") : base(clickEvent, text) { }
}

public abstract class BaseField<T> : VisualElement
{
    private string _label;
    private T _value = default!;
    private bool _isReadOnly;

    protected BaseField(string label = "") => _label = label;

    public string label { get => _label; set => Set(ref _label, value ?? string.Empty); }
    public T value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value)) return;
            _value = value;
            MarkDirty();
            valueChanged?.Invoke(value);
        }
    }
    public bool isReadOnly { get => _isReadOnly; set => Set(ref _isReadOnly, value); }
    public event Action<T>? valueChanged;

    public void SetValueWithoutNotify(T value)
    {
        if (EqualityComparer<T>.Default.Equals(_value, value)) return;
        _value = value;
        MarkDirty();
    }

    internal void ChangeValueFromView(T value)
    {
        if (EqualityComparer<T>.Default.Equals(_value, value)) return;
        _value = value;
        valueChanged?.Invoke(value);
    }
}

public class TextField : BaseField<string>
{
    private bool _multiline;
    private bool _scrollToEnd;
    public bool multiline { get => _multiline; set => Set(ref _multiline, value); }
    public bool scrollToEnd { get => _scrollToEnd; set => Set(ref _scrollToEnd, value); }
    public event Action? doubleClicked;
    public TextField(string label = "") : base(label) => SetValueWithoutNotify(string.Empty);
    internal void RaiseDoubleClicked() => doubleClicked?.Invoke();
}

public sealed class SearchField : TextField
{
    public SearchField(string label = "") : base(label) { }
}

public class FloatField : BaseField<float>
{
    public FloatField(string label = "") : base(label) { }
}

public class IntegerField : BaseField<int>
{
    public IntegerField(string label = "") : base(label) { }
}

public class Toggle : BaseField<bool>
{
    public Toggle(string label = "") : base(label) { }
}

public class Slider : BaseField<float>
{
    public float lowValue { get; set; }
    public float highValue { get; set; } = 1;
    public Slider(string label = "", float start = 0, float end = 1) : base(label)
    {
        lowValue = start;
        highValue = end;
    }
}

public class DropdownField : BaseField<string>
{
    private IReadOnlyList<string> _choices = [];
    public IReadOnlyList<string> choices
    {
        get => _choices;
        set
        {
            _choices = value ?? [];
            MarkDirty();
        }
    }
    public DropdownField(string label = "", IEnumerable<string>? choices = null) : base(label)
    {
        _choices = choices?.ToArray() ?? [];
        SetValueWithoutNotify(_choices.FirstOrDefault() ?? string.Empty);
    }
}

public class Foldout : Toggle
{
    public Foldout(string text = "") : base(text) { }
}

public class ScrollView : VisualElement
{
    public ScrollView() => style.overflow = Overflow.Scroll;
}

public class Toolbar : VisualElement
{
    public Toolbar() => style.flexDirection = FlexDirection.Row;
}

public class Image : VisualElement
{
    private string _sourcePath = string.Empty;
    public string sourcePath { get => _sourcePath; set => Set(ref _sourcePath, value ?? string.Empty); }
    public string scaleMode { get; set; } = "ScaleToFit";
}

public sealed record TreeViewItem(int Id, string Text, object? Data = null, IReadOnlyList<TreeViewItem>? Children = null);

public class TreeView : VisualElement
{
    private IReadOnlyList<TreeViewItem> _items = [];
    private int? _selectedId;
    public IReadOnlyList<TreeViewItem> items
    {
        get => _items;
        set
        {
            _items = value ?? [];
            MarkDirty();
        }
    }
    public int? selectedId { get => _selectedId; set => Set(ref _selectedId, value); }
    public event Action<TreeViewItem?>? selectionChanged;
    public event Action<TreeViewItem>? itemChosen;
    public event Action<TreeViewItem, ContextMenuBuilder>? contextMenuRequested;

    internal void SelectFromView(TreeViewItem? item)
    {
        _selectedId = item?.Id;
        selectionChanged?.Invoke(item);
    }
    internal void ChooseFromView(TreeViewItem item) => itemChosen?.Invoke(item);
    internal void BuildContextMenu(TreeViewItem item, ContextMenuBuilder menu) =>
        contextMenuRequested?.Invoke(item, menu);
}

public class ListView : VisualElement
{
    private IList _itemsSource = Array.Empty<object>();
    private int _selectedIndex = -1;
    public IList itemsSource { get => _itemsSource; set { _itemsSource = value ?? Array.Empty<object>(); MarkDirty(); } }
    public Func<object?, string> makeItemText { get; set; } = item => item?.ToString() ?? string.Empty;
    public int selectedIndex { get => _selectedIndex; set => Set(ref _selectedIndex, value); }
    public bool showAlternatingRowBackgrounds { get; set; } = true;
    public event Action<object?>? selectionChanged;
    public event Action<object?>? itemChosen;
    public event Action<object?, ContextMenuBuilder>? contextMenuRequested;

    internal void SelectFromView(int index)
    {
        _selectedIndex = index;
        selectionChanged?.Invoke(index >= 0 && index < _itemsSource.Count ? _itemsSource[index] : null);
    }
    internal void ChooseFromView(int index) =>
        itemChosen?.Invoke(index >= 0 && index < _itemsSource.Count ? _itemsSource[index] : null);
    internal void BuildContextMenu(object? item, ContextMenuBuilder menu) => contextMenuRequested?.Invoke(item, menu);
}

public sealed class ContextMenuBuilder
{
    private readonly List<ContextMenuItem> _items = [];
    public IReadOnlyList<ContextMenuItem> Items => _items;
    public void AddAction(string name, Action action, bool enabled = true, bool isChecked = false) =>
        _items.Add(new ContextMenuItem(name, action, enabled, isChecked, false));
    public void AddSeparator() => _items.Add(new ContextMenuItem(string.Empty, null, false, false, true));
}

public sealed record ContextMenuItem(string Name, Action? Action, bool Enabled, bool IsChecked, bool Separator);
