using System.Collections;
using System.Collections.ObjectModel;
using BEngine;
using BEngine.Editor;

namespace UnityEditorInternal;

/// <summary>
/// Unity-compatible IMGUI list control backed by either an <see cref="IList"/> or an array
/// <see cref="SerializedProperty"/>.
/// </summary>
public class ReorderableList
{
    public delegate void HeaderCallbackDelegate(Rect rect);
    public delegate void FooterCallbackDelegate(Rect rect);
    public delegate void ElementCallbackDelegate(Rect rect, int index, bool isActive, bool isFocused);
    public delegate float ElementHeightCallbackDelegate(int index);
    public delegate void DrawNoneElementCallback(Rect rect);
    public delegate void ReorderCallbackDelegateWithDetails(ReorderableList list, int oldIndex, int newIndex);
    public delegate void ReorderCallbackDelegate(ReorderableList list);
    public delegate void SelectCallbackDelegate(ReorderableList list);
    public delegate void AddCallbackDelegate(ReorderableList list);
    public delegate void AddDropdownCallbackDelegate(Rect buttonRect, ReorderableList list);
    public delegate void RemoveCallbackDelegate(ReorderableList list);
    public delegate void DeleteArrayElementCallbackDelegate(ReorderableList list, int index);
    public delegate void ChangedCallbackDelegate(ReorderableList list);
    public delegate bool CanRemoveCallbackDelegate(ReorderableList list);
    public delegate bool CanAddCallbackDelegate(ReorderableList list);
    public delegate void DragCallbackDelegate(ReorderableList list);

    public HeaderCallbackDelegate? drawHeaderCallback;
    public FooterCallbackDelegate? drawFooterCallback;
    public ElementCallbackDelegate? drawElementCallback;
    public ElementCallbackDelegate? drawElementBackgroundCallback;
    public DrawNoneElementCallback? drawNoneElementCallback;
    public ElementHeightCallbackDelegate? elementHeightCallback;
    public ReorderCallbackDelegateWithDetails? onReorderCallbackWithDetails;
    public ReorderCallbackDelegate? onReorderCallback;
    public SelectCallbackDelegate? onSelectCallback;
    public AddCallbackDelegate? onAddCallback;
    public AddDropdownCallbackDelegate? onAddDropdownCallback;
    public RemoveCallbackDelegate? onRemoveCallback;
    public DragCallbackDelegate? onMouseDragCallback;
    public SelectCallbackDelegate? onMouseUpCallback;
    public CanRemoveCallbackDelegate? onCanRemoveCallback;
    public CanAddCallbackDelegate? onCanAddCallback;
    public ChangedCallbackDelegate? onChangedCallback;
    public DeleteArrayElementCallbackDelegate? onDeleteArrayElementCallback;

    private const float ElementSpacing = 2;
    private const float ContentPadding = 4;
    private const float DragThreshold = 3;
    private static readonly List<WeakReference<ReorderableList>> Instances = [];
    private static readonly Defaults SharedDefaults = new();

    private readonly Type? _elementType;
    private readonly List<int> _selection = [];
    private SerializedProperty? _serializedProperty;
    private IList? _list;
    private bool _displayHeader;
    private int _controlId;
    private int _dragFrom = -1;
    private int _dragTo = -1;
    private int _mouseDownIndex = -1;
    private Fix64 _dragStartY;
    private Fix64 _elementsStartY;
    private Rect _mouseDownRect;
    private bool _dragging;

    public bool displayAdd;
    public bool displayRemove;
    public float elementHeight = 21;
    public float headerHeight = 20;
    public float footerHeight = 20;
    public bool showDefaultBackground = true;
    public bool multiSelect { get; set; }

    public static Defaults defaultBehaviours => SharedDefaults;

    public SerializedProperty? serializedProperty
    {
        get => _serializedProperty;
        set
        {
            if (value is not null && !value.isArray)
                throw new ArgumentException("Input property must be an array or IList.", nameof(value));
            _serializedProperty = value;
            _list = null;
            ClampSelection();
        }
    }

    public IList? list
    {
        get => _list;
        set
        {
            _list = value;
            if (value is not null) _serializedProperty = null;
            ClampSelection();
        }
    }

    public bool draggable { get; set; }
    public int count => _serializedProperty?.arraySize ?? _list?.Count ?? 0;

    public int index
    {
        get => _selection.Count > 0 ? _selection[0] : count - 1;
        set => Select(value);
    }

    public ReadOnlyCollection<int> selectedIndices => _selection.AsReadOnly();

    public ReorderableList(IList elements, Type elementType) :
        this(elements, elementType, true, true, true, true) { }

    public ReorderableList(IList elements, Type elementType, bool draggable, bool displayHeader,
        bool displayAddButton, bool displayRemoveButton)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(elementType);
        _list = elements;
        _elementType = elementType;
        Initialize(draggable, displayHeader, displayAddButton, displayRemoveButton);
    }

    public ReorderableList(SerializedObject serializedObject, SerializedProperty elements) :
        this(serializedObject, elements, true, true, true, true) { }

    public ReorderableList(SerializedObject serializedObject, SerializedProperty elements, bool draggable,
        bool displayHeader, bool displayAddButton, bool displayRemoveButton)
    {
        ArgumentNullException.ThrowIfNull(serializedObject);
        ArgumentNullException.ThrowIfNull(elements);
        if (!ReferenceEquals(serializedObject, elements.serializedObject))
            throw new ArgumentException("SerializedObject does not own the supplied property.", nameof(elements));
        if (!elements.isArray)
            throw new ArgumentException("Input property must be an array or IList.", nameof(elements));
        _serializedProperty = elements;
        _elementType = ElementType(elements.valueType);
        Initialize(draggable && elements.editable, displayHeader, displayAddButton, displayRemoveButton);
    }

    private void Initialize(bool canDrag, bool displayHeader, bool displayAddButton, bool displayRemoveButton)
    {
        draggable = canDrag;
        _displayHeader = displayHeader;
        displayAdd = displayAddButton;
        displayRemove = displayRemoveButton;
        lock (Instances) Instances.Add(new WeakReference<ReorderableList>(this));
    }

    public static ReorderableList? GetReorderableListFromSerializedProperty(SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        lock (Instances)
        {
            for (var i = Instances.Count - 1; i >= 0; i--)
            {
                if (!Instances[i].TryGetTarget(out var candidate))
                {
                    Instances.RemoveAt(i);
                    continue;
                }
                if (candidate.serializedProperty is { } serialized &&
                    ReferenceEquals(serialized.serializedObject, property.serializedObject) &&
                    serialized.propertyPath == property.propertyPath)
                    return candidate;
            }
        }
        return null;
    }

    public void DoLayoutList()
    {
        var rect = GUILayoutUtility.GetControlRect((Fix64)GetHeight(), GUILayout.ExpandWidth(true));
        DoList(rect);
    }

    public void DoList(Rect rect) => DoList(rect, rect);

    public void DoList(Rect rect, Rect visibleRect)
    {
        defaultBehaviours.EnsureSkin();
        ClampSelection();
        _controlId = GUIUtility.GetControlID(GetHashCode(), FocusType.Keyboard, rect);

        var header = HeaderRect(rect);
        var elements = ElementsRect(rect);
        var footer = FooterRect(rect);

        DrawHeader(header);
        DrawElements(elements, visibleRect);
        DrawFooter(footer);
        HandlePointerContinuation();
        HandleKeyboard();
    }

    public float GetHeight() => HeaderHeight + ElementsHeight + FooterHeight;

    public void GrabKeyboardFocus()
    {
        if (_controlId != 0) GUIUtility.keyboardControl = _controlId;
    }

    public void ReleaseKeyboardFocus()
    {
        if (GUIUtility.keyboardControl == _controlId) GUIUtility.keyboardControl = 0;
    }

    public bool HasKeyboardControl() => _controlId != 0 && GUIUtility.keyboardControl == _controlId;

    public void ClearSelection() => _selection.Clear();

    public void Select(int selectedIndex, bool append = false)
    {
        if (selectedIndex < 0 || selectedIndex >= count)
        {
            if (!append) _selection.Clear();
            return;
        }
        if (!append) _selection.Clear();
        if (!_selection.Contains(selectedIndex)) _selection.Add(selectedIndex);
        _selection.Sort();
    }

    public void SelectRange(int indexFrom, int indexTo)
    {
        if (!multiSelect)
            throw new InvalidOperationException("Cannot select a range when multiSelect is disabled.");
        _selection.Clear();
        var first = Math.Clamp(Math.Min(indexFrom, indexTo), 0, Math.Max(0, count - 1));
        var last = Math.Clamp(Math.Max(indexFrom, indexTo), 0, Math.Max(0, count - 1));
        for (var current = first; count > 0 && current <= last; current++) _selection.Add(current);
    }

    public bool IsSelected(int selectedIndex) => _selection.BinarySearch(selectedIndex) >= 0;

    public void Deselect(int selectedIndex)
    {
        var found = _selection.BinarySearch(selectedIndex);
        if (found >= 0) _selection.RemoveAt(found);
    }

    private float HeaderHeight => Math.Max(_displayHeader ? headerHeight : 0, 2);
    private float FooterHeight => drawFooterCallback is not null || displayAdd || displayRemove
        ? Math.Max(0, footerHeight)
        : 0;

    private float ElementsHeight
    {
        get
        {
            if (count == 0) return Math.Max(elementHeight, (float)EditorGUIUtility.singleLineHeight) +
                                   ContentPadding * 2;
            var result = ContentPadding * 2;
            for (var i = 0; i < count; i++)
            {
                result += ResolveElementHeight(i);
                if (i + 1 < count) result += ElementSpacing;
            }
            return result;
        }
    }

    private Rect HeaderRect(Rect rect) => new(rect.x, rect.y, rect.width, (Fix64)HeaderHeight);

    private Rect ElementsRect(Rect rect) =>
        new(rect.x, rect.y + (Fix64)HeaderHeight, rect.width, (Fix64)ElementsHeight);

    private Rect FooterRect(Rect rect) =>
        new(rect.x, rect.y + (Fix64)(HeaderHeight + ElementsHeight), rect.width, (Fix64)FooterHeight);

    private void DrawHeader(Rect rect)
    {
        if (rect.height <= 0) return;
        defaultBehaviours.DrawHeaderBackground(rect);
        if (drawHeaderCallback is not null) drawHeaderCallback(rect);
        else if (_displayHeader) defaultBehaviours.DrawHeader(rect, _serializedProperty?.serializedObject,
            _serializedProperty, _list);
    }

    private void DrawElements(Rect rect, Rect visibleRect)
    {
        _elementsStartY = rect.y + (Fix64)ContentPadding;
        if (showDefaultBackground && Event.current.type == EventType.Repaint)
            GUI.Box(rect, GUIContent.none, defaultBehaviours.boxBackground);

        if (count == 0)
        {
            var empty = new Rect(rect.x + Defaults.padding, rect.y + (Fix64)ContentPadding,
                Fix64.Max(0, rect.width - Defaults.padding * 2),
                Fix64.Max(0, rect.height - (Fix64)(ContentPadding * 2)));
            if (drawNoneElementCallback is not null) drawNoneElementCallback(empty);
            else defaultBehaviours.DrawNoneElement(empty, draggable);
            return;
        }

        var y = rect.y + (Fix64)ContentPadding;
        for (var elementIndex = 0; elementIndex < count; elementIndex++)
        {
            var height = (Fix64)ResolveElementHeight(elementIndex);
            var row = new Rect(rect.x + 1, y, Fix64.Max(0, rect.width - 2), height);
            y += height + (Fix64)ElementSpacing;
            if (!Intersects(row, visibleRect)) continue;

            var selected = IsSelected(elementIndex);
            var focused = HasKeyboardControl();
            if (drawElementBackgroundCallback is not null)
                drawElementBackgroundCallback(row, elementIndex, selected, focused);
            else
                defaultBehaviours.DrawElementBackground(row, elementIndex, selected, focused, draggable);

            HandleElementInput(row, elementIndex);
            defaultBehaviours.DrawElementDraggingHandle(row, elementIndex, selected, focused, draggable);
            var content = ContentRect(row);
            if (drawElementCallback is not null)
                drawElementCallback(content, elementIndex, selected, focused);
            else
                defaultBehaviours.DrawElement(content, ElementAt(elementIndex), ListItemAt(elementIndex),
                    selected, focused, draggable, _serializedProperty is not null);
        }
    }

    private void DrawFooter(Rect rect)
    {
        if (rect.height <= 0) return;
        if (drawFooterCallback is not null) drawFooterCallback(rect);
        else defaultBehaviours.DrawFooter(rect, this);
    }

    private void HandleElementInput(Rect row, int elementIndex)
    {
        var current = Event.current;
        if (!GUI.enabled || current.button != 0 || current.type != EventType.MouseDown ||
            !row.Contains(current.mousePosition)) return;

        var handle = new Rect(row.x, row.y, draggable ? Defaults.dragHandleWidth : row.width, row.height);
        var previous = index;
        var append = multiSelect && (current.control || current.command);
        if (current.shift && multiSelect && previous >= 0) SelectRange(previous, elementIndex);
        else if (append && IsSelected(elementIndex)) Deselect(elementIndex);
        else Select(elementIndex, append);
        GUIUtility.keyboardControl = _controlId;
        onSelectCallback?.Invoke(this);
        _mouseDownIndex = elementIndex;
        _mouseDownRect = row;

        if (draggable && handle.Contains(current.mousePosition))
        {
            _dragFrom = elementIndex;
            _dragTo = elementIndex;
            _dragStartY = current.mousePosition.y;
            _dragging = false;
            GUIUtility.hotControl = _controlId;
        }
        current.Use();
    }

    private void HandlePointerContinuation()
    {
        var current = Event.current;
        if (current.type == EventType.MouseDrag && GUIUtility.hotControl == _controlId && _dragFrom >= 0)
        {
            if (Fix64.Abs(current.mousePosition.y - _dragStartY) >= (Fix64)DragThreshold) _dragging = true;
            if (_dragging)
            {
                _dragTo = RowIndexAt(current.mousePosition.y);
                onMouseDragCallback?.Invoke(this);
            }
            current.Use();
            return;
        }

        if (current.type != EventType.MouseUp || _mouseDownIndex < 0) return;
        var ownedHotControl = GUIUtility.hotControl == _controlId;
        if (ownedHotControl) GUIUtility.hotControl = 0;
        var reordered = false;
        if (_dragging && _dragFrom >= 0 && _dragTo >= 0 && _dragFrom != _dragTo)
            reordered = MoveElement(_dragFrom, _dragTo);
        if (!reordered && _mouseDownRect.Contains(current.mousePosition) && IsSelected(_mouseDownIndex))
            onMouseUpCallback?.Invoke(this);
        _dragging = false;
        _dragFrom = _dragTo = -1;
        _mouseDownIndex = -1;
        _mouseDownRect = default;
        if (!ownedHotControl && current.button != 0) return;
        current.Use();
    }

    private void HandleKeyboard()
    {
        var current = Event.current;
        if (!HasKeyboardControl() || current.type != EventType.KeyDown) return;
        if (current.keyCode is KeyCode.Delete or KeyCode.Backspace)
        {
            if (CanRemove())
            {
                if (onRemoveCallback is not null) onRemoveCallback(this);
                else defaultBehaviours.DoRemoveButton(this);
                Changed();
            }
            current.Use();
            return;
        }
        if (count == 0 || current.keyCode is not (KeyCode.UpArrow or KeyCode.DownArrow)) return;
        var next = Math.Clamp(index + (current.keyCode == KeyCode.UpArrow ? -1 : 1), 0, count - 1);
        Select(next);
        onSelectCallback?.Invoke(this);
        current.Use();
    }

    private bool MoveElement(int oldIndex, int newIndex)
    {
        if (!draggable || oldIndex < 0 || oldIndex >= count || newIndex < 0 || newIndex >= count) return false;
        var moved = _serializedProperty is not null
            ? _serializedProperty.MoveArrayElement(oldIndex, newIndex)
            : MoveListElement(_list!, oldIndex, newIndex);
        if (!moved) return false;
        Select(newIndex);
        GUI.changed = true;
        if (onReorderCallbackWithDetails is not null)
            onReorderCallbackWithDetails(this, oldIndex, newIndex);
        else onReorderCallback?.Invoke(this);
        Changed();
        return true;
    }

    private static bool MoveListElement(IList values, int source, int destination)
    {
        if (values.IsReadOnly || source == destination) return source == destination;
        var value = values[source];
        if (values.IsFixedSize)
        {
            if (source < destination)
                for (var i = source; i < destination; i++) values[i] = values[i + 1];
            else
                for (var i = source; i > destination; i--) values[i] = values[i - 1];
            values[destination] = value;
            return true;
        }
        values.RemoveAt(source);
        values.Insert(destination, value);
        return true;
    }

    private int RowIndexAt(Fix64 y)
    {
        var cursor = _elementsStartY;
        for (var i = 0; i < count; i++)
        {
            var height = (Fix64)ResolveElementHeight(i);
            if (y < cursor + height / 2) return i;
            cursor += height + (Fix64)ElementSpacing;
        }
        return Math.Max(0, count - 1);
    }

    private Rect ContentRect(Rect row)
    {
        var left = draggable ? Defaults.dragHandleWidth : Defaults.padding;
        return new Rect(row.x + left, row.y + 1,
            Fix64.Max(0, row.width - left - Defaults.padding), Fix64.Max(0, row.height - 2));
    }

    private float ResolveElementHeight(int elementIndex)
    {
        try
        {
            if (elementHeightCallback is not null) return Math.Max(1, elementHeightCallback(elementIndex));
            if (ElementAt(elementIndex) is { } element)
                return Math.Max(1, (float)EditorGUI.GetPropertyHeight(element, includeChildren: true));
            return Math.Max(1, elementHeight);
        }
        catch (ArgumentOutOfRangeException) { return Math.Max(1, elementHeight); }
    }

    private SerializedProperty? ElementAt(int elementIndex) =>
        _serializedProperty is null || elementIndex < 0 || elementIndex >= _serializedProperty.arraySize
            ? null
            : _serializedProperty.GetArrayElementAtIndex(elementIndex);

    private object? ListItemAt(int elementIndex) =>
        _list is null || elementIndex < 0 || elementIndex >= _list.Count ? null : _list[elementIndex];

    private bool CanAdd() => GUI.enabled && (onCanAddCallback?.Invoke(this) ??
        (_serializedProperty is not null || _list is { IsReadOnly: false, IsFixedSize: false }));

    private bool CanRemove() => GUI.enabled && count > 0 && index >= 0 && index < count &&
        (onCanRemoveCallback?.Invoke(this) ??
         (_serializedProperty is not null || _list is { IsReadOnly: false, IsFixedSize: false }));

    private void Add(Rect buttonRect)
    {
        if (!CanAdd()) return;
        if (onAddDropdownCallback is not null) onAddDropdownCallback(buttonRect, this);
        else if (onAddCallback is not null) onAddCallback(this);
        else defaultBehaviours.DoAddButton(this);
        Changed();
    }

    private void Remove()
    {
        if (!CanRemove()) return;
        if (onRemoveCallback is not null) onRemoveCallback(this);
        else defaultBehaviours.DoRemoveButton(this);
        Changed();
    }

    private void Changed()
    {
        ClampSelection();
        GUI.changed = true;
        onChangedCallback?.Invoke(this);
    }

    private void ClampSelection()
    {
        _selection.RemoveAll(selected => selected < 0 || selected >= count);
        if (_dragFrom >= count || _dragTo >= count)
        {
            _dragFrom = _dragTo = -1;
            _dragging = false;
        }
        if (_mouseDownIndex >= count)
        {
            _mouseDownIndex = -1;
            _mouseDownRect = default;
        }
    }

    private static bool Intersects(Rect left, Rect right) =>
        left.x < right.xMax && left.xMax > right.x && left.y < right.yMax && left.yMax > right.y;

    private static Type ElementType(Type collectionType)
    {
        if (collectionType.IsArray) return collectionType.GetElementType() ?? typeof(object);
        return collectionType.IsGenericType ? collectionType.GetGenericArguments()[0] : typeof(object);
    }

    public sealed class Defaults
    {
        public const int padding = 6;
        public const int dragHandleWidth = 20;

        public GUIContent iconToolbarPlus = new("+", tooltip: "Add to the list");
        public GUIContent iconToolbarPlusMore = new("+", tooltip: "Choose an item to add");
        public GUIContent iconToolbarMinus = new("-", tooltip: "Remove selection from the list");
        public GUIStyle draggingHandle = Named(EditorStyles.miniLabel, "RL DragHandle");
        public GUIStyle headerBackground = Named(EditorStyles.inspectorTitlebar, "RL Header");
        public GUIStyle emptyHeaderBackground = Named(EditorStyles.separator, "RL Empty Header");
        public GUIStyle footerBackground = Named(EditorStyles.toolbar, "RL Footer");
        public GUIStyle boxBackground = Named(EditorStyles.viewBackground, "RL Background");
        public GUIStyle preButton = Named(EditorStyles.miniButton, "RL FooterButton");
        public GUIStyle elementBackground = Named(EditorStyles.treeViewRow, "RL Element");
        private GUISkin _skin = GUI.skin;

        internal void EnsureSkin()
        {
            if (ReferenceEquals(_skin, GUI.skin)) return;
            _skin = GUI.skin;
            draggingHandle = Named(EditorStyles.miniLabel, "RL DragHandle");
            headerBackground = Named(EditorStyles.inspectorTitlebar, "RL Header");
            emptyHeaderBackground = Named(EditorStyles.separator, "RL Empty Header");
            footerBackground = Named(EditorStyles.toolbar, "RL Footer");
            boxBackground = Named(EditorStyles.viewBackground, "RL Background");
            preButton = Named(EditorStyles.miniButton, "RL FooterButton");
            elementBackground = Named(EditorStyles.treeViewRow, "RL Element");
        }

        public void DrawFooter(Rect rect, ReorderableList list)
        {
            if (Event.current.type == EventType.Repaint)
                GUI.Box(rect, GUIContent.none, footerBackground);
            const int buttonWidth = 25;
            var removeRect = new Rect(rect.xMax - buttonWidth, rect.y + 2, buttonWidth,
                Fix64.Max(0, rect.height - 4));
            var addRect = new Rect(removeRect.x - buttonWidth, removeRect.y, buttonWidth, removeRect.height);

            if (list.displayAdd)
            {
                var wasEnabled = GUI.enabled;
                GUI.enabled = list.CanAdd();
                var pressed = GUI.Button(addRect,
                    list.onAddDropdownCallback is null ? iconToolbarPlus : iconToolbarPlusMore, preButton);
                GUI.enabled = wasEnabled;
                if (pressed) list.Add(addRect);
            }
            if (list.displayRemove)
            {
                var wasEnabled = GUI.enabled;
                GUI.enabled = list.CanRemove();
                var pressed = GUI.Button(removeRect, iconToolbarMinus, preButton);
                GUI.enabled = wasEnabled;
                if (pressed) list.Remove();
            }
        }

        public void DoAddButton(ReorderableList list)
        {
            if (list._serializedProperty is { } property)
            {
                property.arraySize++;
                list.Select(property.arraySize - 1);
                return;
            }
            if (list._list is not { IsReadOnly: false, IsFixedSize: false } values) return;
            var type = list._elementType ?? typeof(object);
            object? value = type == typeof(string) ? string.Empty :
                type.IsValueType ? Activator.CreateInstance(type) :
                type.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(type) : null;
            list.Select(values.Add(value));
        }

        public void DoRemoveButton(ReorderableList list)
        {
            var selected = list._selection.Count > 0
                ? list._selection.OrderDescending().ToArray()
                : [list.index];
            var last = -1;
            foreach (var selectedIndex in selected)
            {
                if (selectedIndex < 0 || selectedIndex >= list.count) continue;
                if (list._serializedProperty is { } property)
                {
                    property.DeleteArrayElementAtIndex(selectedIndex);
                }
                else if (list._list is { IsReadOnly: false, IsFixedSize: false } values)
                    values.RemoveAt(selectedIndex);
                last = selectedIndex;
            }
            list.ClearSelection();
            if (list.count > 0) list.Select(Math.Clamp(last - 1, 0, list.count - 1));
        }

        public void DrawHeaderBackground(Rect headerRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.Box(headerRect, GUIContent.none,
                headerRect.height < 5 ? emptyHeaderBackground : headerBackground);
        }

        public void DrawHeader(Rect headerRect, SerializedObject? serializedObject,
            SerializedProperty? element, IList? elementList) =>
            EditorGUI.LabelField(new Rect(headerRect.x + padding, headerRect.y,
                    Fix64.Max(0, headerRect.width - padding * 2), headerRect.height),
                element?.displayName ?? (elementList is null ? "List" : "IList"));

        public void DrawElementBackground(Rect rect, int index, bool selected, bool focused, bool draggable)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (selected)
                GUI.Box(rect, GUIContent.none, focused
                    ? EditorStyles.treeViewRowSelected
                    : new GUIStyle(EditorStyles.treeViewRowSelected) { normal =
                        new GUIStyleState(EditorStyles.treeViewRowSelected.disabled) });
            else
                GUI.Box(rect, GUIContent.none, elementBackground);
        }

        public void DrawElementDraggingHandle(Rect rect, int index, bool selected, bool focused,
            bool draggable)
        {
            if (!draggable || Event.current.type != EventType.Repaint) return;
            GUI.Label(new Rect(rect.x + 5, rect.y, 10, rect.height), "=", draggingHandle);
        }

        public void DrawElement(Rect rect, SerializedProperty? element, object? listItem, bool selected,
            bool focused, bool draggable) =>
            DrawElement(rect, element, listItem, selected, focused, draggable, false);

        public void DrawElement(Rect rect, SerializedProperty? element, object? listItem, bool selected,
            bool focused, bool draggable, bool editable)
        {
            if (editable && element is not null)
                EditorGUI.PropertyField(rect, element, includeChildren: true);
            else
                EditorGUI.LabelField(rect, element?.displayName ?? listItem?.ToString() ?? "Null");
        }

        public void DrawNoneElement(Rect rect, bool draggable) =>
            EditorGUI.LabelField(rect, "List is Empty");

        public void DrawOverMaxMultiEditElement(Rect rect, int maxMultiEditElementCount, bool draggable) =>
            EditorGUI.LabelField(rect,
                $"Arrays larger than {maxMultiEditElementCount} cannot be edited together.");

        private static GUIStyle Named(GUIStyle source, string name)
        {
            var result = new GUIStyle(source) { name = name };
            return result;
        }
    }
}
