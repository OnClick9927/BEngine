using BEngine.Editor.Diagnostics;
using UnityEditor.IMGUI.Controls;

namespace BEngine.Editor;

/// <summary>Hierarchical render-event browser used by the Frame Debugger window.</summary>
internal sealed class FrameDebuggerEventTreeView : TreeView<int>
{
    private const int RootIdentifier = int.MinValue;
    private const string DefaultGroupName = "Render Frame";
    private readonly Action<int> _selectEvent;
    private readonly Dictionary<int, FrameDebuggerTreeItem> _itemsById = [];
    private readonly Dictionary<int, int> _eventIdsBySnapshotIndex = [];
    private FrameDebugCaptureSnapshot? _snapshot;
    private int _nextBranchIdentifier = -1;

    internal FrameDebuggerEventTreeView(TreeViewState<int> state, Action<int> selectEvent)
        : base(state)
    {
        _selectEvent = selectEvent ?? throw new ArgumentNullException(nameof(selectEvent));
        rowHeight = 22;
        depthIndentWidth = 14;
        showAlternatingRowBackgrounds = true;
        showBorder = false;
    }

    internal void SetSnapshot(FrameDebugCaptureSnapshot snapshot, int selectedSnapshotIndex)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var changed = !ReferenceEquals(_snapshot, snapshot) || _snapshot.CaptureId != snapshot.CaptureId;
        if (changed)
        {
            _snapshot = snapshot;
            Reload();
            ExpandAll();
        }

        if (selectedSnapshotIndex < 0 || SelectionRepresents(selectedSnapshotIndex)) return;
        if (!_eventIdsBySnapshotIndex.TryGetValue(selectedSnapshotIndex, out var id)) return;
        SetSelection([id], changed
            ? TreeViewSelectionOptions.RevealAndFrame
            : TreeViewSelectionOptions.None);
    }

    internal bool SelectionRepresents(int snapshotIndex)
    {
        var selection = GetSelection();
        return selection.Count == 1 && _itemsById.TryGetValue(selection[0], out var selected) &&
               selected.LastSnapshotIndex == snapshotIndex;
    }

    protected override TreeViewItem<int> BuildRoot()
    {
        _itemsById.Clear();
        _eventIdsBySnapshotIndex.Clear();
        _nextBranchIdentifier = -1;
        var root = new FrameDebuggerTreeItem(RootIdentifier, -1, "Frame", TreeItemKind.Root);
        if (_snapshot is null) return root;

        FrameDebuggerTreeItem? group = null;
        FrameDebuggerTreeItem? batch = null;
        string? previousGroup = null;
        string? previousBatch = null;
        for (var snapshotIndex = 0; snapshotIndex < _snapshot.Events.Count; snapshotIndex++)
        {
            var renderEvent = _snapshot.Events[snapshotIndex];
            var groupName = DisplayName(renderEvent.Marker.Group, DefaultGroupName);
            if (group is null || !string.Equals(previousGroup, groupName, StringComparison.Ordinal))
            {
                group = NewBranch(0, groupName, TreeItemKind.Group);
                root.AddChild(group);
                previousGroup = groupName;
                previousBatch = null;
                batch = null;
            }

            var batchName = renderEvent.Marker.BatchName?.Trim() ?? string.Empty;
            FrameDebuggerTreeItem parent;
            if (batchName.Length == 0)
            {
                parent = group;
                batch = null;
                previousBatch = null;
            }
            else
            {
                if (batch is null || !string.Equals(previousBatch, batchName, StringComparison.Ordinal))
                {
                    batch = NewBranch(1, batchName, TreeItemKind.Batch);
                    group.AddChild(batch);
                    previousBatch = batchName;
                }
                parent = batch;
            }

            var eventId = EventIdentifier(renderEvent.Index, snapshotIndex);
            var eventItem = new FrameDebuggerTreeItem(eventId, parent.depth + 1,
                EventDisplayName(renderEvent), TreeItemKind.Event)
            {
                Event = renderEvent,
                EventCount = 1,
                LastSnapshotIndex = snapshotIndex,
                SearchText = EventSearchText(renderEvent)
            };
            parent.AddChild(eventItem);
            _itemsById[eventId] = eventItem;
            _eventIdsBySnapshotIndex[snapshotIndex] = eventId;
            IncrementBranch(group, snapshotIndex, renderEvent);
            if (!ReferenceEquals(parent, group)) IncrementBranch(parent, snapshotIndex, renderEvent);
        }
        return root;
    }

    protected override IList<TreeViewItem<int>> BuildRows(TreeViewItem<int> root)
    {
        if (!hasSearch) return base.BuildRows(root);
        var rows = new List<TreeViewItem<int>>();
        if (root.children is null) return rows;
        foreach (var child in root.children) AddSearchRows(child, rows, ancestorMatched: false);
        return rows;
    }

    protected override bool DoesItemMatchSearch(TreeViewItem<int> item, string search) =>
        item is FrameDebuggerTreeItem frameItem &&
        frameItem.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase);

    protected override bool CanMultiSelect(TreeViewItem<int> item) => false;
    protected override bool CanRename(TreeViewItem<int> item) => false;

    protected override void SelectionChanged(IList<int> selectedIds)
    {
        if (selectedIds.Count != 1 || !_itemsById.TryGetValue(selectedIds[0], out var item) ||
            item.LastSnapshotIndex < 0) return;
        _selectEvent(item.LastSnapshotIndex);
    }

    protected override void SingleClickedItem(int id)
    {
        if (Event.current.control || Event.current.command) LocateObject(id, select: false);
    }

    protected override void DoubleClickedItem(int id) => LocateObject(id, select: true);

    protected override void RowGUI(RowGUIArgs args)
    {
        if (args.item is not FrameDebuggerTreeItem item)
        {
            base.RowGUI(args);
            return;
        }

        var indent = (Fix64)GetContentIndent(item);
        var content = new Rect(args.rowRect.x + indent + 5, args.rowRect.y,
            Fix64.Max(0, args.rowRect.width - indent - 10), args.rowRect.height);
        CenterRectUsingSingleLineHeight(ref content);
        var countText = item.Kind is TreeItemKind.Group or TreeItemKind.Batch
            ? item.EventCount.ToString("N0")
            : string.Empty;
        var countWidth = countText.Length == 0
            ? Fix64.Zero
            : EditorStyles.miniLabel.CalcSize(new GUIContent(countText)).x + 8;
        var labelRect = new Rect(content.x, content.y,
            Fix64.Max(0, content.width - countWidth), content.height);
        var countRect = new Rect(content.xMax - countWidth + 4, content.y,
            Fix64.Max(0, countWidth - 4), content.height);
        GUI.Label(labelRect, new GUIContent(item.displayName, item.Tooltip),
            item.Kind == TreeItemKind.Group ? EditorStyles.boldLabel : EditorStyles.label);
        if (countText.Length > 0) GUI.Label(countRect, countText, EditorStyles.miniLabel);
    }

    private FrameDebuggerTreeItem NewBranch(int depth, string name, TreeItemKind kind)
    {
        var item = new FrameDebuggerTreeItem(_nextBranchIdentifier--, depth, name, kind)
        {
            SearchText = name,
            LastSnapshotIndex = -1
        };
        _itemsById[item.id] = item;
        return item;
    }

    private int EventIdentifier(int eventIndex, int snapshotIndex)
    {
        var candidate = eventIndex >= 0 && eventIndex < int.MaxValue ? eventIndex + 1 : snapshotIndex + 1;
        while (candidate <= 0 || _itemsById.ContainsKey(candidate)) candidate++;
        return candidate;
    }

    private static void IncrementBranch(FrameDebuggerTreeItem item, int snapshotIndex,
        FrameDebugEvent renderEvent)
    {
        item.EventCount++;
        item.LastSnapshotIndex = snapshotIndex;
        item.Event ??= renderEvent;
    }

    private void AddSearchRows(TreeViewItem<int> item, ICollection<TreeViewItem<int>> rows,
        bool ancestorMatched)
    {
        var matches = ancestorMatched || DoesItemMatchSearch(item, searchString);
        if (!matches && !BranchContainsMatch(item)) return;
        rows.Add(item);
        if (item.children is null) return;
        foreach (var child in item.children) AddSearchRows(child, rows, matches);
    }

    private bool BranchContainsMatch(TreeViewItem<int> item)
    {
        if (item.children is null) return false;
        foreach (var child in item.children)
            if (DoesItemMatchSearch(child, searchString) || BranchContainsMatch(child)) return true;
        return false;
    }

    private void LocateObject(int id, bool select)
    {
        if (!_itemsById.TryGetValue(id, out var item) || item.Event is not { } renderEvent) return;
        var target = ResolveObject(renderEvent);
        if (target is null) return;
        if (select) Selection.activeObject = target;
        EditorGUIUtility.PingObject(target);
    }

    internal static BObject? ResolveObject(FrameDebugEvent renderEvent)
    {
        if (renderEvent.Marker.SourceInstanceId is int instanceId && instanceId > 0 &&
            EditorUtility.InstanceIDToObject(instanceId) is { } source) return source;
        if (renderEvent.Marker.Material is not { } materialGuid) return null;
        var path = AssetDatabase.GUIDToAssetPath(materialGuid.ToString("N"));
        return path.Length == 0 ? null : AssetDatabase.LoadMainAssetAtPath(path);
    }

    private static string DisplayName(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string EventDisplayName(FrameDebugEvent renderEvent) =>
        $"{renderEvent.Index + 1,3}  {renderEvent.Kind}  {renderEvent.Name}";

    private static string EventSearchText(FrameDebugEvent renderEvent) =>
        $"{renderEvent.Name} {renderEvent.Kind} {renderEvent.MeshLabel} " +
        $"{renderEvent.Marker.Group} {renderEvent.Marker.BatchName} " +
        $"{renderEvent.Marker.SourceName} {renderEvent.Marker.Shader} " +
        $"{renderEvent.Marker.Atlas} {renderEvent.State.ProgramLabel} " +
        renderEvent.State.RenderTargetLabel;

    private static bool IsExecutedAtCurrentStep(FrameDebugEvent item, int stepLimit) =>
        item.Executed && (stepLimit < 0 || item.Index < stepLimit);

    private sealed class FrameDebuggerTreeItem(int id, int depth, string displayName,
        TreeItemKind kind) : TreeViewItem<int>(id, depth, displayName)
    {
        internal TreeItemKind Kind { get; } = kind;
        internal FrameDebugEvent? Event { get; set; }
        internal int EventCount { get; set; }
        internal int LastSnapshotIndex { get; set; } = -1;
        internal string SearchText { get; set; } = displayName;
        internal string Tooltip => Event is { } renderEvent
            ? $"{renderEvent.Name}\n{renderEvent.VertexCount:N0} vertices, " +
              $"{renderEvent.TriangleCount:N0} triangles\nCtrl-click to ping; double-click to select"
            : $"{EventCount:N0} render events";
    }

    private enum TreeItemKind
    {
        Root,
        Group,
        Batch,
        Event
    }
}
