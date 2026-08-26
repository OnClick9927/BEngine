using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class VisualElement
{
    private readonly List<VisualElement> _children = [];
    private readonly HashSet<string> _classList = new(StringComparer.Ordinal);
    private string _name = string.Empty;
    private string _tooltip = string.Empty;
    private bool _visible = true;
    private bool _enabledSelf = true;
    private bool _hovered;
    private bool _pressed;
    private bool _focused;
    private string? _textEditingValue;
    private int _textEditingCaretIndex;
    private bool _textEditingCaretVisible;
    private int _textEditingSelectionStart;
    private int _textEditingSelectionEnd;
    private string _sourceTag = string.Empty;
    private string _sourceText = string.Empty;
    private ulong _sortingLayer = SortingLayer.Ui;
    private int _orderInLayer;
    private Material? _material;
    private string _atlas = string.Empty;
    private readonly Dictionary<string, string> _sourceAttributes =
        new(StringComparer.OrdinalIgnoreCase);

    public VisualElement() => style = new Style(MarkDirty);

    public string name { get => _name; set => Set(ref _name, value ?? string.Empty); }
    public string tooltip { get => _tooltip; set => Set(ref _tooltip, value ?? string.Empty); }
    public bool visible { get => _visible; set => Set(ref _visible, value); }
    public bool enabledSelf => _enabledSelf;
    public bool enabledInHierarchy => _enabledSelf && (parent?.enabledInHierarchy ?? true);
    internal bool hovered => _hovered;
    internal bool pressed => _pressed;
    internal bool focused => _focused;
    internal string? textEditingValue => _textEditingValue;
    internal int textEditingCaretIndex => _textEditingCaretIndex;
    internal bool textEditingCaretVisible => _textEditingCaretVisible;
    internal int textEditingSelectionStart => _textEditingSelectionStart;
    internal int textEditingSelectionEnd => _textEditingSelectionEnd;
    internal bool isTextEditing => _textEditingValue is not null;
    public Style style { get; }
    public IList<StyleSheet> styleSheets { get; } = [];
    public string sourceTag { get => _sourceTag; set => Set(ref _sourceTag, value ?? string.Empty); }
    public string sourceText { get => _sourceText; set => Set(ref _sourceText, value ?? string.Empty); }
    public IReadOnlyDictionary<string, string> sourceAttributes => _sourceAttributes;
    public object? userData { get; set; }
    public ulong sortingLayer
    {
        get => _sortingLayer;
        set
        {
            SortingLayer.Validate(value);
            if (!SortingLayer.IsUi(value))
                throw new ArgumentOutOfRangeException(nameof(value),
                    "UI elements must use one of the five UI layers.");
            Set(ref _sortingLayer, value);
        }
    }
    public int orderInLayer { get => _orderInLayer; set => Set(ref _orderInLayer, value); }
    public Material? material { get => _material; set => Set(ref _material, value); }
    public string atlas { get => _atlas; set => Set(ref _atlas, value?.Trim() ?? string.Empty); }
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

    public void ApplyStyleSheets()
    {
        foreach (var sheet in styleSheets) sheet.Apply(this);
    }

    public void SetSourceAttribute(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        value ??= string.Empty;
        if (_sourceAttributes.TryGetValue(name, out var current) && current == value) return;
        _sourceAttributes[name] = value;
        MarkDirty();
    }

    public bool RemoveSourceAttribute(string name)
    {
        if (!_sourceAttributes.Remove(name)) return false;
        MarkDirty();
        return true;
    }

    public string? GetSourceAttribute(string name) =>
        _sourceAttributes.TryGetValue(name, out var value) ? value : null;

    internal void MarkDirty() => changed?.Invoke(this);

    internal void SetInteractionState(bool? isHovered = null, bool? isPressed = null, bool? isFocused = null)
    {
        var changedState = false;
        if (isHovered is { } hoveredValue && _hovered != hoveredValue)
        {
            _hovered = hoveredValue;
            changedState = true;
        }
        if (isPressed is { } pressedValue && _pressed != pressedValue)
        {
            _pressed = pressedValue;
            changedState = true;
        }
        if (isFocused is { } focusedValue && _focused != focusedValue)
        {
            _focused = focusedValue;
            changedState = true;
        }
        if (changedState) MarkDirty();
    }

    internal void SetTextEditingState(
        string? text,
        int caretIndex,
        bool caretVisible,
        int selectionStart = 0,
        int selectionEnd = 0)
    {
        var normalizedIndex = text is null ? 0 : Math.Clamp(caretIndex, 0, text.Length);
        var normalizedVisibility = text is not null && caretVisible;
        var normalizedSelectionStart = text is null ? 0 : Math.Clamp(selectionStart, 0, text.Length);
        var normalizedSelectionEnd = text is null ? 0 : Math.Clamp(selectionEnd, 0, text.Length);
        if (_textEditingValue == text &&
            _textEditingCaretIndex == normalizedIndex &&
            _textEditingCaretVisible == normalizedVisibility &&
            _textEditingSelectionStart == normalizedSelectionStart &&
            _textEditingSelectionEnd == normalizedSelectionEnd)
            return;

        _textEditingValue = text;
        _textEditingCaretIndex = normalizedIndex;
        _textEditingCaretVisible = normalizedVisibility;
        _textEditingSelectionStart = normalizedSelectionStart;
        _textEditingSelectionEnd = normalizedSelectionEnd;
        MarkDirty();
    }

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
