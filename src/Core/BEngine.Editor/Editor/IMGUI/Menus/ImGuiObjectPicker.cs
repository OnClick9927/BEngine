using UnityEditor.IMGUI.Controls;
using ImGuiTreeView = UnityEditor.IMGUI.Controls.TreeView;

namespace BEngine.Editor;

/// <summary>Dedicated searchable TreeView picker used by ObjectField select buttons.</summary>
internal sealed class ImGuiObjectPicker
{
    private const string SearchControlName = "BEngine.ObjectPicker.Search";
    private const int ControlScope = 0x4F42504B;
    private readonly PickerTreeView _assetsTree;
    private readonly PickerTreeView _sceneTree;
    private EditorObjectPickerRequest? _request;
    private PickerTab _activeTab = PickerTab.Assets;
    private Vector2 _position;
    private Rect? _anchor;
    private string _search = string.Empty;
    private int _searchControlId;
    private bool _focusSearch;
    private bool _pointerEntered;
    private bool _isOpen;

    internal ImGuiObjectPicker()
    {
        _assetsTree = new PickerTreeView(PickerTab.Assets, SelectValue);
        _sceneTree = new PickerTreeView(PickerTab.Scene, SelectValue);
    }

    internal bool isOpen => _isOpen;
    internal string activeTab => _activeTab.ToString();
    internal string search => _search;
    internal string currentPath => string.Empty;
    internal IReadOnlyList<string> visiblePaths => ActiveTree.VisiblePaths;
    internal IReadOnlyList<string> tabs => ["Assets", "Scene"];

    internal void Open(EditorObjectPickerRequest request, Vector2 position, Rect? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        Close();
        _request = request;
        _assetsTree.SetContents(request, request.ProjectCandidates, removeAssetsRoot: true);
        _sceneTree.SetContents(request, request.SceneCandidates, removeAssetsRoot: false);
        _activeTab = PickerTab.Assets;
        _position = position;
        _anchor = anchor;
        _search = string.Empty;
        _focusSearch = true;
        _pointerEntered = false;
        _isOpen = true;
    }

    internal void Close()
    {
        var hadInputState = _isOpen || _searchControlId != 0;
        _isOpen = false;
        _request = null;
        _assetsTree.ClearContents();
        _sceneTree.ClearContents();
        _activeTab = PickerTab.Assets;
        _anchor = null;
        _search = string.Empty;
        _focusSearch = false;
        _pointerEntered = false;
        GUI.ClearTextState(_searchControlId);
        _searchControlId = 0;
        if (hadInputState) GUI.FocusControl(string.Empty);
    }

    internal bool Draw()
    {
        if (!_isOpen || _request is null) return false;
        GUIUtility.BeginContainer(ControlScope);
        try { return DrawContents(); }
        finally { GUIUtility.EndContainer(); }
    }

    private bool DrawContents()
    {
        var titleHeight = Fix64.Max(25, EditorGUIUtility.singleLineHeight + 7);
        var searchHeight = Fix64.Max(24, EditorStyles.toolbarSearchField.fixedHeight + 6);
        var tabsHeight = Fix64.Max(23, EditorStyles.toolbarToggle.fixedHeight + 2);
        var menuRect = Place(CalculateWidth(), CalculateHeight(titleHeight + searchHeight + tabsHeight));
        var titleRect = new Rect(menuRect.x + 1, menuRect.y + 1,
            Fix64.Max(1, menuRect.width - 2), titleHeight);
        var searchRect = new Rect(menuRect.x + 5, titleRect.yMax + 4,
            Fix64.Max(1, menuRect.width - 10), searchHeight - 3);
        var tabsRect = new Rect(menuRect.x + 3, searchRect.yMax + 3,
            Fix64.Max(1, menuRect.width - 6), tabsHeight);
        var treeRect = new Rect(menuRect.x + 3, tabsRect.yMax + 1,
            Fix64.Max(1, menuRect.width - 6), Fix64.Max(1, menuRect.yMax - tabsRect.yMax - 4));

        var evt = Event.current;
        var eventType = evt.type;
        var pointer = evt.mousePosition;
        var exitRect = new Rect(menuRect.x - 4, menuRect.y - 4, menuRect.width + 8, menuRect.height + 8);
        if (menuRect.Contains(pointer)) _pointerEntered = true;
        if (eventType == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
        {
            evt.Use();
            Close();
            return false;
        }
        if (eventType is EventType.MouseDown or EventType.ContextClick &&
            !menuRect.Contains(pointer) && !(_anchor?.Contains(pointer) ?? false))
        {
            evt.Use();
            Close();
            return false;
        }
        if (eventType == EventType.MouseLeaveWindow ||
            _pointerEntered && eventType == EventType.MouseMove && !exitRect.Contains(pointer))
        {
            Close();
            return false;
        }

        DrawSurface(menuRect);
        DrawTitle(titleRect);
        DrawSearch(searchRect);
        DrawTabs(tabsRect);
        ActiveTree.OnGUI(treeRect);

        if (ImGuiPopupMenu.IsInputEvent(eventType) && evt.type != EventType.Used) evt.Use();
        return _isOpen;
    }

    private void DrawTitle(Rect rect)
    {
        if (Event.current.type == EventType.Repaint)
            GUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1),
                EditorStyles.separator.normal.backgroundColor);
        GUI.Label(new Rect(rect.x + 8, rect.y, Fix64.Max(1, rect.width - 42), rect.height),
            $"Select {ObjectNames.NicifyVariableName(_request!.ObjectType.Name)}", EditorStyles.boldLabel);
        if (GUI.Button(new Rect(rect.xMax - 29, rect.y + 2, 25, Fix64.Max(18, rect.height - 4)),
                new GUIContent("x", tooltip: "Close"), EditorStyles.toolbarButton))
            Close();
    }

    private void DrawSearch(Rect rect)
    {
        GUI.SetNextControlName(SearchControlName);
        if (_focusSearch)
        {
            GUI.FocusControl(SearchControlName);
            _focusSearch = false;
        }
        var next = GUI.TextField(rect, _search, style: EditorStyles.toolbarSearchField);
        if (_searchControlId == 0 && GUIUtility.textFieldInput &&
            GUI.GetNameOfFocusedControl().Equals(SearchControlName, StringComparison.Ordinal))
            _searchControlId = GUIUtility.keyboardControl;
        if (!next.Equals(_search, StringComparison.Ordinal))
        {
            _search = next;
            Refilter();
        }
        if (_search.Length == 0)
            GUI.Label(new Rect(rect.x + 8, rect.y, Fix64.Max(0, rect.width - 16), rect.height),
                $"Search {ObjectNames.NicifyVariableName(_request!.ObjectType.Name)}...", EditorStyles.miniLabel);
    }

    private void DrawTabs(Rect rect)
    {
        var half = rect.width / 2;
        DrawTab(new Rect(rect.x, rect.y, half, rect.height), PickerTab.Assets,
            $"Assets ({_request!.ProjectCandidates.Count})", true);
        DrawTab(new Rect(rect.x + half, rect.y, rect.width - half, rect.height), PickerTab.Scene,
            $"Scene ({_request.SceneCandidates.Count})", _request.AllowSceneObjects);
    }

    private void DrawTab(Rect rect, PickerTab tab, string label, bool enabled)
    {
        var previous = GUI.enabled;
        GUI.enabled = previous && enabled;
        var selected = _activeTab == tab;
        if (GUI.Toggle(rect, selected, new GUIContent(label), EditorStyles.toolbarToggle) && !selected)
            SwitchTab(tab);
        GUI.enabled = previous;
    }

    private void SwitchTab(PickerTab tab)
    {
        if (tab == PickerTab.Scene && _request is { AllowSceneObjects: false }) return;
        if (_activeTab == tab) return;
        _activeTab = tab;
        ActiveTree.searchString = _search;
        ActiveTree.SetFocus();
    }

    private void Refilter()
    {
        _assetsTree.searchString = _search;
        _sceneTree.searchString = _search;
    }

    private void SelectValue(BObject? value)
    {
        if (_request is not { } request) return;
        var select = request.Select;
        Close();
        EditorFeatureGuard.Invoke("ObjectField.Select", () => select(value));
    }

    private PickerTreeView ActiveTree => _activeTab == PickerTab.Scene ? _sceneTree : _assetsTree;

    private Rect Place(Fix64 width, Fix64 height)
    {
        var x = Fix64.Clamp(_position.x, 2, Fix64.Max(2, GUIUtility.currentViewWidth - width - 2));
        var maximumY = Fix64.Max(2, GUIUtility.currentViewHeight - height - 2);
        var y = _position.y;
        if (_anchor is { } anchor && y + height > GUIUtility.currentViewHeight - 2 && anchor.y - height >= 2)
            y = anchor.y - height;
        return new Rect(x, Fix64.Clamp(y, 2, maximumY), width, height);
    }

    private static Fix64 CalculateWidth()
    {
        var available = Fix64.Max(80, GUIUtility.currentViewWidth - 4);
        var desired = Fix64.Max(360, GUIUtility.currentViewWidth - 24);
        return Fix64.Min(available, Fix64.Min(640, desired));
    }

    private static Fix64 CalculateHeight(Fix64 chromeHeight)
    {
        var available = Fix64.Max(80, GUIUtility.currentViewHeight - 4);
        var desired = Fix64.Max(chromeHeight + 96, GUIUtility.currentViewHeight - 24);
        return Fix64.Min(available, Fix64.Min(440, desired));
    }

    private static void DrawSurface(Rect rect)
    {
        if (Event.current.type != EventType.Repaint) return;
        var style = EditorStyles.dropDownList;
        GUI.DrawRect(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height),
            style.disabled.backgroundColor);
        GUI.Box(rect, GUIContent.none, style);
    }

    private enum PickerTab
    {
        Assets,
        Scene,
        // Compatibility alias for older reflection-based editor extensions.
        Project = Assets
    }

    private sealed class PickerTreeView : ImGuiTreeView
    {
        private readonly PickerTab _tab;
        private readonly Action<BObject?> _select;
        private readonly Dictionary<int, PickerTreeItem> _items = [];
        private readonly List<PickerTreeItem> _searchableItems = [];
        private EditorObjectPickerRequest? _request;
        private IReadOnlyList<EditorObjectPickerCandidate> _candidates = [];
        private bool _removeAssetsRoot;
        private int _nextId;

        internal PickerTreeView(PickerTab tab, Action<BObject?> select) : base(new TreeViewState())
        {
            _tab = tab;
            _select = select;
            rowHeight = 22;
            showBorder = true;
            showAlternatingRowBackgrounds = false;
            enableItemHovering = true;
            depthIndentWidth = 16;
        }

        internal IReadOnlyList<string> VisiblePaths => GetRows()
            .OfType<PickerTreeItem>()
            .Select(static item => item.FullPath)
            .ToArray();

        internal void SetContents(EditorObjectPickerRequest request,
            IReadOnlyList<EditorObjectPickerCandidate> candidates, bool removeAssetsRoot)
        {
            _request = request;
            _candidates = candidates;
            _removeAssetsRoot = removeAssetsRoot;
            state.expandedIDs.Clear();
            state.selectedIDs.Clear();
            state.scrollPos = Vector2.zero;
            state.searchString = string.Empty;
            Reload();
            var current = _items.Values.FirstOrDefault(item => item.Kind == PickerItemKind.Candidate &&
                item.Candidate is { } candidate && EditorObjectPicker.SameObject(request.Current, candidate.Value));
            if (current is not null)
                SetSelection([current.id], TreeViewSelectionOptions.RevealAndFrame);
        }

        internal void ClearContents()
        {
            _request = null;
            _candidates = [];
            _items.Clear();
            _searchableItems.Clear();
            state.expandedIDs.Clear();
            state.selectedIDs.Clear();
            state.scrollPos = Vector2.zero;
            state.searchString = string.Empty;
        }

        protected override TreeViewItem BuildRoot()
        {
            _items.Clear();
            _searchableItems.Clear();
            _nextId = 0;
            var root = new PickerTreeItem(NextId(), -1, "Root", string.Empty,
                PickerItemKind.Group, null);
            if (_request is null) return root;

            root.AddChild(Register(new PickerTreeItem(NextId(), 0, "None", "None",
                PickerItemKind.None, null)));
            if (_request.SelectedObject is { } selected)
            {
                var selectedItem = Register(new PickerTreeItem(NextId(), 0,
                    $"Use Selected: {selected.name}", "Use Selected", PickerItemKind.Selected, null));
                selectedItem.icon = EditorObjectPicker.Content(selected, _request.ObjectType, mixed: false).image;
                root.AddChild(selectedItem);
            }

            var hierarchyRoot = new HierarchyNode(string.Empty, string.Empty);
            foreach (var candidate in _candidates)
            {
                var segments = SplitCandidatePath(candidate.Path, _removeAssetsRoot);
                if (segments.Length == 0) continue;
                var node = hierarchyRoot;
                for (var index = 0; index < segments.Length; index++)
                {
                    var segment = segments[index];
                    var fullPath = node.FullPath.Length == 0 ? segment : $"{node.FullPath}/{segment}";
                    node = node.Children.GetValueOrDefault(segment) ??
                           node.AddChild(new HierarchyNode(segment, fullPath));
                }
                node.Candidate = candidate;
            }

            foreach (var child in hierarchyRoot.Children.Values
                         .OrderBy(static node => node.Name, StringComparer.OrdinalIgnoreCase))
                root.AddChild(BuildItem(child, 0));
            return root;
        }

        protected override IList<TreeViewItem<int>> BuildRows(TreeViewItem<int> root)
        {
            if (!hasSearch) return base.BuildRows(root);
            var terms = searchString.Split(' ', StringSplitOptions.RemoveEmptyEntries |
                                                StringSplitOptions.TrimEntries);
            var rows = new List<TreeViewItem<int>>();
            foreach (var special in root.children?.OfType<PickerTreeItem>() ?? [])
                if (special.Kind is PickerItemKind.None or PickerItemKind.Selected)
                    rows.Add(special);
            rows.AddRange(_searchableItems
                .Where(item => terms.All(term => item.FullPath.Contains(term,
                    StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static item => item.FullPath, StringComparer.OrdinalIgnoreCase));
            return rows;
        }

        protected override bool CanMultiSelect(TreeViewItem<int> item) => false;

        protected override void SingleClickedItem(int id)
        {
            if (_items.TryGetValue(id, out var item) && item.Kind != PickerItemKind.Group)
                Activate(item);
        }

        protected override void DoubleClickedItem(int id)
        {
            if (!_items.TryGetValue(id, out var item)) return;
            if (item.Kind == PickerItemKind.Group)
            {
                SetExpanded(id, !IsExpanded(id));
                return;
            }
            Activate(item);
        }

        private PickerTreeItem BuildItem(HierarchyNode node, int depth)
        {
            var kind = node.Candidate is not null && node.Children.Count == 0
                ? PickerItemKind.Candidate
                : PickerItemKind.Group;
            var item = Register(new PickerTreeItem(NextId(), depth, node.Name, node.FullPath,
                kind, node.Candidate));
            item.icon = kind == PickerItemKind.Group
                ? _tab == PickerTab.Assets
                    ? EditorBuiltinIcons.Assets.FolderClosed
                    : EditorBuiltinIcons.Components.GameObject
                : EditorObjectPicker.Content(node.Candidate!.Value, _request!.ObjectType, mixed: false).image;
            if (kind == PickerItemKind.Candidate) _searchableItems.Add(item);
            foreach (var child in node.Children.Values
                         .OrderBy(static value => value.Name, StringComparer.OrdinalIgnoreCase))
                item.AddChild(BuildItem(child, depth + 1));
            if (node.Candidate is not null && kind == PickerItemKind.Group)
            {
                var candidateItem = Register(new PickerTreeItem(NextId(), depth + 1,
                    node.Name, node.FullPath, PickerItemKind.Candidate, node.Candidate));
                candidateItem.icon = EditorObjectPicker.Content(node.Candidate.Value,
                    _request!.ObjectType, mixed: false).image;
                item.AddChild(candidateItem);
                _searchableItems.Add(candidateItem);
            }
            return item;
        }

        private PickerTreeItem Register(PickerTreeItem item)
        {
            _items[item.id] = item;
            return item;
        }

        private void Activate(PickerTreeItem item)
        {
            var value = item.Kind switch
            {
                PickerItemKind.None => null,
                PickerItemKind.Selected => _request?.SelectedObject,
                PickerItemKind.Candidate => item.Candidate?.Value,
                _ => null
            };
            _select(value);
        }

        private int NextId() => ++_nextId;

        private static string[] SplitCandidatePath(string path, bool removeAssetsRoot)
        {
            var segments = (path ?? string.Empty).Replace('\\', '/').Split('/',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return removeAssetsRoot && segments.Length > 0 &&
                   segments[0].Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? segments[1..]
                : segments;
        }

        private sealed class PickerTreeItem(int id, int depth, string displayName, string fullPath,
            PickerItemKind kind, EditorObjectPickerCandidate? candidate)
            : TreeViewItem(id, depth, displayName)
        {
            internal string FullPath { get; } = fullPath;
            internal PickerItemKind Kind { get; } = kind;
            internal EditorObjectPickerCandidate? Candidate { get; } = candidate;
        }

        private sealed class HierarchyNode(string name, string fullPath)
        {
            internal string Name { get; } = name;
            internal string FullPath { get; } = fullPath;
            internal Dictionary<string, HierarchyNode> Children { get; } =
                new(StringComparer.OrdinalIgnoreCase);
            internal EditorObjectPickerCandidate? Candidate { get; set; }

            internal HierarchyNode AddChild(HierarchyNode child)
            {
                Children[child.Name] = child;
                return child;
            }
        }

        private enum PickerItemKind { None, Selected, Group, Candidate }
    }
}
