using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class ListView : VisualElement
{
    private IList _itemsSource = Array.Empty<object>();
    private readonly HashSet<int> _selectedIndices = [];
    private int _selectedIndex = -1;
    private int _selectionAnchor = -1;
    private float _scrollOffset;
    private float _fixedItemHeight = 22;
    private SelectionType _selectionType = SelectionType.Single;
    private bool _showAlternatingRowBackgrounds = true;
    private bool _showBorder;
    public IList itemsSource
    {
        get => _itemsSource;
        set
        {
            _itemsSource = value ?? Array.Empty<object>();
            _selectedIndices.RemoveWhere(index => index < 0 || index >= _itemsSource.Count);
            if (_selectedIndex >= _itemsSource.Count) _selectedIndex = -1;
            MarkDirty();
        }
    }
    public Func<object?, string> makeItemText { get; set; } = item => item?.ToString() ?? string.Empty;
    public Func<object?, string> makeItemClass { get; set; } = _ => string.Empty;
    public Func<object?, string> makeItemIcon { get; set; } = _ => string.Empty;
    public int selectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value && _selectedIndices.Count == (value >= 0 ? 1 : 0)) return;
            _selectedIndex = value;
            _selectedIndices.Clear();
            if (value >= 0 && value < _itemsSource.Count && _selectionType != SelectionType.None)
                _selectedIndices.Add(value);
            _selectionAnchor = value;
            MarkDirty();
        }
    }
    public IReadOnlyList<int> selectedIndices => _selectedIndices.OrderBy(index => index).ToArray();
    public IReadOnlyList<object?> selectedItems => _selectedIndices.OrderBy(index => index)
        .Where(index => index >= 0 && index < _itemsSource.Count).Select(index => _itemsSource[index]).ToArray();
    public SelectionType selectionType
    {
        get => _selectionType;
        set
        {
            if (_selectionType == value) return;
            _selectionType = value;
            if (value == SelectionType.None) SetSelectionInternal([], false);
            else if (value == SelectionType.Single && _selectedIndices.Count > 1)
                SetSelectionInternal(_selectedIndex >= 0 ? [_selectedIndex] : [], false);
            MarkDirty();
        }
    }
    public float fixedItemHeight { get => _fixedItemHeight; set => Set(ref _fixedItemHeight, Math.Max(12, value)); }
    public float scrollOffset { get => _scrollOffset; set => Set(ref _scrollOffset, Math.Max(0, value)); }
    public bool showAlternatingRowBackgrounds
    {
        get => _showAlternatingRowBackgrounds;
        set => Set(ref _showAlternatingRowBackgrounds, value);
    }
    public bool showBorder { get => _showBorder; set => Set(ref _showBorder, value); }
    public event Action<object?>? selectionChanged;
    public event Action<IReadOnlyList<object?>>? selectedItemsChanged;
    public event Action<IReadOnlyList<int>>? selectedIndicesChanged;
    public event Action<object?>? itemChosen;
    public event Action<object?, ContextMenuBuilder>? contextMenuRequested;

    public void SetSelection(IEnumerable<int> indices) => SetSelectionInternal(indices, true);
    public void SetSelectionWithoutNotify(IEnumerable<int> indices) => SetSelectionInternal(indices, false);
    public void ClearSelection() => SetSelectionInternal([], true);
    public void RefreshItems() => MarkDirty();
    public void ScrollToItem(int index)
    {
        if (index >= 0 && index < _itemsSource.Count) scrollOffset = index * fixedItemHeight;
    }

    internal void SelectFromView(int index, bool additive = false, bool range = false)
    {
        if (_selectionType == SelectionType.None || index < 0 || index >= _itemsSource.Count) return;
        var indices = _selectedIndices.ToHashSet();
        if (_selectionType == SelectionType.Multiple && range && _selectionAnchor >= 0)
        {
            if (!additive) indices.Clear();
            for (var current = Math.Min(_selectionAnchor, index);
                 current <= Math.Max(_selectionAnchor, index); current++) indices.Add(current);
        }
        else if (_selectionType == SelectionType.Multiple && additive)
        {
            if (!indices.Add(index)) indices.Remove(index);
            _selectionAnchor = index;
        }
        else
        {
            indices.Clear();
            indices.Add(index);
            _selectionAnchor = index;
        }
        SetSelectionInternal(indices, true, indices.Contains(index) ? index : -1);
    }
    internal void ChooseFromView(int index) =>
        itemChosen?.Invoke(index >= 0 && index < _itemsSource.Count ? _itemsSource[index] : null);
    internal void BuildContextMenu(object? item, ContextMenuBuilder menu) => contextMenuRequested?.Invoke(item, menu);

    private void SetSelectionInternal(IEnumerable<int> indices, bool notify, int activeIndex = -1)
    {
        var next = indices.Where(index => index >= 0 && index < _itemsSource.Count).Distinct().ToList();
        if (_selectionType == SelectionType.None) next.Clear();
        if (_selectionType == SelectionType.Single && next.Count > 1) next = [next[^1]];
        if (_selectedIndices.SetEquals(next) && (activeIndex < 0 || _selectedIndex == activeIndex)) return;
        _selectedIndices.Clear();
        foreach (var index in next) _selectedIndices.Add(index);
        _selectedIndex = activeIndex >= 0 ? activeIndex : next.LastOrDefault(-1);
        if (_selectedIndex >= 0) _selectionAnchor = _selectionAnchor < 0 ? _selectedIndex : _selectionAnchor;
        MarkDirty();
        if (!notify) return;
        var item = _selectedIndex >= 0 && _selectedIndex < _itemsSource.Count
            ? _itemsSource[_selectedIndex] : null;
        selectionChanged?.Invoke(item);
        selectedItemsChanged?.Invoke(selectedItems);
        selectedIndicesChanged?.Invoke(selectedIndices);
    }
}
