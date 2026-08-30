namespace BEngine.Editor;

internal sealed class ImGuiAdvancedDropdown
{
    private const string SearchControlName = "BEngine.AdvancedDropdown.Search";
    private const int MaximumVisibleRows = 14;
    private static readonly int ControlScope = SearchControlName.GetHashCode(StringComparison.Ordinal);
    private readonly List<AdvancedItem> _items = [];
    private readonly List<AdvancedRow> _filtered = [];
    private readonly AdvancedNode _root = new(0, string.Empty, string.Empty, null);
    private AdvancedNode _currentNode;
    private Vector2 _position;
    private Rect? _anchor;
    private Vector2 _scrollPosition;
    private string _search = string.Empty;
    private int _selectedIndex = -1;
    private int _searchControlId;
    private int _nextNodeId;
    private bool _focusSearch;
    private bool _scrollToSelection;
    private bool _isOpen;

    public ImGuiAdvancedDropdown() => _currentNode = _root;

    public bool isOpen => _isOpen;

    internal string search => _search;
    internal string currentPath => _currentNode.FullPath;
    internal IReadOnlyList<string> visiblePaths => _filtered.Select(static item => item.Path).ToArray();

    public void Open(IEnumerable<GenericMenuItem> items, Vector2 position, Rect? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (isOpen || _searchControlId != 0) Close();
        _items.Clear();
        _root.Clear();
        _currentNode = _root;
        _nextNodeId = 0;
        var index = 0;
        foreach (var item in items)
        {
            if (item.Separator) continue;
            var path = NormalizePath(item.Path);
            if (path.Length == 0) continue;
            var advancedItem = new AdvancedItem(index++, path, item.On, item.Enabled, item.Action);
            _items.Add(advancedItem);
            AddToHierarchy(advancedItem);
        }

        _position = position;
        _anchor = anchor;
        _search = string.Empty;
        _scrollPosition = Vector2.zero;
        _focusSearch = true;
        _scrollToSelection = true;
        _isOpen = _items.Count > 0;
        Refilter();
        if (!_isOpen) Close();
    }

    public void Close()
    {
        var hadInputState = _isOpen || _searchControlId != 0;
        _isOpen = false;
        _items.Clear();
        _filtered.Clear();
        _root.Clear();
        _currentNode = _root;
        _anchor = null;
        _search = string.Empty;
        _scrollPosition = Vector2.zero;
        _selectedIndex = -1;
        _focusSearch = false;
        _scrollToSelection = false;
        GUI.ClearTextState(_searchControlId);
        _searchControlId = 0;
        if (hadInputState) GUI.FocusControl(string.Empty);
    }

    public bool Draw()
    {
        if (!isOpen) return false;
        GUIUtility.BeginContainer(ControlScope);
        try
        {
            return DrawContents();
        }
        finally
        {
            GUIUtility.EndContainer();
        }
    }

    private bool DrawContents()
    {
        var rowHeight = Fix64.Max(20, EditorStyles.menuItem.fixedHeight);
        var searchHeight = Fix64.Max(22, EditorStyles.toolbarSearchField.fixedHeight + 4);
        var headerHeight = rowHeight;
        var width = CalculateWidth();
        var availableHeight = Fix64.Max(searchHeight + headerHeight + rowHeight + 15,
            GUIUtility.currentViewHeight - 4);
        var desiredRows = Math.Clamp(_filtered.Count, 1, MaximumVisibleRows);
        var listHeight = Fix64.Min(rowHeight * desiredRows,
            Fix64.Max(rowHeight, availableHeight - searchHeight - headerHeight - 15));
        var height = searchHeight + headerHeight + listHeight + 15;
        var menuRect = Place(width, height);
        var searchRect = new Rect(menuRect.x + 6, menuRect.y + 6,
            Fix64.Max(1, menuRect.width - 12), searchHeight - 2);
        var headerRect = new Rect(menuRect.x + 3, searchRect.yMax + 3,
            Fix64.Max(1, menuRect.width - 6), headerHeight);
        var listRect = new Rect(menuRect.x + 3, headerRect.yMax + 1,
            Fix64.Max(1, menuRect.width - 6), listHeight);

        var evt = Event.current;
        var eventType = evt.type;
        var rootPointer = evt.mousePosition;
        if (eventType == EventType.KeyDown && HandleKeyboard(evt, rowHeight, listRect.height))
            return isOpen;

        if (eventType == EventType.MouseLeaveWindow ||
            eventType == EventType.MouseMove &&
            !menuRect.Contains(rootPointer) && !(_anchor?.Contains(rootPointer) ?? false))
        {
            evt.Use();
            Close();
            return false;
        }

        if (eventType is EventType.MouseDown or EventType.ContextClick &&
            !menuRect.Contains(rootPointer) && !(_anchor?.Contains(rootPointer) ?? false))
        {
            evt.Use();
            Close();
            return false;
        }

        DrawSurface(menuRect);

        GUI.SetNextControlName(SearchControlName);
        if (_focusSearch)
        {
            GUI.FocusControl(SearchControlName);
            _focusSearch = false;
        }
        var nextSearch = GUI.TextField(searchRect, _search, style: EditorStyles.toolbarSearchField);
        if (_searchControlId == 0 && GUIUtility.textFieldInput &&
            GUI.GetNameOfFocusedControl().Equals(SearchControlName, StringComparison.Ordinal))
            _searchControlId = GUIUtility.keyboardControl;
        if (!nextSearch.Equals(_search, StringComparison.Ordinal))
        {
            _search = nextSearch;
            Refilter();
        }
        if (_search.Length == 0)
        {
            GUI.Label(new Rect(searchRect.x + 8, searchRect.y,
                    Fix64.Max(0, searchRect.width - 16), searchRect.height),
                "Search...", EditorStyles.miniLabel);
        }

        DrawHierarchyHeader(headerRect);
        if (_scrollToSelection)
        {
            EnsureSelectionVisible(rowHeight, listRect.height);
            _scrollToSelection = false;
        }
        DrawItems(listRect, rowHeight);

        if (ImGuiPopupMenu.IsInputEvent(eventType) && evt.type != EventType.Used)
            evt.Use();
        return isOpen;
    }

    private bool HandleKeyboard(Event evt, Fix64 rowHeight, Fix64 listHeight)
    {
        switch (evt.keyCode)
        {
            case KeyCode.Escape:
                evt.Use();
                Close();
                return true;
            case KeyCode.UpArrow:
                MoveSelection(-1);
                EnsureSelectionVisible(rowHeight, listHeight);
                evt.Use();
                return false;
            case KeyCode.DownArrow:
                MoveSelection(1);
                EnsureSelectionVisible(rowHeight, listHeight);
                evt.Use();
                return false;
            case KeyCode.PageUp:
                MoveSelection(-MaximumVisibleRows);
                EnsureSelectionVisible(rowHeight, listHeight);
                evt.Use();
                return false;
            case KeyCode.PageDown:
                MoveSelection(MaximumVisibleRows);
                EnsureSelectionVisible(rowHeight, listHeight);
                evt.Use();
                return false;
            case KeyCode.LeftArrow when _search.Length == 0 && _currentNode.Parent is not null:
                NavigateBack();
                evt.Use();
                return false;
            case KeyCode.Backspace when _search.Length == 0 && _currentNode.Parent is not null:
                NavigateBack();
                evt.Use();
                return false;
            case KeyCode.RightArrow when SelectedRow is { IsGroup: true }:
                EnterSelectedGroup();
                evt.Use();
                return false;
            case KeyCode.Return:
                evt.Use();
                ActivateSelected();
                return true;
            default:
                return false;
        }
    }

    private void DrawHierarchyHeader(Rect rect)
    {
        if (Event.current.type == EventType.Repaint)
            GUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1),
                EditorStyles.separator.normal.backgroundColor);

        if (_search.Length > 0)
        {
            GUI.Label(new Rect(rect.x + 9, rect.y, Fix64.Max(1, rect.width - 18), rect.height),
                "Search Results", EditorStyles.boldLabel);
            return;
        }

        if (_currentNode.Parent is null)
        {
            GUI.Label(new Rect(rect.x + 9, rect.y, Fix64.Max(1, rect.width - 18), rect.height),
                "All", EditorStyles.boldLabel);
            return;
        }

        var clicked = GUI.Button(rect, GUIContent.none, EditorStyles.menuItem);
        GUI.Label(new Rect(rect.x + 7, rect.y, 18, rect.height), "<", EditorStyles.boldLabel);
        GUI.Label(new Rect(rect.x + 25, rect.y, Fix64.Max(1, rect.width - 32), rect.height),
            _currentNode.FullPath, EditorStyles.boldLabel);
        if (clicked) NavigateBack();
    }

    private void DrawItems(Rect listRect, Fix64 rowHeight)
    {
        var contentHeight = Fix64.Max(listRect.height, rowHeight * Math.Max(1, _filtered.Count));
        var contentWidth = Fix64.Max(1, listRect.width - (contentHeight > listRect.height ? 10 : 0));
        _scrollPosition = GUI.BeginScrollView(listRect, _scrollPosition,
            new Rect(0, 0, contentWidth, contentHeight));
        try
        {
            if (_filtered.Count == 0)
            {
                GUI.Label(new Rect(8, 0, Fix64.Max(1, contentWidth - 16), rowHeight),
                    "No results", EditorStyles.menuItemDisabled);
                return;
            }

            for (var index = 0; index < _filtered.Count; index++)
            {
                var item = _filtered[index];
                var row = new Rect(0, rowHeight * index, contentWidth, rowHeight);
                var hovered = row.Contains(Event.current.mousePosition);
                if (hovered && Event.current.type == EventType.MouseMove && item.Enabled)
                    _selectedIndex = index;
                var previousEnabled = GUI.enabled;
                GUI.enabled = item.Enabled;
                var clicked = GUI.Button(row, GUIContent.none,
                    item.Enabled ? EditorStyles.menuItem : EditorStyles.menuItemDisabled);
                if (index == _selectedIndex && Event.current.type == EventType.Repaint)
                    GUI.DrawRect(row, EditorStyles.selectionRect.normal.backgroundColor);
                var style = item.Enabled ? EditorStyles.menuItem : EditorStyles.menuItemDisabled;
                if (item.On)
                    GUI.Label(new Rect(row.x + 5, row.y, 16, row.height),
                        new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Check, "Checked"));
                var rightPadding = item.IsGroup ? 51 : 34;
                GUI.Label(new Rect(row.x + 27, row.y,
                        Fix64.Max(0, row.width - rightPadding), row.height),
                    item.Label, style);
                if (item.IsGroup)
                    GUI.Label(new Rect(row.xMax - 17, row.y, 14, row.height), ">", style);
                GUI.enabled = previousEnabled;

                if (!clicked || !item.Enabled) continue;
                _selectedIndex = index;
                ActivateSelected();
                return;
            }
        }
        finally
        {
            GUI.EndScrollView();
        }
    }

    private AdvancedRow? SelectedRow => _selectedIndex >= 0 && _selectedIndex < _filtered.Count
        ? _filtered[_selectedIndex]
        : null;

    private void ActivateSelected()
    {
        if (SelectedRow is not { Enabled: true } item) return;
        if (item.IsGroup)
        {
            EnterGroup(item);
            return;
        }

        if (item.Action is null) return;
        var action = item.Action;
        var path = item.Path;
        Close();
        EditorFeatureGuard.Invoke($"AdvancedDropdown {path}", action);
    }

    private void EnterSelectedGroup()
    {
        if (SelectedRow is { IsGroup: true, Enabled: true } item)
            EnterGroup(item);
    }

    private void EnterGroup(AdvancedRow item)
    {
        if (item.Node is null) return;
        _currentNode = item.Node;
        _focusSearch = true;
        Refilter();
    }

    private void NavigateBack()
    {
        if (_currentNode.Parent is null) return;
        _currentNode = _currentNode.Parent;
        _focusSearch = true;
        Refilter();
    }

    private void MoveSelection(int delta)
    {
        if (_filtered.Count == 0 || delta == 0) return;
        var direction = Math.Sign(delta);
        var remaining = Math.Abs(delta);
        var index = _selectedIndex;
        if (index < 0) index = direction > 0 ? -1 : 0;
        while (remaining > 0)
        {
            var found = false;
            for (var attempts = 0; attempts < _filtered.Count; attempts++)
            {
                index = (index + direction + _filtered.Count) % _filtered.Count;
                if (!_filtered[index].Enabled) continue;
                found = true;
                break;
            }
            if (!found) return;
            remaining--;
        }
        _selectedIndex = index;
    }

    private void EnsureSelectionVisible(Fix64 rowHeight, Fix64 listHeight)
    {
        if (_selectedIndex < 0) return;
        var top = rowHeight * _selectedIndex;
        var bottom = top + rowHeight;
        if (top < _scrollPosition.y)
            _scrollPosition = new Vector2(_scrollPosition.x, top);
        else if (bottom > _scrollPosition.y + listHeight)
            _scrollPosition = new Vector2(_scrollPosition.x, Fix64.Max(0, bottom - listHeight));
    }

    private void Refilter()
    {
        var previousId = SelectedRow?.SelectionId ?? int.MinValue;
        _filtered.Clear();
        if (string.IsNullOrWhiteSpace(_search))
        {
            foreach (var node in _currentNode.Children)
                _filtered.Add(AdvancedRow.FromNode(node));
        }
        else
        {
            foreach (var item in _items)
            {
                if (MatchesSearch(item.Path, _search))
                    _filtered.Add(AdvancedRow.FromSearch(item));
            }
        }

        _selectedIndex = previousId == int.MinValue
            ? -1
            : _filtered.FindIndex(item => item.SelectionId == previousId && item.Enabled);
        if (_selectedIndex < 0)
            _selectedIndex = _filtered.FindIndex(static item => item.Enabled && item.On);
        if (_selectedIndex < 0)
            _selectedIndex = _filtered.FindIndex(static item => item.Enabled);
        _scrollPosition = Vector2.zero;
        _scrollToSelection = true;
    }

    private void AddToHierarchy(AdvancedItem item)
    {
        var level = _root;
        foreach (var segment in SplitPath(item.Path))
        {
            var child = level.Children.FirstOrDefault(candidate =>
                candidate.Name.Equals(segment, StringComparison.Ordinal));
            if (child is null)
            {
                var path = level.Parent is null ? segment : $"{level.FullPath}/{segment}";
                child = new AdvancedNode(++_nextNodeId, segment, path, level);
                level.Children.Add(child);
            }
            level = child;
        }
        level.Item = item;
    }

    private Rect Place(Fix64 width, Fix64 height)
    {
        var maximumX = Fix64.Max(2, GUIUtility.currentViewWidth - width - 2);
        var x = Fix64.Clamp(_position.x, 2, maximumX);
        var maximumY = Fix64.Max(2, GUIUtility.currentViewHeight - height - 2);
        var y = _position.y;
        if (_anchor is { } anchor && y + height > GUIUtility.currentViewHeight - 2 &&
            anchor.y - height >= 2)
            y = anchor.y - height;
        y = Fix64.Clamp(y, 2, maximumY);
        return new Rect(x, y, width, height);
    }

    private Fix64 CalculateWidth()
    {
        var desired = _items.Select(static item =>
                EditorStyles.menuItem.CalcSize(new GUIContent(item.Path)).x + 72)
            .DefaultIfEmpty(280).Max();
        return Fix64.Clamp(Fix64.Max(280, desired), 120,
            Fix64.Max(120, GUIUtility.currentViewWidth - 4));
    }

    private static void DrawSurface(Rect rect)
    {
        if (Event.current.type != EventType.Repaint) return;
        var style = EditorStyles.dropDownList;
        GUI.DrawRect(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height),
            style.disabled.backgroundColor);
        GUI.Box(rect, GUIContent.none, style);
    }

    private static string NormalizePath(string path) => string.Join('/', SplitPath(path));

    private static string[] SplitPath(string path) => (path ?? string.Empty).Replace('\\', '/').Split('/',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool MatchesSearch(string path, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(term => path.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record AdvancedItem(
        int Id,
        string Path,
        bool On,
        bool Enabled,
        Action? Action);

    private sealed class AdvancedNode(
        int id,
        string name,
        string fullPath,
        AdvancedNode? parent)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public string FullPath { get; } = fullPath;
        public AdvancedNode? Parent { get; } = parent;
        public List<AdvancedNode> Children { get; } = [];
        public AdvancedItem? Item { get; set; }
        public bool IsEnabled => Children.Count > 0
            ? Children.Any(static child => child.IsEnabled)
            : Item is { Enabled: true };

        public void Clear()
        {
            Children.Clear();
            Item = null;
        }
    }

    private sealed record AdvancedRow(
        int SelectionId,
        string Label,
        string Path,
        bool On,
        bool Enabled,
        Action? Action,
        AdvancedNode? Node,
        bool IsGroup)
    {
        public static AdvancedRow FromNode(AdvancedNode node)
        {
            var item = node.Item;
            var isGroup = node.Children.Count > 0;
            return new AdvancedRow(-(node.Id + 1), node.Name, node.FullPath,
                item?.On ?? false, node.IsEnabled, isGroup ? null : item?.Action, node, isGroup);
        }

        public static AdvancedRow FromSearch(AdvancedItem item) => new(item.Id, item.Path,
            item.Path, item.On, item.Enabled, item.Action, null, false);
    }
}
