using System.Collections;
using System.Buffers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class VisualElement : IBindable
{
    private static int _nextAccessibilityId;
    private readonly List<VisualElement> _children = [];
    private Action<VisualElement>? _descendantChanged, _descendantHierarchyChanged;
    private readonly HashSet<string> _classList = new(StringComparer.Ordinal);
    private string _name = string.Empty;
    private string _tooltip = string.Empty;
    private bool _visible = true;
    private bool _enabledSelf = true;
    private bool _hovered;
    private bool _pressed;
    private bool _focused;
    private bool _styleSheetsDirty = true;
    private bool _applyingStyleSheets;
    private bool _applyingRepaintStyles;
    private bool _supportsLocalInteractionRepaint;
    private long _layoutVersion;
    // Applying the cascade may change inherited or selector-dependent geometry
    // anywhere in the tree without an inline setter on the affected subtree.
    internal long styleApplicationVersion { get; private set; }
    private long _renderVersion;
    internal long renderVersion => _renderVersion;
    private bool _changeAffectsLayout = true;
    private bool _changeAffectsStyleSheets = true;
    internal long layoutVersion => _layoutVersion;
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
    private readonly Dictionary<Type, List<EventCallbackRegistration>> _callbacks = [];
    private VisualElementScheduler? _scheduler;
    private string _bindingPath = string.Empty;
    private AccessibilityRole _accessibilityRole;
    private string _accessibilityName = string.Empty;
    private string _accessibilityDescription = string.Empty;
    private string _accessibilityValue = string.Empty;
    private bool _accessibilityHidden;
    private AccessibilityLiveRegion _accessibilityLiveRegion;
    private bool _focusable;
    private int _tabIndex = -1;

    public VisualElement()
    {
        style = new Style(OnStyleChanged, OnStylePaintChanged, OnTextMetricsChanged);
        var sheets = new ObservableCollection<StyleSheet>();
        // A method-group subscription is cached by the element and avoids a
        // closure allocation for every node mounted in a dynamic UI.
        sheets.CollectionChanged += OnStyleSheetsChanged;
        styleSheets = sheets;
    }

    private void OnStyleSheetsChanged(object? sender, NotifyCollectionChangedEventArgs args) => MarkDirty();

    public string name { get => _name; set => Set(ref _name, value ?? string.Empty); }
    public string tooltip { get => _tooltip; set => SetRepaint(ref _tooltip, value ?? string.Empty); }
    public bool visible
    {
        get => _visible;
        set { if (_visible == value) return; _visible = value; MarkLayoutDirty(); }
    }
    public bool enabledSelf => _enabledSelf;
    public bool enabledInHierarchy => _enabledSelf && (parent?.enabledInHierarchy ?? true);
    public bool hovered => _hovered;
    public bool pressed => _pressed;
    public bool focused => _focused;
    public int accessibilityId { get; } = Interlocked.Increment(ref _nextAccessibilityId);
    public AccessibilityRole accessibilityRole { get => _accessibilityRole; set => Set(ref _accessibilityRole, value); }
    public string accessibilityName { get => _accessibilityName; set => Set(ref _accessibilityName, value ?? string.Empty); }
    public string accessibilityDescription { get => _accessibilityDescription; set => Set(ref _accessibilityDescription, value ?? string.Empty); }
    public string accessibilityValue { get => _accessibilityValue; set => Set(ref _accessibilityValue, value ?? string.Empty); }
    public bool accessibilityHidden { get => _accessibilityHidden; set => Set(ref _accessibilityHidden, value); }
    public AccessibilityLiveRegion accessibilityLiveRegion { get => _accessibilityLiveRegion; set => Set(ref _accessibilityLiveRegion, value); }
    public bool focusable { get => _focusable; set => Set(ref _focusable, value); }
    public int tabIndex { get => _tabIndex; set => Set(ref _tabIndex, value); }
    internal UIDocument? runtimeDocument { get; set; }
    public void CapturePointer(int pointerId) => (FindRuntimeDocument() ??
        throw new InvalidOperationException("Pointer capture requires a runtime UI document.")).CapturePointer(pointerId, this);
    public void ReleasePointer(int pointerId) => FindRuntimeDocument()?.ReleasePointer(pointerId, this);
    public bool HasPointerCapture(int pointerId) => FindRuntimeDocument()?.HasPointerCapture(pointerId, this) == true;
    private UIDocument? FindRuntimeDocument()
    {
        for (var element = this; element is not null; element = element.parent)
            if (element.runtimeDocument is { } document) return document;
        return null;
    }
    internal string? textEditingValue => _textEditingValue;
    internal int textEditingCaretIndex => _textEditingCaretIndex;
    internal bool textEditingCaretVisible => _textEditingCaretVisible;
    internal int textEditingSelectionStart => _textEditingSelectionStart;
    internal int textEditingSelectionEnd => _textEditingSelectionEnd;
    internal bool isTextEditing => _textEditingValue is not null;
    public Style style { get; }
    public IList<StyleSheet> styleSheets { get; }
    public string sourceTag { get => _sourceTag; set => Set(ref _sourceTag, value ?? string.Empty); }
    public string sourceText { get => _sourceText; set => Set(ref _sourceText, value ?? string.Empty); }
    public IReadOnlyDictionary<string, string> sourceAttributes => _sourceAttributes;
    public object? userData { get; set; }
    public string bindingPath { get => _bindingPath; set => Set(ref _bindingPath, value ?? string.Empty); }
    public VisualElementScheduler schedule => _scheduler ??= new VisualElementScheduler(this);
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
    internal void CopyClassesTo(string[] destination) => _classList.CopyTo(destination);

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
        child.SetStyleViewport(styleViewportWidth, styleViewportHeight);
        _children.Insert(Math.Clamp(index, 0, _children.Count), child);
        child.changed += _descendantChanged ??= OnDescendantChanged;
        child.hierarchyChanged += _descendantHierarchyChanged ??= OnDescendantHierarchyChanged;
        MarkDirty();
        hierarchyChanged?.Invoke(this);
    }

    public bool Remove(VisualElement child)
    {
        if (!_children.Remove(child)) return false;
        CompleteRemoval(child);
        return true;
    }

    private void CompleteRemoval(VisualElement child)
    {
        child.changed -= _descendantChanged;
        child.hierarchyChanged -= _descendantHierarchyChanged;
        child.parent = null;
        MarkDirty();
        hierarchyChanged?.Invoke(this);
    }

    public void Clear()
    {
        while (_children.Count > 0)
        {
            var index = _children.Count - 1; var child = _children[index];
            _children.RemoveAt(index); CompleteRemoval(child);
        }
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

    public T? Q<T>(string? elementName = null, string? className = null) where T : VisualElement
    {
        if (this is T typed &&
            (elementName is null || name == elementName) &&
            (className is null || ClassListContains(className))) return typed;
        foreach (var child in _children)
        {
            var match = child.Q<T>(elementName, className);
            if (match is not null) return match;
        }
        return null;
    }

    public VisualElement? Q(string? elementName = null, string? className = null) =>
        Q<VisualElement>(elementName, className);

    public UQueryBuilder<T> Query<T>(string? elementName = null, string? className = null)
        where T : VisualElement => new(this, elementName, className);

    public bool Contains(VisualElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        for (var current = element; current is not null; current = current.parent)
            if (ReferenceEquals(current, this)) return true;
        return false;
    }

    public int IndexOf(VisualElement element) => _children.IndexOf(element);

    public void BringToFront()
    {
        if (parent is not { } owner || ReferenceEquals(owner._children[^1], this)) return;
        owner._children.Remove(this);
        owner._children.Add(this);
        owner.hierarchyChanged?.Invoke(owner);
    }

    public void SendToBack()
    {
        if (parent is not { } owner || ReferenceEquals(owner._children[0], this)) return;
        owner._children.Remove(this);
        owner._children.Insert(0, this);
        owner.hierarchyChanged?.Invoke(owner);
    }

    public void RegisterCallback<TEvent>(Action<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown)
        where TEvent : UIEventBase
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!_callbacks.TryGetValue(typeof(TEvent), out var callbacks))
            _callbacks[typeof(TEvent)] = callbacks = [];
        if (callbacks.Any(item => item.Callback.Equals(callback) && item.TrickleDown == trickleDown)) return;
        callbacks.Add(new EventCallbackRegistration(callback, evt => callback((TEvent)evt), trickleDown));
    }

    public void UnregisterCallback<TEvent>(Action<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown)
        where TEvent : UIEventBase
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!_callbacks.TryGetValue(typeof(TEvent), out var callbacks)) return;
        callbacks.RemoveAll(item => item.Callback.Equals(callback) && item.TrickleDown == trickleDown);
        if (callbacks.Count == 0) _callbacks.Remove(typeof(TEvent));
    }

    public void SendEvent(UIEventBase evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        evt.target ??= this;
        var depth = 0;
        for (var current = this; current is not null; current = current.parent) depth++;
        var route = ArrayPool<VisualElement>.Shared.Rent(depth);
        try
        {
            var count = 0;
            for (var current = this; current is not null; current = current.parent)
                route[count++] = current;
            var dispatchId = UIEventDiagnostics.BeginDispatch(evt, route.AsSpan(0, count));
            try
            {
            for (var index = count - 1; index >= 0 && !evt.propagationStopped; index--)
                route[index].InvokeCallbacks(evt, TrickleDown.TrickleDown, dispatchId);
            for (var index = 0; index < count && !evt.propagationStopped; index++)
                route[index].InvokeCallbacks(evt, TrickleDown.NoTrickleDown, dispatchId);
            }
            finally
            {
                UIEventDiagnostics.EndDispatch(dispatchId, evt);
            }
        }
        finally
        {
            Array.Clear(route, 0, depth);
            ArrayPool<VisualElement>.Shared.Return(route);
            evt.currentTarget = null;
        }
    }

    public IEnumerable<VisualElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        foreach (var descendant in child.DescendantsAndSelf())
            yield return descendant;
    }

    // Internal recursive walkers keep the concrete List enumerator. Besides
    // avoiding one iterator object per descendant, this retains its fail-fast
    // behavior when a style callback changes an ancestor's children.
    internal List<VisualElement>.Enumerator GetChildEnumerator() => _children.GetEnumerator();

    internal bool HasCallbacksOnPath<TEvent>() where TEvent : UIEventBase
    {
        for (var current = this; current is not null; current = current.parent)
            if (current._callbacks.ContainsKey(typeof(TEvent))) return true;
        return false;
    }

    public void ApplyStyleSheets()
    {
        styleApplicationVersion++;
        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.parent)
            ancestor.styleApplicationVersion++;
        _layoutVersion++; _renderVersion++;
        _applyingStyleSheets = true;
        try
        {
            ResetDescendantStyleSheetValues();
            StyleSheet.defaultStyleSheet.Apply(this);
            _supportsLocalInteractionRepaint = StyleSheet.defaultStyleSheet.SupportsLocalInteractionRepaint;
            ApplyDescendantStyleSheets(this);
        }
        finally { _applyingStyleSheets = false; }
        _styleSheetsDirty = false;
    }

    private void ResetDescendantStyleSheetValues()
    {
        style.ResetStyleSheetValues();
        foreach (var child in _children) child.ResetDescendantStyleSheetValues();
    }

    private void ApplyDescendantStyleSheets(VisualElement applyingRoot)
    {
        if (styleSheets.Count > 0)
            foreach (var sheet in styleSheets)
            {
                applyingRoot._supportsLocalInteractionRepaint &= sheet.SupportsLocalInteractionRepaint;
                sheet.Apply(this);
            }
        foreach (var child in _children) child.ApplyDescendantStyleSheets(applyingRoot);
    }

    internal void EnsureStyleSheets()
    {
        var root = this;
        while (root.parent is not null) root = root.parent;
        if (root._styleSheetsDirty) root.ApplyStyleSheets();
    }

    internal Fix64 styleViewportWidth { get; private set; }
    internal Fix64 styleViewportHeight { get; private set; }
    private bool _styleViewportDirty = true;
    internal void SetStyleViewport(Fix64 width, Fix64 height)
    {
        if (!_styleViewportDirty && styleViewportWidth == width && styleViewportHeight == height) return;
        var changed = SetDescendantStyleViewport(width, height);
        if (changed)
        {
            _styleSheetsDirty = true;
            // A custom control can lay out a subtree separately. Its next parent
            // layout must restore the shared viewport used by media queries.
            for (var ancestor = parent; ancestor is not null; ancestor = ancestor.parent)
                ancestor._styleViewportDirty = true;
        }
    }

    private bool SetDescendantStyleViewport(Fix64 width, Fix64 height)
    {
        var changed = styleViewportWidth != width || styleViewportHeight != height;
        styleViewportWidth = width; styleViewportHeight = height; _styleViewportDirty = false;
        foreach (var child in _children) changed |= child.SetDescendantStyleViewport(width, height);
        return changed;
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

    internal void MarkDirty()
    {
        var affectsStyleSheets = _changeAffectsStyleSheets;
        _changeAffectsStyleSheets = true;
        _changeAffectsLayout = true; _layoutVersion++; _renderVersion++;
        if (!_applyingStyleSheets) _styleSheetsDirty = true;
        try { changed?.Invoke(this); }
        finally { _changeAffectsStyleSheets = affectsStyleSheets; }
    }

    private void OnStyleChanged()
    {
        if (_applyingRepaintStyles) return;
        // Inline geometry/paint changes do not change selector matches. Reapplying
        // the cascade here would reset every styled element for each animation.
        MarkLayoutDirty();
    }

    private void OnStylePaintChanged()
    {
        if (!_applyingRepaintStyles) MarkDirtyRepaint();
    }

    private void OnTextMetricsChanged()
    {
        if (_applyingRepaintStyles) return;
        // A pulse inside a fixed leaf changes glyphs and alignment, but cannot
        // resize siblings. Containers still reflow inherited font metrics.
        if (this is TextElement && Children.Count == 0 && style.width > 0 && style.height > 0) MarkDirtyRepaint();
        else MarkLayoutDirty();
    }

    private void MarkLayoutDirty()
    {
        var affectsStyleSheets = _changeAffectsStyleSheets;
        _changeAffectsStyleSheets = false;
        _changeAffectsLayout = true; _layoutVersion++; _renderVersion++;
        try { changed?.Invoke(this); }
        finally { _changeAffectsStyleSheets = affectsStyleSheets; }
    }

    private void MarkInteractionDirty()
    {
        var root = this;
        while (root.parent is not null) root = root.parent;
        if (root._styleSheetsDirty || !root._supportsLocalInteractionRepaint)
        { MarkDirty(); return; }
        _applyingRepaintStyles = true;
        try
        {
            style.ResetInteractionPaintValues();
            StyleSheet.defaultStyleSheet.ApplyInteractionPaint(this, root);
            ApplyOwnerStyles(this);
        }
        finally { _applyingRepaintStyles = false; }
        MarkDirtyRepaint();

        void ApplyOwnerStyles(VisualElement owner)
        {
            if (owner.parent is { } parent) ApplyOwnerStyles(parent);
            foreach (var sheet in owner.styleSheets) sheet.ApplyInteractionPaint(this, owner);
        }
    }

    protected void MarkTextDirty()
    {
        // Supported selectors match type/name/classes/interaction, not text content.
        // Text never changes those matches. Fixed text boxes only need repaint;
        // intrinsic sizing still invalidates their ancestors' layout.
        if (this is TextElement && style.width > 0 && style.height > 0) MarkDirtyRepaint();
        else MarkLayoutDirty();
    }

    /// <summary>Invalidates painted content without recalculating styles or layout.</summary>
    public void MarkDirtyRepaint()
    {
        var affectsLayout = _changeAffectsLayout;
        _changeAffectsLayout = false;
        _renderVersion++;
        try { changed?.Invoke(this); }
        finally { _changeAffectsLayout = affectsLayout; }
    }

    protected void SetRepaint<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        MarkDirtyRepaint();
    }

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
        if (changedState) MarkInteractionDirty();
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
        MarkDirtyRepaint();
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

    private void OnDescendantChanged(VisualElement element)
    {
        _renderVersion++;
        if (element._changeAffectsLayout)
        {
            _layoutVersion++;
            if (element._changeAffectsStyleSheets && !_applyingStyleSheets) _styleSheetsDirty = true;
        }
        changed?.Invoke(element);
    }
    private void OnDescendantHierarchyChanged(VisualElement element)
    {
        _layoutVersion++; _renderVersion++;
        _styleSheetsDirty = true;
        hierarchyChanged?.Invoke(element);
    }

    private void InvokeCallbacks(UIEventBase evt, TrickleDown trickleDown, long dispatchId)
    {
        if (!_callbacks.TryGetValue(evt.GetType(), out var callbacks)) return;
        evt.currentTarget = this;
        var phase = ReferenceEquals(this, evt.target)
            ? UIEventPropagationPhase.AtTarget
            : trickleDown == TrickleDown.TrickleDown
                ? UIEventPropagationPhase.TrickleDown
                : UIEventPropagationPhase.BubbleUp;
        for (var index = 0; index < callbacks.Count && !evt.immediatePropagationStopped; index++)
        {
            var registration = callbacks[index];
            if (registration.TrickleDown != trickleDown) continue;
            UIEventDiagnostics.CallbackInvoked(dispatchId, evt, this, phase, registration.Callback);
            registration.Invoke(evt);
        }
    }

    private readonly record struct EventCallbackRegistration(
        Delegate Callback,
        Action<UIEventBase> Invoke,
        TrickleDown TrickleDown);
}
