using BEngine;
using BEngine.Editor;

namespace UnityEditor.IMGUI.Controls;

public class MultiColumnHeaderState
{
    public class Column
    {
        public GUIContent headerContent { get; set; } = new();
        public TextAnchor headerTextAlignment { get; set; } = TextAnchor.MiddleLeft;
        public TextAnchor sortingArrowAlignment { get; set; } = TextAnchor.MiddleRight;
        public Fix64 width { get; set; } = 80;
        public Fix64 minWidth { get; set; } = 20;
        public Fix64 maxWidth { get; set; } = 10000;
        public bool autoResize { get; set; } = true;
        public bool allowToggleVisibility { get; set; } = true;
        public bool canSort { get; set; } = true;
        public bool sortedAscending { get; set; } = true;
        public int userData { get; set; }
        public string contextMenuText { get; set; } = string.Empty;

        internal Column Clone() => new()
        {
            headerContent = new GUIContent(headerContent),
            headerTextAlignment = headerTextAlignment,
            sortingArrowAlignment = sortingArrowAlignment,
            width = width,
            minWidth = minWidth,
            maxWidth = maxWidth,
            autoResize = autoResize,
            allowToggleVisibility = allowToggleVisibility,
            canSort = canSort,
            sortedAscending = sortedAscending,
            userData = userData,
            contextMenuText = contextMenuText
        };
    }

    private int[] _visibleColumns;
    private int[] _sortedColumns = [];
    private int _maximumNumberOfSortedColumns = 1;

    public Column[] columns { get; }

    public int[] visibleColumns
    {
        get => [.. _visibleColumns];
        set => _visibleColumns = NormalizeIndices(value, requireOne: columns.Length > 0);
    }

    public int[] sortedColumns
    {
        get => [.. _sortedColumns];
        set => _sortedColumns = NormalizeIndices(value, requireOne: false)
            .Take(maximumNumberOfSortedColumns).ToArray();
    }

    public int sortedColumnIndex
    {
        get => _sortedColumns.FirstOrDefault(-1);
        set => sortedColumns = value < 0 ? [] : [value];
    }

    public int maximumNumberOfSortedColumns
    {
        get => _maximumNumberOfSortedColumns;
        set
        {
            _maximumNumberOfSortedColumns = Math.Max(1, value);
            if (_sortedColumns.Length > _maximumNumberOfSortedColumns)
                _sortedColumns = _sortedColumns[.._maximumNumberOfSortedColumns];
        }
    }

    public Fix64 widthOfAllVisibleColumns => _visibleColumns.Aggregate(Fix64.Zero,
        (width, index) => width + columns[index].width);

    public MultiColumnHeaderState(Column[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        this.columns = columns.Select(static column =>
            column ?? throw new ArgumentException("Columns cannot contain null.", nameof(columns))).ToArray();
        _visibleColumns = Enumerable.Range(0, columns.Length).ToArray();
        ClampColumnWidths();
    }

    public static bool CanOverwriteSerializedFields(MultiColumnHeaderState source,
        MultiColumnHeaderState destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        return source.columns.Length == destination.columns.Length;
    }

    public static void OverwriteSerializedFields(MultiColumnHeaderState source,
        MultiColumnHeaderState destination)
    {
        if (!CanOverwriteSerializedFields(source, destination))
            throw new ArgumentException("Multi-column header states must have the same column count.");

        for (var index = 0; index < source.columns.Length; index++)
        {
            var copy = source.columns[index].Clone();
            var target = destination.columns[index];
            target.headerContent = copy.headerContent;
            target.headerTextAlignment = copy.headerTextAlignment;
            target.sortingArrowAlignment = copy.sortingArrowAlignment;
            target.width = copy.width;
            target.minWidth = copy.minWidth;
            target.maxWidth = copy.maxWidth;
            target.autoResize = copy.autoResize;
            target.allowToggleVisibility = copy.allowToggleVisibility;
            target.canSort = copy.canSort;
            target.sortedAscending = copy.sortedAscending;
            target.userData = copy.userData;
            target.contextMenuText = copy.contextMenuText;
        }

        destination.maximumNumberOfSortedColumns = source.maximumNumberOfSortedColumns;
        destination.visibleColumns = source.visibleColumns;
        destination.sortedColumns = source.sortedColumns;
        destination.ClampColumnWidths();
    }

    internal void ClampColumnWidths()
    {
        foreach (var column in columns)
        {
            column.minWidth = Fix64.Max(1, column.minWidth);
            column.maxWidth = Fix64.Max(column.minWidth, column.maxWidth);
            column.width = Fix64.Clamp(column.width, column.minWidth, column.maxWidth);
        }
    }

    private int[] NormalizeIndices(IEnumerable<int>? source, bool requireOne)
    {
        var result = (source ?? []).Where(index => index >= 0 && index < columns.Length)
            .Distinct().ToArray();
        if (requireOne && result.Length == 0) result = [0];
        return result;
    }
}
