using BEngine;
using BEngine.Editor;

namespace UnityEditor.IMGUI.Controls;

/// <summary>
/// Immediate-mode hierarchical control with persistent expansion, selection and scroll state.
/// </summary>
public abstract class TreeView<TIdentifier>
    where TIdentifier : unmanaged, IEquatable<TIdentifier>
{
    public delegate bool DoFoldoutCallback(Rect position, bool expandedState, GUIStyle style);
    public delegate List<TIdentifier> GetNewSelectionFunction(TreeViewItem<TIdentifier> clickedItem,
        bool keepMultiSelection, bool useActionKeyAsShift);

    protected internal struct RowGUIArgs
    {
        public TreeViewItem<TIdentifier> item;
        public string label;
        public Rect rowRect;
        public int row;
        public bool selected;
        public bool focused;
        public bool isRenaming;

        private MultiColumnHeaderState? _columnState;
        private Rect[]? _cellRects;

        public int GetNumVisibleColumns()
        {
            EnsureColumns();
            return _columnState!.visibleColumns.Length;
        }

        public int GetColumn(int visibleColumnIndex)
        {
            EnsureColumns();
            return _columnState!.visibleColumns[visibleColumnIndex];
        }

        public Rect GetCellRect(int visibleColumnIndex)
        {
            EnsureColumns();
            return _cellRects![visibleColumnIndex];
        }

        internal void SetColumns(MultiColumnHeaderState state, Rect[] cellRects)
        {
            _columnState = state;
            _cellRects = cellRects;
        }

        private readonly void EnsureColumns()
        {
            if (_columnState is null || _cellRects is null)
                throw new NotSupportedException(
                    "Column information is only available when the TreeView uses a MultiColumnHeader.");
        }
    }

    protected internal struct DragAndDropArgs
    {
        public DragAndDropPosition dragAndDropPosition;
        public TreeViewItem<TIdentifier>? parentItem;
        public int insertAtIndex;
        public bool performDrop;
    }

    protected struct SetupDragAndDropArgs
    {
        public IList<TIdentifier> draggedItemIDs;
    }

    protected internal struct CanStartDragArgs
    {
        public TreeViewItem<TIdentifier> draggedItem;
        public IList<TIdentifier> draggedItemIDs;
    }

    protected struct RenameEndedArgs
    {
        public bool acceptedRename;
        public TIdentifier itemID;
        public string originalName;
        public string newName;
    }

    protected internal enum DragAndDropPosition
    {
        UponItem,
        BetweenItems,
        OutsideItems
    }

    public static class DefaultGUI
    {
        public static void FoldoutLabel(Rect rect, string label, bool selected, bool focused) =>
            GUI.Label(rect, label, DefaultStyles.foldoutLabel);

        public static void Label(Rect rect, string label, bool selected, bool focused) =>
            GUI.Label(rect, label, DefaultStyles.label);

        public static void LabelRightAligned(Rect rect, string label, bool selected, bool focused) =>
            GUI.Label(rect, label, DefaultStyles.labelRightAligned);

        public static void BoldLabel(Rect rect, string label, bool selected, bool focused) =>
            GUI.Label(rect, label, DefaultStyles.boldLabel);

        public static void BoldLabelRightAligned(Rect rect, string label, bool selected, bool focused) =>
            GUI.Label(rect, label, DefaultStyles.boldLabelRightAligned);
    }

    public static class DefaultStyles
    {
        public static GUIStyle foldoutLabel => EditorStyles.label;
        public static GUIStyle label => EditorStyles.label;
        public static GUIStyle labelRightAligned
        {
            get
            {
                var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight };
                return style;
            }
        }
        public static GUIStyle boldLabel => EditorStyles.boldLabel;
        public static GUIStyle boldLabelRightAligned
        {
            get
            {
                var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight };
                return style;
            }
        }
        public static GUIStyle backgroundEven => EditorStyles.treeViewRow;
        public static GUIStyle backgroundOdd => EditorStyles.viewBackground;
    }

    private sealed class CollapsedChildList : List<TreeViewItem<TIdentifier>>;

    private static int _nextControlId = 100_000;
    private readonly List<TreeViewItem<TIdentifier>> _defaultRows = [];
    private readonly List<Rect> _rowRects = [];
    private TreeViewItem<TIdentifier>? _rootItem;
    private IList<TreeViewItem<TIdentifier>> _rows = [];
    private TreeViewItem<TIdentifier>? _hoveredItem;
    private TreeViewItem<TIdentifier>? _renamingItem;
    private string _renameOriginal = string.Empty;
    private string _renameValue = string.Empty;
    private TreeViewItem<TIdentifier>? _pressedItem;
    private bool _pressedKeepMultiSelection;
    private bool _pressedShift;
    private bool _pressedAction;
    private int _pressedClickCount;
    private bool _emptyAreaPressed;
    private TreeViewItem<TIdentifier>? _dragCandidate;
    private Vector2 _dragStart;
    private bool _dragStarted;
    private Rect _treeViewRect;
    private Rect _bodyRect;
    private Fix64 _contentHeight;
    private Fix64 _contentWidth;
    private Fix64 _lastMinimumRowHeight = -1;
    private float _rowHeight = 22;
    private float _baseIndent;
    private float _depthIndentWidth = 14;
    private float _extraSpaceBeforeIconAndLabel;
    private float _customFoldoutYOffset;
    private float _cellMargin = 6;
    private int _columnIndexForTreeFoldouts;
    private bool _useScrollView = true;
    private bool _enableItemHovering;
    private bool _drawSelection = true;
    private GetNewSelectionFunction? _getNewSelectionOverride;

    protected GetNewSelectionFunction getNewSelectionOverride
    {
        set => _getNewSelectionOverride = value;
    }

    internal bool deselectOnUnhandledMouseDown { get; set; } = true;
    protected DoFoldoutCallback? foldoutOverride { get; set; }
    public TreeViewState<TIdentifier> state { get; }
    public MultiColumnHeader? multiColumnHeader { get; set; }
    protected TreeViewItem<TIdentifier> rootItem => _rootItem ??
        throw new InvalidOperationException("TreeView has not been loaded. Call Reload() first.");
    protected TreeViewItem<TIdentifier>? hoveredItem => _hoveredItem;
    protected bool enableItemHovering
    {
        get => _enableItemHovering;
        set => _enableItemHovering = value;
    }
    protected bool isInitialized => _rootItem is not null;
    protected Rect treeViewRect
    {
        get => _treeViewRect;
        set => _treeViewRect = value;
    }
    protected float baseIndent
    {
        get => _baseIndent;
        set => _baseIndent = value;
    }
    internal bool drawSelection
    {
        get => _drawSelection;
        set => _drawSelection = value;
    }
    protected float foldoutWidth => 14;
    protected float extraSpaceBeforeIconAndLabel
    {
        get => _extraSpaceBeforeIconAndLabel;
        set => _extraSpaceBeforeIconAndLabel = value;
    }
    protected float customFoldoutYOffset
    {
        get => _customFoldoutYOffset;
        set => _customFoldoutYOffset = value;
    }
    protected int columnIndexForTreeFoldouts
    {
        get => _columnIndexForTreeFoldouts;
        set
        {
            if (multiColumnHeader is null)
                throw new InvalidOperationException(
                    "columnIndexForTreeFoldouts requires a MultiColumnHeader.");
            if ((uint)value >= (uint)multiColumnHeader.state.columns.Length)
                throw new ArgumentOutOfRangeException(nameof(value));
            _columnIndexForTreeFoldouts = value;
        }
    }
    protected bool useScrollView
    {
        get => _useScrollView;
        set => _useScrollView = value;
    }
    protected float depthIndentWidth
    {
        get => _depthIndentWidth;
        set => _depthIndentWidth = Math.Max(0, value);
    }
    protected bool showAlternatingRowBackgrounds { get; set; }
    protected bool showBorder { get; set; }
    protected bool showingHorizontalScrollBar => _contentWidth > _bodyRect.width;
    protected bool showingVerticalScrollBar => _contentHeight > _bodyRect.height;
    protected float cellMargin
    {
        get => _cellMargin;
        set => _cellMargin = Math.Max(0, value);
    }
    public float totalHeight => (float)_contentHeight + (showBorder ? 2 : 0);
    protected float rowHeight
    {
        get => Math.Max(_rowHeight, (float)MinimumRowHeight());
        set => _rowHeight = Math.Max(1, value);
    }
    public int treeViewControlID { get; set; }
    protected bool isDragging => _dragStarted;
    public bool hasSearch => !string.IsNullOrEmpty(searchString);
    public string searchString
    {
        get => state.searchString;
        set
        {
            value ??= string.Empty;
            if (string.Equals(state.searchString, value, StringComparison.Ordinal)) return;
            state.searchString = value;
            RefreshRows();
            SearchChanged(value);
            Repaint();
        }
    }

    protected TreeView(TreeViewState<TIdentifier> state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        treeViewControlID = Interlocked.Increment(ref _nextControlId);
    }

    protected TreeView(TreeViewState<TIdentifier> state, MultiColumnHeader multiColumnHeader)
        : this(state)
    {
        this.multiColumnHeader = multiColumnHeader ?? throw new ArgumentNullException(nameof(multiColumnHeader));
    }

    protected abstract TreeViewItem<TIdentifier> BuildRoot();

    protected virtual IList<TreeViewItem<TIdentifier>> BuildRows(TreeViewItem<TIdentifier> root)
    {
        _defaultRows.Clear();
        if (hasSearch)
        {
            AddSearchResults(root, _defaultRows);
            _defaultRows.Sort(static (left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.displayName, right.displayName));
        }
        else AddExpandedRows(root, _defaultRows);
        return _defaultRows;
    }

    public void Reload()
    {
        _rootItem = BuildRoot() ?? throw new InvalidOperationException("BuildRoot returned null.");
        if (_rootItem.depth != -1) _rootItem.depth = -1;
        NormalizeExistingHierarchy(_rootItem, null, -1);
        RefreshRows();
        Repaint();
    }

    public void Repaint() => EditorApplication.QueuePlayerLoopUpdate();

    protected Rect GetCellRectForTreeFoldouts(Rect rowRect)
    {
        if (multiColumnHeader is null)
            throw new InvalidOperationException("A MultiColumnHeader is required.");
        var visibleIndex = multiColumnHeader.GetVisibleColumnIndex(columnIndexForTreeFoldouts);
        if (visibleIndex < 0)
            throw new InvalidOperationException("The foldout column is hidden.");
        return multiColumnHeader.GetCellRect(visibleIndex, rowRect);
    }

    protected Rect GetRowRect(int row)
    {
        EnsureRowRects();
        if ((uint)row >= (uint)_rowRects.Count) throw new ArgumentOutOfRangeException(nameof(row));
        return _rowRects[row];
    }

    public virtual IList<TreeViewItem<TIdentifier>> GetRows() => _rows;

    protected IList<TreeViewItem<TIdentifier>> FindRows(IList<TIdentifier> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.ToHashSet();
        return _rows.Where(item => wanted.Contains(item.id)).ToArray();
    }

    protected TreeViewItem<TIdentifier>? FindItem(TIdentifier id,
        TreeViewItem<TIdentifier> searchFromThisItem) => FindItemRecursive(id, searchFromThisItem);

    protected int FindRowOfItem(TreeViewItem<TIdentifier> item) => _rows.IndexOf(item);

    protected void GetFirstAndLastVisibleRows(out int firstRowVisible, out int lastRowVisible)
    {
        EnsureRowRects();
        if (_rowRects.Count == 0)
        {
            firstRowVisible = lastRowVisible = -1;
            return;
        }
        var top = useScrollView ? state.scrollPos.y : Fix64.Zero;
        var bottom = top + Fix64.Max(0, _bodyRect.height);
        firstRowVisible = _rowRects.FindIndex(rect => rect.yMax >= top);
        lastRowVisible = _rowRects.FindLastIndex(rect => rect.y <= bottom);
        if (firstRowVisible < 0) firstRowVisible = 0;
        if (lastRowVisible < 0) lastRowVisible = _rowRects.Count - 1;
    }

    public void ExpandAll()
    {
        if (_rootItem is null) return;
        var expanded = new List<TIdentifier>();
        CollectExpandable(_rootItem, expanded);
        SetExpanded(expanded);
    }

    public void CollapseAll() => SetExpanded([]);

    public void SetExpandedRecursive(TIdentifier id, bool expanded)
    {
        var item = FindItemInternal(id);
        if (item is null) return;
        var ids = new List<TIdentifier>();
        CollectExpandable(item, ids);
        var current = state.expandedIDs.ToHashSet();
        if (expanded) current.UnionWith(ids);
        else current.ExceptWith(ids);
        ApplyExpanded(current);
    }

    public bool SetExpanded(TIdentifier id, bool expanded)
    {
        var current = state.expandedIDs.ToHashSet();
        var changed = expanded ? current.Add(id) : current.Remove(id);
        if (changed) ApplyExpanded(current);
        return changed;
    }

    public void SetExpanded(IList<TIdentifier> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ApplyExpanded(ids.Distinct());
    }

    public IList<TIdentifier> GetExpanded() => [.. state.expandedIDs];
    public bool IsExpanded(TIdentifier id) => state.expandedIDs.Contains(id);
    public IList<TIdentifier> GetSelection() => [.. state.selectedIDs];
    public void SetSelection(IList<TIdentifier> selectedIDs) =>
        SetSelection(selectedIDs, TreeViewSelectionOptions.None);

    public void SetSelection(IList<TIdentifier> selectedIDs, TreeViewSelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(selectedIDs);
        var nextSelection = selectedIDs.Distinct().ToList();
        var changed = !state.selectedIDs.SequenceEqual(nextSelection);
        state.selectedIDs = nextSelection;
        if (state.selectedIDs.Count > 0)
        {
            state.lastClickedID = state.selectedIDs[^1];
            state.hasLastClickedID = true;
        }
        if ((options & TreeViewSelectionOptions.RevealAndFrame) != 0 && state.selectedIDs.Count > 0)
            FrameItem(state.selectedIDs[^1]);
        if (changed && (options & TreeViewSelectionOptions.FireSelectionChanged) != 0)
            SelectionChanged(GetSelection());
        if (changed) Repaint();
    }

    public bool IsSelected(TIdentifier id) => state.selectedIDs.Contains(id);
    public bool HasSelection() => state.selectedIDs.Count > 0;
    public bool HasFocus() => GUIUtility.keyboardControl == treeViewControlID;
    public void SetFocus() => GUIUtility.keyboardControl = treeViewControlID;

    public void SetFocusAndEnsureSelectedItem()
    {
        SetFocus();
        if (!HasSelection() && _rows.Count > 0)
            SetSelection([_rows[0].id], TreeViewSelectionOptions.FireSelectionChanged |
                                            TreeViewSelectionOptions.RevealAndFrame);
        else if (HasSelection()) FrameItem(state.selectedIDs[^1]);
    }

    protected void SelectionClick(TreeViewItem<TIdentifier> item, bool keepMultiSelection)
    {
        ArgumentNullException.ThrowIfNull(item);
        var evt = Event.current;
        SelectionClick(item, keepMultiSelection, evt.shift, evt.control || evt.command);
    }

    private void SelectionClick(TreeViewItem<TIdentifier> item, bool keepMultiSelection,
        bool shift, bool action)
    {
        List<TIdentifier> selection;
        if (_getNewSelectionOverride is not null)
            selection = _getNewSelectionOverride(item, keepMultiSelection, action);
        else if (shift && state.hasLastClickedID)
            selection = SelectRange(state.lastClickedID, item.id);
        else if ((keepMultiSelection || action) && CanMultiSelect(item))
        {
            selection = [.. state.selectedIDs];
            if (!selection.Remove(item.id)) selection.Add(item.id);
        }
        else selection = [item.id];
        state.lastClickedID = item.id;
        state.hasLastClickedID = true;
        SetSelection(selection, TreeViewSelectionOptions.FireSelectionChanged);
    }

    public bool BeginRename(TreeViewItem<TIdentifier> item) => BeginRename(item, 0);

    public bool BeginRename(TreeViewItem<TIdentifier> item, float delay)
    {
        _ = delay;
        ArgumentNullException.ThrowIfNull(item);
        if (!CanRename(item)) return false;
        if (_renamingItem is not null) FinishRename(true);
        _renamingItem = item;
        _renameOriginal = item.displayName;
        _renameValue = item.displayName;
        SetFocus();
        Repaint();
        return true;
    }

    public void EndRename() => FinishRename(true);

    public void FrameItem(TIdentifier id)
    {
        var item = FindItemInternal(id);
        if (item is null) return;
        var ancestor = item.parent;
        var expanded = state.expandedIDs.ToHashSet();
        while (ancestor is not null && !ReferenceEquals(ancestor, _rootItem))
        {
            expanded.Add(ancestor.id);
            ancestor = ancestor.parent;
        }
        ApplyExpanded(expanded, notify: false);
        EnsureRowRects();
        var row = _rows.IndexOf(item);
        if (row < 0 || !useScrollView) return;
        var rect = _rowRects[row];
        var scrollY = state.scrollPos.y;
        if (rect.y < scrollY) scrollY = rect.y;
        else if (rect.yMax > scrollY + _bodyRect.height)
            scrollY = Fix64.Max(0, rect.yMax - _bodyRect.height);
        state.scrollPos = new Vector2(state.scrollPos.x, scrollY);
    }

    public virtual void OnGUI(Rect rect)
    {
        if (!isInitialized) return;
        _treeViewRect = rect;
        if (showBorder)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.viewBackground);
            rect = new Rect(rect.x + 1, rect.y + 1, Fix64.Max(0, rect.width - 2),
                Fix64.Max(0, rect.height - 2));
        }

        if (multiColumnHeader is not null)
        {
            var headerRect = new Rect(rect.x, rect.y, rect.width,
                Fix64.Min(rect.height, multiColumnHeader.height));
            multiColumnHeader.OnGUI(headerRect, useScrollView ? state.scrollPos.x : Fix64.Zero);
            _bodyRect = new Rect(rect.x, headerRect.yMax, rect.width,
                Fix64.Max(0, rect.height - headerRect.height));
        }
        else _bodyRect = rect;

        EnsureRowRects();
        HandleKeyboard();
        if (useScrollView)
        {
            var viewRect = new Rect(0, 0, Fix64.Max(_bodyRect.width, _contentWidth),
                Fix64.Max(_bodyRect.height, _contentHeight));
            state.scrollPos = GUI.BeginScrollView(_bodyRect, state.scrollPos, viewRect);
            BeforeRowsGUI();
            DrawRows(new Rect(0, 0, viewRect.width, viewRect.height));
            AfterRowsGUI();
            GUI.EndScrollView();
        }
        else
        {
            GUI.BeginClip(_bodyRect);
            BeforeRowsGUI();
            DrawRows(_bodyRect);
            AfterRowsGUI();
            GUI.EndClip();
        }
        HandleOutsideContextClick();
        CommandEventHandling();
    }

    public void SelectAllRows()
    {
        SetSelection(_rows.Where(CanMultiSelect).Select(item => item.id).ToArray(),
            TreeViewSelectionOptions.FireSelectionChanged);
    }

    protected float GetFoldoutIndent(TreeViewItem<TIdentifier> item) =>
        baseIndent + Math.Max(0, item.depth) * depthIndentWidth;

    protected float GetContentIndent(TreeViewItem<TIdentifier> item) =>
        GetFoldoutIndent(item) + foldoutWidth + extraSpaceBeforeIconAndLabel;

    protected IList<TIdentifier> SortItemIDsInRowOrder(IList<TIdentifier> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.ToHashSet();
        return _rows.Where(item => wanted.Contains(item.id)).Select(item => item.id).ToArray();
    }

    protected void CenterRectUsingSingleLineHeight(ref Rect rect)
    {
        var line = EditorGUIUtility.singleLineHeight;
        if (rect.height <= line) return;
        rect = new Rect(rect.x, rect.y + (rect.height - line) / 2, rect.width, line);
    }

    protected void AddExpandedRows(TreeViewItem<TIdentifier> root,
        IList<TreeViewItem<TIdentifier>> rows)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rows);
        if (root.children is null) return;
        foreach (var child in root.children) AddExpandedRowsRecursive(child, rows);
    }

    protected virtual void SelectionChanged(IList<TIdentifier> selectedIds) { }
    protected virtual void SingleClickedItem(TIdentifier id) { }
    protected virtual void DoubleClickedItem(TIdentifier id) { }
    protected virtual void ContextClickedItem(TIdentifier id) { }
    protected virtual void ContextClicked() { }
    protected virtual void ExpandedStateChanged() { }
    protected virtual void SearchChanged(string newSearch) { }
    protected virtual void KeyEvent() { }

    protected virtual IList<TIdentifier> GetAncestors(TIdentifier id)
    {
        var result = new List<TIdentifier>();
        var item = FindItemInternal(id)?.parent;
        while (item is not null)
        {
            result.Add(item.id);
            item = item.parent;
        }
        return result;
    }

    protected virtual IList<TIdentifier> GetDescendantsThatHaveChildren(TIdentifier id)
    {
        var result = new List<TIdentifier>();
        var item = FindItemInternal(id);
        if (item is not null) CollectExpandable(item, result);
        return result;
    }

    protected virtual bool CanMultiSelect(TreeViewItem<TIdentifier> item) => true;
    protected virtual bool CanRename(TreeViewItem<TIdentifier> item) => false;
    protected virtual void RenameEnded(RenameEndedArgs args) { }
    protected virtual bool CanStartDrag(CanStartDragArgs args) => false;
    protected virtual void SetupDragAndDrop(SetupDragAndDropArgs args) { }
    protected virtual DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args) =>
        DragAndDropVisualMode.None;
    protected virtual bool CanBeParent(TreeViewItem<TIdentifier> item) => true;
    protected virtual bool CanChangeExpandedState(TreeViewItem<TIdentifier> item) =>
        !hasSearch && item.hasChildren;
    protected virtual bool DoesItemMatchSearch(TreeViewItem<TIdentifier> item, string search) =>
        item.displayName.Contains(search, StringComparison.OrdinalIgnoreCase);

    protected virtual void RowGUI(RowGUIArgs args) => DefaultRowGUI(args);

    protected virtual void BeforeRowsGUI() { }
    protected virtual void AfterRowsGUI() { }
    protected virtual void RefreshCustomRowHeights()
    {
        _rowRects.Clear();
        EnsureRowRects();
        Repaint();
    }
    protected virtual float GetCustomRowHeight(int row, TreeViewItem<TIdentifier> item) => rowHeight;
    protected virtual Rect GetRenameRect(Rect rowRect, int row, TreeViewItem<TIdentifier> item)
    {
        var rect = GetTreeCellRect(rowRect);
        var indent = (Fix64)GetContentIndent(item);
        return new Rect(rect.x + indent, rect.y, Fix64.Max(0, rect.width - indent), rect.height);
    }

    protected virtual void CommandEventHandling()
    {
        var evt = Event.current;
        if (!HasFocus() || evt.type is not (EventType.ValidateCommand or EventType.ExecuteCommand)) return;
        if (evt.commandName == "SelectAll")
        {
            if (evt.type == EventType.ExecuteCommand) SelectAllRows();
            evt.Use();
        }
        else if (evt.commandName == "FrameSelected")
        {
            if (evt.type == EventType.ExecuteCommand && HasSelection()) FrameItem(state.selectedIDs[^1]);
            evt.Use();
        }
    }

    protected static void SetupParentsAndChildrenFromDepths(TreeViewItem<TIdentifier> root,
        IList<TreeViewItem<TIdentifier>> rows)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rows);
        root.depth = -1;
        root.parent = null;
        root.children = [];
        var parents = new List<TreeViewItem<TIdentifier>> { root };
        foreach (var item in rows)
        {
            if (item is null) throw new ArgumentException("Rows cannot contain null items.", nameof(rows));
            if (item.depth < 0 || item.depth + 1 > parents.Count)
                throw new ArgumentException("Row depths must form a continuous hierarchy.", nameof(rows));
            while (parents.Count > item.depth + 1) parents.RemoveAt(parents.Count - 1);
            item.parent = null;
            item.children = null;
            parents[item.depth].AddChild(item);
            if (parents.Count == item.depth + 1) parents.Add(item);
            else parents[item.depth + 1] = item;
        }
    }

    protected static void SetupDepthsFromParentsAndChildren(TreeViewItem<TIdentifier> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        NormalizeExistingHierarchy(root, null, -1);
    }

    protected static List<TreeViewItem<TIdentifier>> CreateChildListForCollapsedParent() =>
        new CollapsedChildList();

    protected static bool IsChildListForACollapsedParent(IList<TreeViewItem<TIdentifier>> childList) =>
        childList is CollapsedChildList;

    private void RefreshRows()
    {
        if (_rootItem is null)
        {
            _rows = [];
            return;
        }
        _rows = BuildRows(_rootItem) ?? throw new InvalidOperationException("BuildRows returned null.");
        _rowRects.Clear();
        EnsureRowRects();
    }

    private void EnsureRowRects()
    {
        var minimumRowHeight = MinimumRowHeight();
        if (_rowRects.Count == _rows.Count && _lastMinimumRowHeight == minimumRowHeight)
        {
            _contentWidth = multiColumnHeader?.state.widthOfAllVisibleColumns ??
                            Fix64.Max(_bodyRect.width, EstimateContentWidth());
            return;
        }
        _lastMinimumRowHeight = minimumRowHeight;
        _rowRects.Clear();
        var y = Fix64.Zero;
        for (var index = 0; index < _rows.Count; index++)
        {
            var height = Fix64.Max(minimumRowHeight,
                (Fix64)GetCustomRowHeight(index, _rows[index]));
            _rowRects.Add(new Rect(0, y, 1, height));
            y += height;
        }
        _contentHeight = y;
        _contentWidth = multiColumnHeader?.state.widthOfAllVisibleColumns ??
                        Fix64.Max(_bodyRect.width, EstimateContentWidth());
    }

    private Fix64 EstimateContentWidth()
    {
        var widest = Fix64.Zero;
        foreach (var item in _rows)
        {
            var textWidth = DefaultStyles.label.CalcSize(new GUIContent(item.displayName)).x;
            widest = Fix64.Max(widest, (Fix64)GetContentIndent(item) + textWidth + 20);
        }
        return widest;
    }

    private static Fix64 MinimumRowHeight() => Fix64.Max(EditorGUIUtility.singleLineHeight,
        GUITextMetrics.MeasureLineHeight(EditorStyles.treeViewRow.fontSize, GUIUtility.fontFamily));

    private void DrawRows(Rect contentRect)
    {
        _hoveredItem = null;
        var localMouse = Event.current.mousePosition;
        for (var index = 0; index < _rows.Count; index++)
        {
            var source = _rowRects[index];
            var rowRect = useScrollView
                ? new Rect(contentRect.x, source.y, contentRect.width, source.height)
                : new Rect(contentRect.x, contentRect.y + source.y, contentRect.width, source.height);
            if (useScrollView)
            {
                var visibleTop = state.scrollPos.y;
                if (source.yMax < visibleTop || source.y > visibleTop + _bodyRect.height) continue;
            }
            else if (!rowRect.Overlaps(_bodyRect)) continue;

            var item = _rows[index];
            var selected = IsSelected(item.id);
            var focused = HasFocus();
            if (rowRect.Contains(localMouse)) _hoveredItem = item;

            if (showAlternatingRowBackgrounds && index % 2 == 1 && Event.current.type == EventType.Repaint)
                GUI.Box(rowRect, GUIContent.none, DefaultStyles.backgroundOdd);
            if (Event.current.type == EventType.Repaint && (!_drawSelection || !selected))
                GUI.Box(rowRect, GUIContent.none, EditorStyles.treeViewRow);
            if (_drawSelection && selected && Event.current.type == EventType.Repaint)
            {
                if (focused) GUI.Box(rowRect, GUIContent.none, EditorStyles.treeViewRowSelected);
                else
                {
                    var wasEnabled = GUI.enabled;
                    try
                    {
                        GUI.enabled = false;
                        GUI.Box(rowRect, GUIContent.none, EditorStyles.treeViewRowSelected);
                    }
                    finally
                    {
                        GUI.enabled = wasEnabled;
                    }
                }
            }

            var args = new RowGUIArgs
            {
                item = item,
                label = item.displayName,
                rowRect = rowRect,
                row = index,
                selected = selected,
                focused = focused,
                isRenaming = ReferenceEquals(item, _renamingItem)
            };
            if (multiColumnHeader is not null)
            {
                var visible = multiColumnHeader.state.visibleColumns;
                var cells = new Rect[visible.Length];
                for (var column = 0; column < visible.Length; column++)
                    cells[column] = multiColumnHeader.GetCellRect(column, rowRect);
                args.SetColumns(multiColumnHeader.state, cells);
            }

            DrawFoldout(item, rowRect);
            RowGUI(args);
            HandleRowInput(item, rowRect, index);
        }

        HandleDragOverRows(contentRect);
    }

    private void DefaultRowGUI(RowGUIArgs args)
    {
        var cell = GetTreeCellRect(args.rowRect);
        var indent = (Fix64)GetContentIndent(args.item);
        var margin = (Fix64)cellMargin;
        var labelRect = new Rect(cell.x + indent + margin, cell.y,
            Fix64.Max(0, cell.width - indent - margin), cell.height);
        CenterRectUsingSingleLineHeight(ref labelRect);
        if (args.isRenaming)
        {
            var renameRect = GetRenameRect(args.rowRect, args.row, args.item);
            GUI.SetNextControlName($"TreeViewRename:{treeViewControlID}");
            _renameValue = GUI.TextField(renameRect, _renameValue, EditorStyles.textField);
            GUI.FocusControl($"TreeViewRename:{treeViewControlID}");
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode is KeyCode.Return or KeyCode.Escape)
            {
                FinishRename(evt.keyCode == KeyCode.Return);
                evt.Use();
            }
            return;
        }
        GUI.Label(labelRect, new GUIContent(args.label, args.item.icon, string.Empty), DefaultStyles.label);
    }

    private Rect GetTreeCellRect(Rect rowRect)
    {
        if (multiColumnHeader is null) return rowRect;
        var visible = multiColumnHeader.GetVisibleColumnIndex(columnIndexForTreeFoldouts);
        return visible >= 0 ? multiColumnHeader.GetCellRect(visible, rowRect) : rowRect;
    }

    private void DrawFoldout(TreeViewItem<TIdentifier> item, Rect rowRect)
    {
        if (!CanChangeExpandedState(item)) return;
        var cell = GetTreeCellRect(rowRect);
        var foldoutRect = new Rect(cell.x + (Fix64)GetFoldoutIndent(item),
            cell.y + (Fix64)_customFoldoutYOffset, (Fix64)foldoutWidth, cell.height);
        var expanded = IsExpanded(item.id);
        var next = foldoutOverride is not null
            ? foldoutOverride(foldoutRect, expanded, EditorStyles.foldout)
            : GUI.Button(foldoutRect,
                EditorGUIUtility.IconContent(expanded ? "FoldoutOpen" : "FoldoutClosed"),
                EditorStyles.foldout)
                ? !expanded
                : expanded;
        if (next != expanded) SetExpanded(item.id, next);
    }

    private void HandleRowInput(TreeViewItem<TIdentifier> item, Rect rowRect, int row)
    {
        var evt = Event.current;
        if (!rowRect.Contains(evt.mousePosition)) return;
        if (evt.type == EventType.MouseDown && evt.button == 0)
        {
            SetFocus();
            _pressedItem = item;
            _pressedKeepMultiSelection = evt.control || evt.command;
            _pressedShift = evt.shift;
            _pressedAction = evt.control || evt.command;
            _pressedClickCount = evt.clickCount;
            _emptyAreaPressed = false;
            _dragCandidate = item;
            _dragStart = evt.mousePosition;
            _dragStarted = false;
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && evt.button == 0 && _pressedItem is not null)
        {
            var pressedItem = _pressedItem;
            var completeClick = pressedItem.id.Equals(item.id) && !_dragStarted &&
                                !DragAndDrop.isDragging;
            var clickCount = _pressedClickCount;
            var keepMultiSelection = _pressedKeepMultiSelection;
            var shift = _pressedShift;
            var action = _pressedAction;
            ClearPointerPress();
            if (completeClick)
            {
                SelectionClick(item, keepMultiSelection, shift, action);
                if (clickCount >= 2) DoubleClickedItem(item.id);
                else SingleClickedItem(item.id);
            }
            evt.Use();
        }
        else if (evt.type == EventType.ContextClick)
        {
            if (!IsSelected(item.id)) SetSelection([item.id], TreeViewSelectionOptions.FireSelectionChanged);
            ContextClickedItem(item.id);
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && _dragCandidate is not null && !_dragStarted)
        {
            var delta = evt.mousePosition - _dragStart;
            if (Fix64.Abs(delta.x) + Fix64.Abs(delta.y) < 4) return;
            if (DragAndDrop.isDragging)
            {
                _pressedItem = null;
                return;
            }
            var dragged = IsSelected(_dragCandidate.id) ? GetSelection() : [_dragCandidate.id];
            var args = new CanStartDragArgs { draggedItem = _dragCandidate, draggedItemIDs = dragged };
            if (!CanStartDrag(args)) return;
            DragAndDrop.PrepareStartDrag();
            SetupDragAndDrop(new SetupDragAndDropArgs { draggedItemIDs = dragged });
            DragAndDrop.StartDrag(_dragCandidate.displayName);
            _dragStarted = true;
            _pressedItem = null;
            evt.Use();
        }
    }

    private void HandleDragOverRows(Rect contentRect)
    {
        var evt = Event.current;
        if (evt.type is not (EventType.DragUpdated or EventType.DragPerform or EventType.DragExited)) return;
        if (evt.type == EventType.DragExited)
        {
            _dragStarted = false;
            _dragCandidate = null;
            return;
        }
        TreeViewItem<TIdentifier>? target = null;
        var targetRow = -1;
        for (var index = 0; index < _rowRects.Count; index++)
        {
            var source = _rowRects[index];
            var rowRect = useScrollView
                ? new Rect(contentRect.x, source.y, contentRect.width, source.height)
                : new Rect(contentRect.x, contentRect.y + source.y, contentRect.width, source.height);
            if (!rowRect.Contains(evt.mousePosition)) continue;
            target = _rows[index];
            targetRow = index;
            break;
        }

        DragAndDropPosition position;
        TreeViewItem<TIdentifier>? parent;
        var insert = -1;
        if (target is null)
        {
            position = DragAndDropPosition.OutsideItems;
            parent = _rootItem;
        }
        else
        {
            position = CanBeParent(target) ? DragAndDropPosition.UponItem : DragAndDropPosition.BetweenItems;
            parent = position == DragAndDropPosition.UponItem ? target : target.parent;
            if (position == DragAndDropPosition.BetweenItems && parent?.children is { } siblings)
                insert = siblings.IndexOf(target);
            else insert = targetRow;
        }
        var perform = evt.type == EventType.DragPerform;
        var visual = HandleDragAndDrop(new DragAndDropArgs
        {
            dragAndDropPosition = position,
            parentItem = parent,
            insertAtIndex = insert,
            performDrop = perform
        });
        DragAndDrop.visualMode = visual;
        if (perform && visual != DragAndDropVisualMode.None && visual != DragAndDropVisualMode.Rejected)
        {
            DragAndDrop.AcceptDrag();
            _dragStarted = false;
            _dragCandidate = null;
        }
        evt.Use();
    }

    private void HandleKeyboard()
    {
        var evt = Event.current;
        if (!HasFocus() || evt.type != EventType.KeyDown || _rows.Count == 0) return;
        var current = state.hasLastClickedID
            ? _rows.ToList().FindIndex(item => item.id.Equals(state.lastClickedID))
            : -1;
        if (current < 0 && HasSelection())
            current = _rows.ToList().FindIndex(item => IsSelected(item.id));
        if (current < 0) current = 0;
        var next = current;
        var handled = true;
        switch (evt.keyCode)
        {
            case KeyCode.UpArrow: next = Math.Max(0, current - 1); break;
            case KeyCode.DownArrow: next = Math.Min(_rows.Count - 1, current + 1); break;
            case KeyCode.Home: next = 0; break;
            case KeyCode.End: next = _rows.Count - 1; break;
            case KeyCode.PageUp: next = Math.Max(0, current - 10); break;
            case KeyCode.PageDown: next = Math.Min(_rows.Count - 1, current + 10); break;
            case KeyCode.LeftArrow:
                if (IsExpanded(_rows[current].id)) SetExpanded(_rows[current].id, false);
                else if (_rows[current].parent is { } parent && !ReferenceEquals(parent, _rootItem))
                    next = Math.Max(0, _rows.IndexOf(parent));
                break;
            case KeyCode.RightArrow:
                if (_rows[current].hasChildren && !IsExpanded(_rows[current].id))
                    SetExpanded(_rows[current].id, true);
                else if (_rows[current].children is { Count: > 0 })
                    next = Math.Min(_rows.Count - 1, current + 1);
                break;
            case KeyCode.F2:
                BeginRename(_rows[current]);
                break;
            case KeyCode.Return:
                DoubleClickedItem(_rows[current].id);
                break;
            default:
                handled = false;
                break;
        }
        if (!handled) return;
        if (next != current || !HasSelection())
            SetSelection([_rows[next].id], TreeViewSelectionOptions.FireSelectionChanged |
                                                 TreeViewSelectionOptions.RevealAndFrame);
        KeyEvent();
        evt.Use();
    }

    private void HandleOutsideContextClick()
    {
        var evt = Event.current;
        if (evt.type == EventType.ContextClick && _bodyRect.Contains(evt.mousePosition))
        {
            ContextClicked();
            evt.Use();
        }
        else if (evt.type == EventType.MouseDown && evt.button == 0 &&
                 _bodyRect.Contains(evt.mousePosition) && deselectOnUnhandledMouseDown)
        {
            _emptyAreaPressed = true;
            _pressedItem = null;
            _dragCandidate = null;
            _dragStarted = false;
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && evt.button == 0 &&
                 (_emptyAreaPressed || _pressedItem is not null))
        {
            var clearSelection = _emptyAreaPressed && _hoveredItem is null &&
                                 _bodyRect.Contains(evt.mousePosition);
            ClearPointerPress();
            if (clearSelection)
                SetSelection([], TreeViewSelectionOptions.FireSelectionChanged);
            evt.Use();
        }
        else if (evt.type is EventType.MouseLeaveWindow or EventType.DragExited)
        {
            ClearPointerPress();
        }
    }

    private void ClearPointerPress()
    {
        _pressedItem = null;
        _pressedKeepMultiSelection = false;
        _pressedShift = false;
        _pressedAction = false;
        _pressedClickCount = 0;
        _emptyAreaPressed = false;
        _dragCandidate = null;
        _dragStarted = false;
    }

    private void FinishRename(bool accepted)
    {
        if (_renamingItem is null) return;
        var item = _renamingItem;
        var nextName = _renameValue.Trim();
        accepted &= nextName.Length > 0;
        if (accepted) item.displayName = nextName;
        RenameEnded(new RenameEndedArgs
        {
            acceptedRename = accepted,
            itemID = item.id,
            originalName = _renameOriginal,
            newName = accepted ? nextName : _renameOriginal
        });
        _renamingItem = null;
        _renameOriginal = _renameValue = string.Empty;
        SetFocus();
        Repaint();
    }

    private void ApplyExpanded(IEnumerable<TIdentifier> ids, bool notify = true)
    {
        var next = ids.Distinct().ToList();
        var changed = !state.expandedIDs.SequenceEqual(next);
        if (!changed) return;
        state.expandedIDs = next;
        RefreshRows();
        if (notify) ExpandedStateChanged();
        Repaint();
    }

    private void AddExpandedRowsRecursive(TreeViewItem<TIdentifier> item,
        IList<TreeViewItem<TIdentifier>> rows)
    {
        rows.Add(item);
        if (!item.hasChildren || !IsExpanded(item.id) || item.children is null) return;
        foreach (var child in item.children) AddExpandedRowsRecursive(child, rows);
    }

    private void AddSearchResults(TreeViewItem<TIdentifier> item,
        ICollection<TreeViewItem<TIdentifier>> rows)
    {
        if (!ReferenceEquals(item, _rootItem) && DoesItemMatchSearch(item, searchString)) rows.Add(item);
        if (item.children is null) return;
        foreach (var child in item.children) AddSearchResults(child, rows);
    }

    private List<TIdentifier> SelectRange(TIdentifier anchor, TIdentifier clicked)
    {
        var first = _rows.ToList().FindIndex(item => item.id.Equals(anchor));
        var last = _rows.ToList().FindIndex(item => item.id.Equals(clicked));
        if (first < 0 || last < 0) return [clicked];
        if (first > last) (first, last) = (last, first);
        var result = new List<TIdentifier>();
        for (var index = first; index <= last; index++)
            if (CanMultiSelect(_rows[index])) result.Add(_rows[index].id);
        return result;
    }

    private TreeViewItem<TIdentifier>? FindItemInternal(TIdentifier id) =>
        _rootItem is null ? null : FindItemRecursive(id, _rootItem);

    private static TreeViewItem<TIdentifier>? FindItemRecursive(TIdentifier id,
        TreeViewItem<TIdentifier> item)
    {
        if (item.id.Equals(id)) return item;
        if (item.children is null) return null;
        foreach (var child in item.children)
            if (FindItemRecursive(id, child) is { } found) return found;
        return null;
    }

    private static void CollectExpandable(TreeViewItem<TIdentifier> item,
        ICollection<TIdentifier> result)
    {
        if (item.hasChildren) result.Add(item.id);
        if (item.children is null) return;
        foreach (var child in item.children) CollectExpandable(child, result);
    }

    private static void NormalizeExistingHierarchy(TreeViewItem<TIdentifier> item,
        TreeViewItem<TIdentifier>? parent, int depth)
    {
        item.parent = parent;
        item.depth = depth;
        if (item.children is null || IsChildListForACollapsedParent(item.children)) return;
        foreach (var child in item.children) NormalizeExistingHierarchy(child, item, depth + 1);
    }
}

/// <summary>Unity-compatible integer identifier TreeView.</summary>
public abstract class TreeView : TreeView<int>
{
    protected TreeView(TreeViewState state) : base(state) { }
    protected TreeView(TreeViewState state, MultiColumnHeader multiColumnHeader)
        : base(state, multiColumnHeader) { }
}
