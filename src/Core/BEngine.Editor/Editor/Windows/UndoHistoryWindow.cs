using UnityEditor.IMGUI.Controls;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Toolbar/UndoHistory.png")]
internal sealed class UndoHistoryWindow : EditorWindow
{
    private static readonly Vector2 PopupSize = new(720, 440);
    private readonly TreeViewState<int> _treeState = new();
    private UndoHistoryTreeView? _tree;
    private IReadOnlyList<UndoHistoryEntry> _history = [];
    private int _cursor;
    private long _observedVersion = -1;
    private Vector2 _detailsScroll;

    public UndoHistoryWindow() => saveToLayout = false;

    internal static UndoHistoryWindow Open()
    {
        var window = CreateWindow<UndoHistoryWindow>();
        window.position = new Rect(240, 180, PopupSize.x, PopupSize.y);
        window.ShowPopup();
        return window;
    }

    [MenuItem("Window/Analysis/Undo History", false, 212)]
    private static void OpenFromMenu() => Open();

    internal static UndoHistoryWindow Open(Rect anchor)
    {
        var window = CreateWindow<UndoHistoryWindow>();
        window.ShowAsDropDown(anchor, PopupSize);
        return window;
    }

    protected override void OnEnable()
    {
        minSize = new Vector2(280, 220);
        titleContent = new GUIContent("Undo History", EditorBuiltinIcons.Toolbar.UndoHistory,
            "Review detailed Undo records and return to an earlier state");
        _tree ??= new UndoHistoryTreeView(this, _treeState);
        RefreshHistory(true);
    }

    protected override void Update() => RefreshHistory(false);

    protected override void OnGUI()
    {
        RefreshHistory(false);
        DrawToolbar();
        var top = EditorStyles.toolbar.fixedHeight + 3;
        var width = Fix64.Max(1, GUIUtility.currentViewWidth);
        var height = Fix64.Max(1, GUIUtility.currentViewHeight - top);
        if (width >= 580)
        {
            var treeWidth = Fix64.Clamp(width * Fix64.FromDecimal(0.55m), 280, 440);
            _tree!.OnGUI(new Rect(0, top, treeWidth, height));
            GUI.DrawRect(new Rect(treeWidth, top, 1, height),
                EditorStyles.separator.normal.backgroundColor);
            DrawDetails(new Rect(treeWidth + 1, top, Fix64.Max(1, width - treeWidth - 1), height));
            return;
        }
        var treeHeight = Fix64.Clamp(height * Fix64.FromDecimal(0.62m), 100,
            Fix64.Max(100, height - 76));
        _tree!.OnGUI(new Rect(0, top, width, treeHeight));
        GUI.DrawRect(new Rect(0, top + treeHeight, width, 1),
            EditorStyles.separator.normal.backgroundColor);
        DrawDetails(new Rect(0, top + treeHeight + 1, width,
            Fix64.Max(1, height - treeHeight - 1)));
    }

    private void DrawToolbar()
    {
        var height = EditorStyles.toolbar.fixedHeight;
        GUI.Box(new Rect(0, 0, GUIUtility.currentViewWidth, height + 2),
            GUIContent.none, EditorStyles.toolbar);
        var state = _cursor == _history.Count
            ? $"Current state  |  {_cursor:N0} applied"
            : $"History state {_cursor:N0}/{_history.Count:N0}";
        GUI.Label(new Rect(6, 1, Fix64.Max(1, GUIUtility.currentViewWidth - 66), height),
            state, EditorStyles.toolbarLabel);
        var old = GUI.enabled;
        GUI.enabled = _history.Count > 0;
        if (GUI.Button(new Rect(Fix64.Max(4, GUIUtility.currentViewWidth - 58), 1, 54, height),
                "Clear", EditorStyles.toolbarButton)) Undo.ClearAll();
        GUI.enabled = old;
    }

    private void DrawDetails(Rect area)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var selected = _tree?.GetSelection().FirstOrDefault() ?? _cursor;
        if (selected <= 0 || selected > _history.Count)
        {
            GUI.Label(new Rect(area.x + 8, area.y + 8, Fix64.Max(1, area.width - 16), 44),
                selected == 0 ? "Initial state\nNo Undo operations are applied." :
                "Select a history entry to view its details.", EditorStyles.wordWrappedMiniLabel);
            return;
        }
        var entry = _history[selected - 1];
        var contentHeight = Fix64.Max(area.height, 180 + (entry.TargetNames?.Length ?? 0) * 22);
        var viewport = new Rect(area.x + 1, area.y + 1,
            Fix64.Max(1, area.width - 2), Fix64.Max(1, area.height - 2));
        _detailsScroll = GUI.BeginScrollView(viewport, _detailsScroll,
            new Rect(0, 0, Fix64.Max(1, viewport.width - 11), contentHeight));
        try
        {
            var width = Fix64.Max(1, viewport.width - 27);
            var y = (Fix64)7;
            GUI.Label(new Rect(8, y, width, 24), entry.Name, EditorStyles.boldLabel);
            y += 27;
            DrawDetail(width, ref y, "History State", $"{selected:N0} of {_history.Count:N0}");
            DrawDetail(width, ref y, "Group", entry.Group.ToString("N0"));
            DrawDetail(width, ref y, "Operations", entry.OperationCount.ToString("N0"));
            DrawDetail(width, ref y, "Objects", entry.TargetCount.ToString("N0"));
            DrawDetail(width, ref y, "Scene Change", entry.AffectsScene ? "Yes" : "No");
            DrawDetail(width, ref y, "Direction", entry.IsRedo ? "Redo" : "Undo");
            if (entry.TargetNames is { Length: > 0 } targets)
            {
                y += 5;
                GUI.Label(new Rect(8, y, width, 22), "Affected Objects", EditorStyles.miniBoldLabel);
                y += 22;
                foreach (var target in targets)
                {
                    GUI.Label(new Rect(18, y, Fix64.Max(1, width - 10), 22), target,
                        EditorStyles.miniLabel);
                    y += 22;
                }
            }
        }
        finally { GUI.EndScrollView(); }
    }

    private static void DrawDetail(Fix64 width, ref Fix64 y, string label, string value)
    {
        var labelWidth = Fix64.Clamp(width * Fix64.FromDecimal(0.42m), 88, 145);
        GUI.Label(new Rect(8, y, labelWidth, 22), label, EditorStyles.miniLabel);
        GUI.Label(new Rect(8 + labelWidth, y, Fix64.Max(1, width - labelWidth), 22), value,
            EditorStyles.label);
        y += 22;
    }

    private void RefreshHistory(bool force)
    {
        if (!force && _observedVersion == Undo.historyVersion) return;
        _observedVersion = Undo.historyVersion;
        _history = Undo.GetHistory(out _cursor);
        _tree ??= new UndoHistoryTreeView(this, _treeState);
        _tree.Reload();
        _tree.SetSelection([_cursor], TreeViewSelectionOptions.RevealAndFrame);
        Repaint();
    }

    private void MoveToCursor(int cursor)
    {
        if (cursor == _cursor) return;
        Undo.MoveToHistoryCursor(cursor);
        RefreshHistory(true);
    }

    private sealed class UndoHistoryTreeView(UndoHistoryWindow owner, TreeViewState<int> state)
        : TreeView<int>(state)
    {
        protected override TreeViewItem<int> BuildRoot()
        {
            var root = new TreeViewItem<int>(int.MinValue, -1, "Root") { children = [] };
            root.AddChild(new TreeViewItem<int>(0, 0, "Initial State"));
            for (var index = 0; index < owner._history.Count; index++)
            {
                var entry = owner._history[index];
                var status = index < owner._cursor ? "Applied" : "Redo";
                root.AddChild(new TreeViewItem<int>(index + 1, 0,
                    $"{index + 1,4}  {entry.Name}   [{status}]"));
            }
            return root;
        }

        protected override bool CanMultiSelect(TreeViewItem<int> item) => false;

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectedIds.Count == 1) owner.MoveToCursor(selectedIds[0]);
        }
    }
}
