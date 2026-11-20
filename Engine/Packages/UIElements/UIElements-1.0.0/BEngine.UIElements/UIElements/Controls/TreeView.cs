using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class TreeView : VisualElement
{
    private static readonly ObjectPool<HashSet<int>> ValidIdSets = new(
        static () => new(), onRelease: static ids =>
        {
            ids.Clear();
            if (ids.EnsureCapacity(0) > 2048) ids.TrimExcess();
        }, maxSize: 2);
    private static readonly ObjectPool<Dictionary<int, TreeViewItem>> SelectionMaps = new(
        static () => new(), onRelease: static items =>
        {
            items.Clear();
            if (items.EnsureCapacity(0) > 2048) items.TrimExcess();
        }, maxSize: 4);
    private IReadOnlyList<TreeViewItem> _items = [];
    private readonly HashSet<int> _expandedIds = [];
    private readonly HashSet<int> _selectedIds = [];
    private bool _itemsInitialized;
    private int? _selectedId;
    private int? _selectionAnchorId;
    private int? _hoveredId;
    private int? _pressedId;
    private float _scrollOffset;
    private float _fixedItemHeight = 22;
    private SelectionType _selectionType = SelectionType.Single;
    private bool _showAlternatingRowBackgrounds;
    private bool _showBorder;
    private int? _renamingId;
    private string _renameValue = string.Empty;
    private int? _draggedId;
    private int? _dropTargetId;
    private TreeViewDropPosition _dropPosition = TreeViewDropPosition.Inside;
    private List<(TreeViewItem Item, int Depth)>? _visibleRows;
    private bool _visibleRowsDirty = true;

    public IReadOnlyList<TreeViewItem> items
    {
        get => _items;
        set
        {
            _items = value ?? [];
            _visibleRowsDirty = true;
            var validIds = ValidIdSets.Get();
            try
            {
                CollectIds(_items, validIds);
                _expandedIds.IntersectWith(validIds);
                _selectedIds.IntersectWith(validIds);
                if (_selectedId is { } selected && !validIds.Contains(selected)) _selectedId = null;
                if (_selectionAnchorId is { } anchor && !validIds.Contains(anchor)) _selectionAnchorId = null;
                if (_hoveredId is { } hovered && !validIds.Contains(hovered)) _hoveredId = null;
                if (_pressedId is { } pressed && !validIds.Contains(pressed)) _pressedId = null;
                if (_renamingId is { } renaming && !validIds.Contains(renaming)) CancelRename();
                if (_draggedId is { } dragged && !validIds.Contains(dragged)) CancelDrag();
                if (_dropTargetId is { } dropTarget && !validIds.Contains(dropTarget)) ClearDropTarget();
                if (!_itemsInitialized)
                {
                    foreach (var item in new ItemEnumerator(_items))
                        if (item.Children is { Count: > 0 }) _expandedIds.Add(item.Id);
                    _itemsInitialized = true;
                }
                MarkDirty();
            }
            finally
            {
                ValidIdSets.Release(validIds);
            }
        }
    }

    private static void CollectIds(IReadOnlyList<TreeViewItem> items, HashSet<int> ids)
    {
        foreach (var item in new ItemEnumerator(items))
        {
            ids.Add(item.Id);
            if (item.Children is { } children) CollectIds(children, ids);
        }
    }

    public IReadOnlyList<TreeViewItem> rootItems { get => items; set => items = value; }
    public int? selectedId
    {
        get => _selectedId;
        set
        {
            if (_selectedId == value && _selectedIds.Count == (value is null ? 0 : 1)) return;
            _selectedId = value;
            _selectedIds.Clear();
            if (value is { } id && _selectionType != SelectionType.None) _selectedIds.Add(id);
            _selectionAnchorId = value;
            MarkDirty();
        }
    }
    public IReadOnlyList<int> selectedIds
    {
        get
        {
            var ids = new int[_selectedIds.Count];
            _selectedIds.CopyTo(ids);
            Array.Sort(ids);
            return ids;
        }
    }
    public IReadOnlyList<TreeViewItem> selectedItems
    {
        get
        {
            var selected = _selectedIds.Count == 0 ? [] : new TreeViewItem[_selectedIds.Count];
            var count = 0;
            CollectSelectedItems(_items, ref selected, ref count);
            if (count != selected.Length) Array.Resize(ref selected, count);
            return selected;
        }
    }

    private void CollectSelectedItems(IReadOnlyList<TreeViewItem> items, ref TreeViewItem[] selected, ref int count)
    {
        foreach (var item in new ItemEnumerator(items))
        {
            if (_selectedIds.Contains(item.Id))
            {
                if (count == selected.Length) Array.Resize(ref selected, Math.Max(4, count * 2));
                selected[count++] = item;
            }
            if (item.Children is { } children) CollectSelectedItems(children, ref selected, ref count);
        }
    }
    internal int? hoveredId => _hoveredId;
    internal int? pressedId => _pressedId;
    public SelectionType selectionType
    {
        get => _selectionType;
        set
        {
            if (_selectionType == value) return;
            _selectionType = value;
            if (value == SelectionType.None) SetSelectionInternal([], false);
            else if (value == SelectionType.Single && _selectedIds.Count > 1)
                SetSelectionInternal(_selectedId is { } id ? [id] : [], false);
            MarkDirty();
        }
    }
    public float fixedItemHeight { get => _fixedItemHeight; set => Set(ref _fixedItemHeight, Math.Max(12, value)); }
    public float scrollOffset { get => _scrollOffset; set => SetRepaint(ref _scrollOffset, float.IsFinite(value) ? Math.Max(0, value) : 0); }
    public bool showAlternatingRowBackgrounds
    {
        get => _showAlternatingRowBackgrounds;
        set => Set(ref _showAlternatingRowBackgrounds, value);
    }
    public bool showBorder { get => _showBorder; set => Set(ref _showBorder, value); }
    public bool allowRename { get; set; } = true;
    public bool reorderable { get; set; } = true;
    public int? renamingId => _renamingId;
    public string renameValue => _renameValue;
    public int? draggedId => _draggedId;
    public int? dropTargetId => _dropTargetId;
    public TreeViewDropPosition dropPosition => _dropPosition;
    public Func<TreeViewItem, bool>? canRenameItem { get; set; }
    public Func<TreeViewItem, string, bool>? validateRename { get; set; }
    public Func<TreeViewItem, bool>? canStartDrag { get; set; }
    public Func<TreeViewItem, TreeViewItem, TreeViewDropPosition, bool>? canDrop { get; set; }
    public event Action<TreeViewItem?>? selectionChanged;
    public event Action<IReadOnlyList<TreeViewItem>>? selectedItemsChanged;
    public event Action<TreeViewItem>? itemChosen;
    public event Action<TreeViewItem, ContextMenuBuilder>? contextMenuRequested;
    public event Action<int, bool>? expandedStateChanged;
    public event Action<TreeViewItem>? renameStarted;
    public event Action<TreeViewRenameEvent>? itemRenamed;
    public event Action<TreeViewItem>? dragStarted;
    public event Action<TreeViewDragAndDropEvent>? itemDropped;

    public bool IsExpanded(int id) => _expandedIds.Contains(id);

    public void SetExpanded(int id, bool expanded)
    {
        if (!(expanded ? _expandedIds.Add(id) : _expandedIds.Remove(id))) return;
        _visibleRowsDirty = true;
        MarkDirty();
        expandedStateChanged?.Invoke(id, expanded);
    }

    public void ToggleExpanded(int id) => SetExpanded(id, !IsExpanded(id));

    public void ExpandItem(int id) => SetExpanded(id, true);
    public void CollapseItem(int id) => SetExpanded(id, false);

    public void ExpandAll()
    {
        var changed = ExpandBranches(_items);
        if (!changed) return;
        _visibleRowsDirty = true;
        MarkDirty();
    }

    private bool ExpandBranches(IReadOnlyList<TreeViewItem> items)
    {
        var changed = false;
        foreach (var item in new ItemEnumerator(items))
            if (item.Children is { Count: > 0 } children)
            {
                changed |= _expandedIds.Add(item.Id);
                changed |= ExpandBranches(children);
            }
        return changed;
    }

    public void CollapseAll()
    {
        if (_expandedIds.Count == 0) return;
        _expandedIds.Clear();
        _visibleRowsDirty = true;
        MarkDirty();
    }

    public TreeViewItem? GetItemForId(int id) => FindItem(_items, id);

    private static TreeViewItem? FindItem(IReadOnlyList<TreeViewItem> items, int id)
    {
        foreach (var item in new ItemEnumerator(items))
        {
            if (item.Id == id) return item;
            if (item.Children is { } children && FindItem(children, id) is { } match) return match;
        }
        return null;
    }

    // Arrays are the normal authored tree representation. Keep custom-list
    // enumeration semantics while avoiding an enumerator object for every array.
    private struct ItemEnumerator : IDisposable
    {
        private readonly TreeViewItem[]? _array;
        private readonly IEnumerator<TreeViewItem>? _fallback;
        private int _index;

        internal ItemEnumerator(IReadOnlyList<TreeViewItem> items)
        {
            _array = items as TreeViewItem[];
            _fallback = _array is null ? items.GetEnumerator() : null;
            _index = -1;
        }

        public ItemEnumerator GetEnumerator() => this;
        public TreeViewItem Current => _array is { } array ? array[_index] : _fallback!.Current;
        public bool MoveNext() => _array is { } array ? ++_index < array.Length : _fallback!.MoveNext();
        public void Dispose() => _fallback?.Dispose();
    }

    public T? GetItemDataForId<T>(int id) => GetItemForId(id)?.Data is T data ? data : default;

    public void SetSelection(IEnumerable<int> ids) => SetSelectionInternal(ids, true);
    public void SetSelectionWithoutNotify(IEnumerable<int> ids) => SetSelectionInternal(ids, false);
    public void ClearSelection() => SetSelectionInternal([], true);
    public void RefreshItems() => MarkDirty();

    public void ScrollToItem(int id)
    {
        var rows = GetVisibleRows();
        var index = -1;
        for (var current = 0; current < rows.Count; current++)
        {
            if (rows[current].Item.Id != id) continue;
            index = current;
            break;
        }
        if (index >= 0) scrollOffset = index * fixedItemHeight;
    }

    public bool BeginRename(int id)
    {
        var item = GetItemForId(id);
        if (!allowRename || item is null || canRenameItem?.Invoke(item) == false) return false;
        CancelDrag();
        _renamingId = id;
        _renameValue = item.Text;
        MarkDirty();
        renameStarted?.Invoke(item);
        return true;
    }

    public void SetRenameValue(string value)
    {
        if (_renamingId is null) return;
        value ??= string.Empty;
        if (_renameValue == value) return;
        _renameValue = value;
        MarkDirty();
    }

    public bool CommitRename(string? value = null)
    {
        if (_renamingId is not { } id || GetItemForId(id) is not { } item) return false;
        var nextName = (value ?? _renameValue).Trim();
        if (nextName.Length == 0 || validateRename?.Invoke(item, nextName) == false) return false;
        _renamingId = null;
        _renameValue = string.Empty;
        if (nextName == item.Text)
        {
            MarkDirty();
            return true;
        }
        items = RenameItem(_items, id, nextName);
        itemRenamed?.Invoke(new TreeViewRenameEvent(item, item.Text, nextName));
        return true;
    }

    public void CancelRename()
    {
        if (_renamingId is null) return;
        _renamingId = null;
        _renameValue = string.Empty;
        MarkDirty();
    }

    public bool BeginDrag(int id)
    {
        var item = GetItemForId(id);
        if (!reorderable || item is null || canStartDrag?.Invoke(item) == false) return false;
        CancelRename();
        _draggedId = id;
        _dropTargetId = null;
        _dropPosition = TreeViewDropPosition.Inside;
        MarkDirty();
        dragStarted?.Invoke(item);
        return true;
    }

    public bool UpdateDragTarget(int targetId, TreeViewDropPosition position = TreeViewDropPosition.Inside)
    {
        if (_draggedId is not { } sourceId || GetItemForId(sourceId) is not { } source ||
            GetItemForId(targetId) is not { } target || sourceId == targetId || IsDescendant(source, targetId) ||
            canDrop?.Invoke(source, target, position) == false)
        {
            ClearDropTarget();
            return false;
        }
        if (_dropTargetId == targetId && _dropPosition == position) return true;
        _dropTargetId = targetId;
        _dropPosition = position;
        MarkDirty();
        return true;
    }

    public bool PerformDrop()
    {
        if (_draggedId is not { } sourceId || _dropTargetId is not { } targetId ||
            GetItemForId(sourceId) is not { } source || GetItemForId(targetId) is not { } target)
            return false;
        var withoutSource = RemoveItem(_items, sourceId, out var removed);
        if (removed is null) return false;
        var reordered = InsertItem(withoutSource, removed, targetId, _dropPosition, out var inserted);
        if (!inserted) return false;
        var position = _dropPosition;
        _draggedId = null;
        _dropTargetId = null;
        items = reordered;
        if (position == TreeViewDropPosition.Inside) ExpandItem(targetId);
        itemDropped?.Invoke(new TreeViewDragAndDropEvent(source, target, position));
        return true;
    }

    public void CancelDrag()
    {
        if (_draggedId is null && _dropTargetId is null) return;
        _draggedId = null;
        _dropTargetId = null;
        MarkDirty();
    }

    internal void ScrollBy(float delta, float viewportHeight)
    {
        var contentHeight = GetVisibleRows().Count * fixedItemHeight;
        scrollOffset = Math.Clamp(scrollOffset + delta, 0, Math.Max(0, contentHeight - viewportHeight));
    }

    public IReadOnlyList<(TreeViewItem Item, int Depth)> GetVisibleRows()
    {
        if (!_visibleRowsDirty && _visibleRows is not null) return _visibleRows;
        _visibleRows ??= [];
        _visibleRows.Clear();
        AppendVisible(_items, 0, _visibleRows);
        _visibleRowsDirty = false;
        return _visibleRows;
    }

    internal (TreeViewItem Item, int Depth)? GetRowAtOffset(float offset)
    {
        var rows = GetVisibleRows();
        var index = (int)Math.Floor((offset + scrollOffset) / fixedItemHeight);
        return index >= 0 && index < rows.Count ? rows[index] : null;
    }

    internal void SetHoveredFromView(TreeViewItem? item) => SetInteractionId(ref _hoveredId, item?.Id);
    internal void SetPressedFromView(TreeViewItem? item) => SetInteractionId(ref _pressedId, item?.Id);
    internal bool IsSelected(int id) => _selectedIds.Contains(id);

    internal void SelectFromView(TreeViewItem? item, bool additive = false, bool range = false)
    {
        if (_selectionType == SelectionType.None) return;
        if (item is null)
        {
            SetSelectionInternal([], true);
            return;
        }
        var ids = _selectedIds.ToHashSet();
        if (_selectionType == SelectionType.Multiple && range && _selectionAnchorId is { } anchor)
        {
            var rows = GetVisibleRows();
            var first = -1;
            var last = -1;
            for (var index = 0; index < rows.Count && (first < 0 || last < 0); index++)
            {
                if (rows[index].Item.Id == anchor) first = index;
                if (rows[index].Item.Id == item.Id) last = index;
            }
            if (first >= 0 && last >= 0)
            {
                if (!additive) ids.Clear();
                for (var index = Math.Min(first, last); index <= Math.Max(first, last); index++)
                    ids.Add(rows[index].Item.Id);
            }
        }
        else if (_selectionType == SelectionType.Multiple && additive)
        {
            if (!ids.Add(item.Id)) ids.Remove(item.Id);
            _selectionAnchorId = item.Id;
        }
        else
        {
            ids.Clear();
            ids.Add(item.Id);
            _selectionAnchorId = item.Id;
        }
        SetSelectionInternal(ids, true, ids.Contains(item.Id) ? item : null);
    }
    internal void ChooseFromView(TreeViewItem item) => itemChosen?.Invoke(item);
    internal void BuildContextMenu(TreeViewItem item, ContextMenuBuilder menu) =>
        contextMenuRequested?.Invoke(item, menu);

    private void SetInteractionId(ref int? field, int? value)
    {
        if (field == value) return;
        field = value;
        MarkDirtyRepaint();
    }

    private void AppendVisible(
        IEnumerable<TreeViewItem> items,
        int depth,
        ICollection<(TreeViewItem Item, int Depth)> rows)
    {
        foreach (var item in items)
        {
            rows.Add((item, depth));
            if (item.Children is { Count: > 0 } && IsExpanded(item.Id))
                AppendVisible(item.Children, depth + 1, rows);
        }
    }

    private static IEnumerable<TreeViewItem> Flatten(IEnumerable<TreeViewItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children ?? [])) yield return child;
        }
    }

    private void ClearDropTarget()
    {
        if (_dropTargetId is null) return;
        _dropTargetId = null;
        MarkDirty();
    }

    private static bool IsDescendant(TreeViewItem item, int candidateId) =>
        item.Children is { } children && FindItem(children, candidateId) is not null;

    private static IReadOnlyList<TreeViewItem> RenameItem(
        IEnumerable<TreeViewItem> source,
        int id,
        string name) => source.Select(item => item.Id == id
        ? item with { Text = name }
        : item with { Children = RenameItem(item.Children ?? [], id, name) }).ToArray();

    private static IReadOnlyList<TreeViewItem> RemoveItem(
        IEnumerable<TreeViewItem> source,
        int id,
        out TreeViewItem? removed)
    {
        removed = null;
        var result = new List<TreeViewItem>();
        foreach (var item in source)
        {
            if (item.Id == id)
            {
                removed = item;
                continue;
            }
            var children = RemoveItem(item.Children ?? [], id, out var child);
            removed ??= child;
            result.Add(item with { Children = children });
        }
        return result;
    }

    private static IReadOnlyList<TreeViewItem> InsertItem(
        IEnumerable<TreeViewItem> source,
        TreeViewItem dragged,
        int targetId,
        TreeViewDropPosition position,
        out bool inserted)
    {
        inserted = false;
        var result = new List<TreeViewItem>();
        foreach (var item in source)
        {
            if (item.Id == targetId)
            {
                if (position == TreeViewDropPosition.Before) result.Add(dragged);
                result.Add(position == TreeViewDropPosition.Inside
                    ? item with { Children = (item.Children ?? []).Append(dragged).ToArray() }
                    : item);
                if (position == TreeViewDropPosition.After) result.Add(dragged);
                inserted = true;
                continue;
            }
            var children = InsertItem(item.Children ?? [], dragged, targetId, position, out var childInserted);
            inserted |= childInserted;
            result.Add(item with { Children = children });
        }
        return result;
    }

    private void SetSelectionInternal(IEnumerable<int> ids, bool notify, TreeViewItem? activeItem = null)
    {
        var valid = SelectionMaps.Get();
        try
        {
            CollectItems(_items, valid);
            ApplySelection(ids, notify, activeItem, valid);
        }
        finally { SelectionMaps.Release(valid); }
    }

    private void ApplySelection(IEnumerable<int> ids, bool notify, TreeViewItem? activeItem,
        Dictionary<int, TreeViewItem> valid)
    {
        ArgumentNullException.ThrowIfNull(ids, "source");
        var next = new List<int>();
        var seen = new HashSet<int>();
        foreach (var candidate in ids)
            if (valid.ContainsKey(candidate) && seen.Add(candidate)) next.Add(candidate);
        if (_selectionType == SelectionType.None) next.Clear();
        if (_selectionType == SelectionType.Single && next.Count > 1) next = [next[^1]];
        if (_selectedIds.SetEquals(next) && (activeItem is null || _selectedId == activeItem.Id)) return;
        _selectedIds.Clear();
        foreach (var selected in next) _selectedIds.Add(selected);
        _selectedId = activeItem?.Id ?? next.LastOrDefault();
        if (next.Count == 0) _selectedId = null;
        _selectionAnchorId ??= _selectedId;
        MarkDirty();
        if (!notify) return;
        var primary = activeItem ?? (_selectedId is { } id && valid.TryGetValue(id, out var item) ? item : null);
        selectionChanged?.Invoke(primary);
        selectedItemsChanged?.Invoke(ProjectItems21383());
        [global::BEngine.SkipLoopGuard]
        global::BEngine.UIElements.TreeViewItem[] ProjectItems21383()
        {
            var projectionSource21383 = next;
            ArgumentNullException.ThrowIfNull(projectionSource21383, "source");
            global::System.ReadOnlySpan<int> projectionValues21383 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionSource21383);
            var projectionOutput21383 = projectionValues21383.Length == 0 ? global::System.Array.Empty<global::BEngine.UIElements.TreeViewItem>() : new global::BEngine.UIElements.TreeViewItem[projectionValues21383.Length];
            var projectionIndex21383 = 0;
            foreach (var id in projectionValues21383)
            {
                projectionOutput21383[projectionIndex21383++] = valid[id];
            }
        
            return projectionOutput21383;
        }
    }

    private static void CollectItems(IReadOnlyList<TreeViewItem> items, Dictionary<int, TreeViewItem> result)
    {
        foreach (var item in new ItemEnumerator(items))
        {
            result.Add(item.Id, item);
            if (item.Children is { } children) CollectItems(children, result);
        }
    }
}
