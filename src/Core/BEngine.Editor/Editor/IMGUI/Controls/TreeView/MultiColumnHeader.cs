using BEngine;
using BEngine.Editor;

namespace UnityEditor.IMGUI.Controls;

public class MultiColumnHeader
{
    public delegate void HeaderCallback(MultiColumnHeader multiColumnHeader);

    private Rect _lastRect;
    private Fix64 _lastScroll;
    private int _resizeColumn = -1;
    private Fix64 _resizeStartX;
    private Fix64 _resizeStartWidth;

    public static class DefaultGUI
    {
        public static Fix64 columnContentMargin => 6;
    }

    public static class DefaultStyles
    {
        public static GUIStyle background => EditorStyles.toolbar;
        public static GUIStyle columnHeader => EditorStyles.toolbarButton;
    }

    public MultiColumnHeaderState state { get; }
    public bool canSort { get; set; } = true;
    public bool allowDraggingColumnsToReorder { get; set; } = true;
    public Fix64 height { get; set; } = 22;
    public int currentColumnIndex { get; private set; } = -1;
    public int sortedColumnIndex => state.sortedColumnIndex;

    public event HeaderCallback? sortingChanged;
    public event HeaderCallback? visibleColumnsChanged;
    public event HeaderCallback? columnSettingsChanged;
    public event HeaderCallback? columnsSwapped;

    public MultiColumnHeader(MultiColumnHeaderState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public virtual void OnGUI(Rect rect, Fix64 xScroll = default)
    {
        _lastRect = rect;
        _lastScroll = Fix64.Max(0, xScroll);
        state.ClampColumnWidths();
        GUI.Box(rect, GUIContent.none, DefaultStyles.background);

        var x = rect.x - _lastScroll;
        foreach (var columnIndex in state.visibleColumns)
        {
            var column = state.columns[columnIndex];
            var headerRect = new Rect(x, rect.y, column.width, rect.height);
            currentColumnIndex = columnIndex;
            HandleResize(columnIndex, new Rect(headerRect.xMax - 3, headerRect.y, 6, headerRect.height));
            ColumnHeaderGUI(column, headerRect, columnIndex);
            x += column.width;
        }
        currentColumnIndex = -1;

        if (Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition))
        {
            var menu = new GenericMenu();
            AddColumnHeaderContextMenuItems(menu);
            menu.ShowAsContext();
            Event.current.Use();
        }
    }

    public Rect GetCellRect(int visibleColumnIndex, Rect rowRect)
    {
        var visible = state.visibleColumns;
        if (visibleColumnIndex < 0 || visibleColumnIndex >= visible.Length)
            throw new ArgumentOutOfRangeException(nameof(visibleColumnIndex));
        var x = rowRect.x;
        for (var index = 0; index < visibleColumnIndex; index++)
            x += state.columns[visible[index]].width;
        return new Rect(x, rowRect.y, state.columns[visible[visibleColumnIndex]].width, rowRect.height);
    }

    public Rect GetColumnRect(int visibleColumnIndex)
    {
        var cell = GetCellRect(visibleColumnIndex,
            new Rect(_lastRect.x - _lastScroll, _lastRect.y, _lastRect.width, _lastRect.height));
        return new Rect(cell.x, _lastRect.y, cell.width, _lastRect.height);
    }

    public MultiColumnHeaderState.Column GetColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= state.columns.Length)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        return state.columns[columnIndex];
    }

    public int GetVisibleColumnIndex(int columnIndex) => Array.IndexOf(state.visibleColumns, columnIndex);
    public int GetColumnIndex(int visibleColumnIndex)
    {
        var visible = state.visibleColumns;
        if ((uint)visibleColumnIndex >= (uint)visible.Length)
            throw new ArgumentOutOfRangeException(nameof(visibleColumnIndex));
        return visible[visibleColumnIndex];
    }
    public bool IsColumnVisible(int columnIndex) => GetVisibleColumnIndex(columnIndex) >= 0;
    public bool IsSortedAscending(int columnIndex) => GetColumn(columnIndex).sortedAscending;

    public void SetSorting(int columnIndex, bool ascending)
    {
        ValidateSortableColumn(columnIndex);
        state.columns[columnIndex].sortedAscending = ascending;
        state.sortedColumns = [columnIndex];
        OnSortingChanged();
    }

    public void SetSortDirection(int columnIndex, bool ascending) => SetSorting(columnIndex, ascending);

    public void SetSortingColumns(int[] columnIndices, bool[] ascending)
    {
        ArgumentNullException.ThrowIfNull(columnIndices);
        ArgumentNullException.ThrowIfNull(ascending);
        if (columnIndices.Length != ascending.Length)
            throw new ArgumentException("Column and sort-direction arrays must have the same length.");
        foreach (var index in columnIndices) ValidateSortableColumn(index);
        for (var index = 0; index < columnIndices.Length; index++)
            state.columns[columnIndices[index]].sortedAscending = ascending[index];
        state.sortedColumns = columnIndices;
        OnSortingChanged();
    }

    public void ResizeToFit()
    {
        if (_lastRect.width <= 0) return;
        var auto = state.visibleColumns.Where(index => state.columns[index].autoResize).ToArray();
        if (auto.Length == 0) return;
        var fixedWidth = state.visibleColumns.Except(auto).Aggregate(Fix64.Zero,
            (sum, index) => sum + state.columns[index].width);
        var share = Fix64.Max(1, (_lastRect.width - fixedWidth) / auto.Length);
        foreach (var index in auto)
        {
            var column = state.columns[index];
            column.width = Fix64.Clamp(share, column.minWidth, column.maxWidth);
        }
        columnSettingsChanged?.Invoke(this);
        Repaint();
    }

    public void Repaint() => EditorApplication.QueuePlayerLoopUpdate();

    protected virtual void ColumnHeaderGUI(MultiColumnHeaderState.Column column, Rect headerRect,
        int columnIndex)
    {
        var clicked = SortingButton(column, headerRect, columnIndex);
        if (clicked) ColumnHeaderClicked(columnIndex);
    }

    protected bool SortingButton(MultiColumnHeaderState.Column column, Rect headerRect, int columnIndex)
    {
        var content = new GUIContent(column.headerContent);
        if (state.sortedColumns.Contains(columnIndex))
            content.text += column.sortedAscending ? "  ^" : "  v";
        return GUI.Button(headerRect, content, DefaultStyles.columnHeader);
    }

    protected virtual void ColumnHeaderClicked(int columnIndex)
    {
        if (!canSort || !state.columns[columnIndex].canSort) return;
        var ascending = state.sortedColumnIndex == columnIndex
            ? !state.columns[columnIndex].sortedAscending
            : state.columns[columnIndex].sortedAscending;
        SetSorting(columnIndex, ascending);
    }

    protected virtual void AddColumnHeaderContextMenuItems(GenericMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        for (var index = 0; index < state.columns.Length; index++)
        {
            var columnIndex = index;
            var column = state.columns[index];
            var label = string.IsNullOrWhiteSpace(column.contextMenuText)
                ? column.headerContent.text : column.contextMenuText;
            if (column.allowToggleVisibility)
                menu.AddItem(new GUIContent(label), IsColumnVisible(index), () => ToggleVisibility(columnIndex));
            else menu.AddDisabledItem(new GUIContent(label), IsColumnVisible(index));
        }
    }

    public void ToggleVisibility(int columnIndex)
    {
        _ = GetColumn(columnIndex);
        var visible = state.visibleColumns.ToList();
        if (!visible.Remove(columnIndex)) visible.Add(columnIndex);
        if (visible.Count == 0) return;
        state.visibleColumns = visible.ToArray();
        OnVisibleColumnsChanged();
    }

    protected virtual void OnSortingChanged()
    {
        sortingChanged?.Invoke(this);
        Repaint();
    }

    protected virtual void OnVisibleColumnsChanged()
    {
        visibleColumnsChanged?.Invoke(this);
        Repaint();
    }

    protected void SwapColumns(int firstVisibleIndex, int secondVisibleIndex)
    {
        var visible = state.visibleColumns;
        if ((uint)firstVisibleIndex >= (uint)visible.Length || (uint)secondVisibleIndex >= (uint)visible.Length)
            throw new ArgumentOutOfRangeException();
        (visible[firstVisibleIndex], visible[secondVisibleIndex]) =
            (visible[secondVisibleIndex], visible[firstVisibleIndex]);
        state.visibleColumns = visible;
        columnsSwapped?.Invoke(this);
        Repaint();
    }

    private void HandleResize(int columnIndex, Rect handleRect)
    {
        var column = state.columns[columnIndex];
        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);
        var id = GUIUtility.GetControlID(HashCode.Combine(GetHashCode(), columnIndex), FocusType.Passive, handleRect);
        var current = Event.current;
        switch (current.GetTypeForControl(id))
        {
            case EventType.MouseDown when current.button == 0 && handleRect.Contains(current.mousePosition):
                GUIUtility.hotControl = id;
                _resizeColumn = columnIndex;
                _resizeStartX = current.mousePosition.x;
                _resizeStartWidth = column.width;
                current.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == id && _resizeColumn == columnIndex:
                column.width = Fix64.Clamp(_resizeStartWidth + current.mousePosition.x - _resizeStartX,
                    column.minWidth, column.maxWidth);
                columnSettingsChanged?.Invoke(this);
                current.Use();
                Repaint();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                _resizeColumn = -1;
                current.Use();
                break;
        }
    }

    private void ValidateSortableColumn(int columnIndex)
    {
        var column = GetColumn(columnIndex);
        if (!canSort || !column.canSort)
            throw new InvalidOperationException($"Column {columnIndex} cannot be sorted.");
    }
}
