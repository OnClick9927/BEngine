using UnityEditor.IMGUI.Controls;

namespace BEngine.Editor;

/// <summary>Category tree for detailed rendering and memory counters.</summary>
internal sealed class ProfilerCounterTreeView : TreeView<int>
{
    private const int CounterIdentifierOffset = 1000;
    private readonly MultiColumnHeader _header;
    private readonly Action<ProfilerCounterDetail?> _selectionChanged;
    private readonly Dictionary<int, ProfilerCounterDetail> _detailsById = [];
    private ProfilerCounterDetail[] _details = [];
    private string _signature = string.Empty;

    internal ProfilerCounterTreeView(
        TreeViewState<int> state,
        Action<ProfilerCounterDetail?> selectionChanged)
        : this(state, CreateHeader(), selectionChanged)
    {
    }

    private ProfilerCounterTreeView(
        TreeViewState<int> state,
        MultiColumnHeader header,
        Action<ProfilerCounterDetail?> selectionChanged)
        : base(state, header)
    {
        _header = header;
        _selectionChanged = selectionChanged;
        rowHeight = 22;
        depthIndentWidth = 14;
        columnIndexForTreeFoldouts = (int)Column.Counter;
        showAlternatingRowBackgrounds = true;
        showBorder = false;
        Reload();
        ExpandAll();
    }

    internal void SetDetails(IReadOnlyList<ProfilerCounterDetail> details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var signature = string.Join('\u001e', details.Select(static detail => detail.Key));
        if (string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            _details = details.ToArray();
            for (var index = 0; index < _details.Length; index++)
                _detailsById[CounterIdentifierOffset + index] = _details[index];
            var currentSelection = GetSelection();
            _selectionChanged(currentSelection.Count == 1 &&
                              _detailsById.TryGetValue(currentSelection[0], out var currentDetail)
                ? currentDetail
                : null);
            return;
        }

        var selectedKey = GetSelection().Count == 1 &&
                          _detailsById.TryGetValue(GetSelection()[0], out var selected)
            ? selected.Key
            : string.Empty;
        _details = details.ToArray();
        _signature = signature;
        Reload();
        ExpandAll();

        var selectedIndex = string.IsNullOrEmpty(selectedKey)
            ? (_details.Length > 0 ? 0 : -1)
            : Array.FindIndex(_details, detail =>
                string.Equals(detail.Key, selectedKey, StringComparison.Ordinal));
        if (selectedIndex < 0 && _details.Length > 0) selectedIndex = 0;
        if (selectedIndex < 0)
        {
            SetSelection([]);
            _selectionChanged(null);
            return;
        }

        var id = CounterIdentifierOffset + selectedIndex;
        SetSelection([id], TreeViewSelectionOptions.RevealAndFrame);
        _selectionChanged(_details[selectedIndex]);
    }

    protected override TreeViewItem<int> BuildRoot()
    {
        _detailsById.Clear();
        var root = new CounterItem(0, -1, "Profiler Counters", isCategory: true);
        var categories = new Dictionary<string, CounterItem>(StringComparer.Ordinal);
        var categoryIndex = 0;
        for (var index = 0; index < _details.Length; index++)
        {
            var detail = _details[index];
            if (!categories.TryGetValue(detail.Category, out var category))
            {
                category = new CounterItem(-(++categoryIndex), 0, detail.Category,
                    isCategory: true);
                categories.Add(detail.Category, category);
                root.AddChild(category);
            }

            var id = CounterIdentifierOffset + index;
            category.AddChild(new CounterItem(id, 1, detail.Name, isCategory: false));
            _detailsById[id] = detail;
        }
        return root;
    }

    protected override bool CanMultiSelect(TreeViewItem<int> item) => false;
    protected override bool CanRename(TreeViewItem<int> item) => false;

    protected override void SelectionChanged(IList<int> selectedIds)
    {
        var detail = selectedIds.Count == 1 &&
                     _detailsById.TryGetValue(selectedIds[0], out var selected)
            ? selected
            : null;
        _selectionChanged(detail);
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        if (args.item is not CounterItem item)
        {
            base.RowGUI(args);
            return;
        }

        for (var visibleIndex = 0; visibleIndex < args.GetNumVisibleColumns(); visibleIndex++)
        {
            var column = (Column)args.GetColumn(visibleIndex);
            var cell = args.GetCellRect(visibleIndex);
            cell = new Rect(cell.x + 5, cell.y, Fix64.Max(0, cell.width - 10), cell.height);
            CenterRectUsingSingleLineHeight(ref cell);
            if (column == Column.Counter)
            {
                var indent = (Fix64)GetContentIndent(item);
                cell = new Rect(cell.x + indent, cell.y, Fix64.Max(0, cell.width - indent),
                    cell.height);
            }

            _detailsById.TryGetValue(item.id, out var detail);
            var text = column switch
            {
                Column.Counter => item.displayName,
                Column.Value => detail?.DisplayValue ?? string.Empty,
                _ => string.Empty
            };
            GUI.Label(cell, new GUIContent(text, detail?.Description ?? string.Empty),
                item.IsCategory ? EditorStyles.boldLabel : EditorStyles.label);
        }
    }

    private static MultiColumnHeader CreateHeader()
    {
        var state = new MultiColumnHeaderState(
        [
            NewColumn("Counter", 280, 130, 620, TextAnchor.MiddleLeft),
            NewColumn("Value", 160, 90, 320, TextAnchor.MiddleRight)
        ]);
        return new MultiColumnHeader(state) { canSort = false };
    }

    private static MultiColumnHeaderState.Column NewColumn(
        string label,
        int width,
        int minimum,
        int maximum,
        TextAnchor alignment) => new()
    {
        headerContent = new GUIContent(label),
        headerTextAlignment = alignment,
        width = width,
        minWidth = minimum,
        maxWidth = maximum,
        autoResize = true,
        allowToggleVisibility = false,
        canSort = false
    };

    private sealed class CounterItem(
        int id,
        int depth,
        string displayName,
        bool isCategory) : TreeViewItem<int>(id, depth, displayName)
    {
        internal bool IsCategory { get; } = isCategory;
    }

    private enum Column
    {
        Counter,
        Value
    }
}
