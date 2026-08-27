using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using UnityEditor.IMGUI.Controls;

namespace BEngine.ExampleTests.IMGUIControlsTreeView;

internal static class Program
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static int Main()
    {
        try
        {
            VerifyPublicSurface();
            VerifyGenericTreeModel();
            VerifySelectionExpansionAndSearch();
            VerifyRowsAndCustomHeights();
            VerifyRenameAndDragHooks();
            VerifyIntCompatibilityWrappers();
            VerifyMultiColumnHeader();
            VerifySearchAndAdvancedDropdown();
            Console.WriteLine("IMGUI_CONTROLS_TREEVIEW_OK|public-surface,guid-generic,int-wrapper," +
                              "parents-depths,reload,rows,search,expand,selection-callback,selection-options," +
                              "row-gui,gpu-render,custom-height,rename,drag-drop,multicolumn,sorting,visibility," +
                              "search-field,advanced-dropdown");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"IMGUI_CONTROLS_TREEVIEW_FAILED|{exception}");
            return 1;
        }
        finally
        {
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
            DragAndDrop.PrepareStartDrag();
        }
    }

    private static void VerifyPublicSurface()
    {
        var assembly = typeof(TreeView<>).Assembly;
        Require(assembly.GetName().Name == "BEngine.Editor",
            "IMGUI Controls are not exported by BEngine.Editor.");
        foreach (var name in new[]
                 {
                     "TreeView`1", "TreeView", "TreeViewItem`1", "TreeViewItem", "TreeViewState`1",
                     "TreeViewState", "TreeViewSelectionOptions", "MultiColumnHeaderState",
                     "MultiColumnHeader", "SearchField", "AdvancedDropdownState", "AdvancedDropdownItem",
                     "AdvancedDropdown"
                 })
        {
            var type = assembly.GetType($"UnityEditor.IMGUI.Controls.{name}");
            Require(type is { IsPublic: true }, $"Public UnityEditor.IMGUI.Controls.{name} is missing.");
        }

        Require(typeof(TreeView).BaseType == typeof(TreeView<int>) &&
                typeof(TreeViewItem).BaseType == typeof(TreeViewItem<int>) &&
                typeof(TreeViewState).BaseType == typeof(TreeViewState<int>),
            "The non-generic TreeView compatibility API is not backed by the int generic API.");
        Require((TreeViewSelectionOptions.FireSelectionChanged | TreeViewSelectionOptions.RevealAndFrame)
                .HasFlag(TreeViewSelectionOptions.FireSelectionChanged),
            "TreeViewSelectionOptions is not a flags-compatible enum.");
    }

    private static void VerifyGenericTreeModel()
    {
        var state = new TreeViewState<Guid>();
        var tree = new GuidTree(state);
        tree.Reload();

        Require(tree.BuildCount == 1, "Reload did not invoke BuildRoot exactly once.");
        Require(tree.Root.depth == -1 && tree.Group.depth == 0 && tree.Alpha.depth == 1 &&
                ReferenceEquals(tree.Group.parent, tree.Root) &&
                ReferenceEquals(tree.Alpha.parent, tree.Group) &&
                tree.Root.children!.SequenceEqual([tree.Group, tree.Gamma]) &&
                tree.Group.children!.SequenceEqual([tree.Alpha, tree.Beta]),
            "TreeViewItem parent/child references or depth values were not established.");
        Require(tree.GetRows().Select(item => item.id).SequenceEqual([GuidTree.GroupId, GuidTree.GammaId]),
            "Initial rows did not contain the collapsed root-level items in hierarchy order.");

        tree.Reload();
        Require(tree.BuildCount == 2 && tree.GetRows().Count == 2,
            "Reload did not rebuild and replace the TreeView model deterministically.");
    }

    private static void VerifySelectionExpansionAndSearch()
    {
        var tree = new GuidTree(new TreeViewState<Guid>());
        tree.Reload();
        Require(tree.SetExpanded(GuidTree.GroupId, true) && tree.IsExpanded(GuidTree.GroupId),
            "SetExpanded did not expand a branch.");
        Require(tree.GetRows().Select(item => item.id).SequenceEqual(
                [GuidTree.GroupId, GuidTree.AlphaId, GuidTree.BetaId, GuidTree.GammaId]),
            "Expanded rows were not rebuilt in hierarchy order.");
        Require(tree.ExpandedChanges > 0 && tree.GetExpanded().Contains(GuidTree.GroupId),
            "Expanded state did not persist or notify the extension hook.");

        tree.CollapseAll();
        Require(!tree.IsExpanded(GuidTree.GroupId) && tree.GetRows().Count == 2,
            "CollapseAll did not hide descendants.");
        tree.ExpandAll();
        Require(tree.IsExpanded(GuidTree.GroupId) && tree.GetRows().Count == 4,
            "ExpandAll did not reveal descendants.");

        tree.SetSelection([GuidTree.AlphaId]);
        Require(tree.IsSelected(GuidTree.AlphaId) && tree.HasSelection() && tree.SelectionChanges == 0,
            "SetSelection(None) changed the wrong state or fired a callback.");
        tree.SetSelection([GuidTree.BetaId], TreeViewSelectionOptions.FireSelectionChanged |
                                            TreeViewSelectionOptions.RevealAndFrame);
        Require(tree.GetSelection().SequenceEqual([GuidTree.BetaId]) && tree.SelectionChanges == 1 &&
                tree.LastSelection.SequenceEqual([GuidTree.BetaId]),
            "Selection options did not update state and fire SelectionChanged exactly once.");
        tree.SelectAllRows();
        Require(tree.SelectionChanges == 2 && tree.GetSelection().Count == tree.GetRows().Count,
            "SelectAllRows did not select visible selectable rows and notify listeners.");

        tree.searchString = "beta";
        Require(tree.hasSearch && tree.SearchChanges == 1 && tree.LastSearch == "beta" &&
                tree.GetRows().Select(item => item.id).SequenceEqual([GuidTree.BetaId]),
            "TreeView search did not notify or filter the complete hierarchy case-insensitively.");
        tree.searchString = string.Empty;
        Require(!tree.hasSearch && tree.SearchChanges == 2 && tree.GetRows().Count == 4,
            "Clearing search did not restore expanded rows.");
    }

    private static void VerifyRowsAndCustomHeights()
    {
        var tree = new GuidTree(new TreeViewState<Guid>());
        tree.Reload();
        tree.ExpandAll();
        var commands = Render(() => tree.OnGUI(new Rect(0, 0, 360, 220)), 360, 220);

        Require(tree.BeforeRowsCount == 1 && tree.AfterRowsCount == 1 && tree.DrawnRows.Count == 4,
            "BeforeRowsGUI, RowGUI, or AfterRowsGUI was not invoked once per repaint.");
        Require(tree.DrawnRows.Single(row => row.Id == GuidTree.BetaId).Height == GuidTree.TallRowHeight,
            "GetCustomRowHeight was not reflected in RowGUIArgs.rowRect.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "Beta"),
            "Default RowGUI did not emit GPU text commands.");
        Require(tree.totalHeight >= tree.DrawnRows.Sum(row => row.Height),
            "TreeView.totalHeight does not include all custom row heights.");

        tree.SetSelection([GuidTree.BetaId]);
        tree.SetFocus();
        var focused = Render(() => tree.OnGUI(new Rect(0, 0, 360, 220)), 360, 220);
        Require(HasSolidRect(focused, EditorStyles.treeViewRowSelected.normal.backgroundColor),
            "A focused selected TreeView row did not draw the active selection background.");
        GUIUtility.keyboardControl = 0;
        var unfocused = Render(() => tree.OnGUI(new Rect(0, 0, 360, 220)), 360, 220);
        Require(HasSolidRect(unfocused, EditorStyles.treeViewRowSelected.disabled.backgroundColor),
            "An unfocused selected TreeView row lost its inactive selection background.");
    }

    private static void VerifyRenameAndDragHooks()
    {
        var tree = new GuidTree(new TreeViewState<Guid>());
        tree.Reload();
        tree.ExpandAll();
        Require(tree.BeginRename(tree.Beta), "BeginRename rejected an item allowed by CanRename.");
        Require(tree.CanRenameCalls > 0, "BeginRename did not consult CanRename.");
        tree.EndRename();

        tree.InvokeRenameEnded(GuidTree.BetaId, "Beta", "Renamed Beta", accepted: true);
        Require(tree.LastRename is { Accepted: true, Original: "Beta", Renamed: "Renamed Beta" },
            "RenameEnded did not receive the complete public extension arguments.");

        Require(tree.InvokeCanStartDrag(tree.Alpha, [GuidTree.AlphaId, GuidTree.BetaId]),
            "CanStartDrag override could not accept a valid drag.");
        tree.InvokeSetupDrag([GuidTree.AlphaId, GuidTree.BetaId]);
        Require(tree.SetupDraggedIds.SequenceEqual([GuidTree.AlphaId, GuidTree.BetaId]),
            "SetupDragAndDrop did not receive all dragged identifiers.");
        var mode = tree.InvokeDrop(tree.Group, insertAtIndex: 1, performDrop: true);
        Require(mode == DragAndDropVisualMode.Move && tree.LastDrop is
                { Position: "UponItem", InsertAtIndex: 1, PerformDrop: true },
            "HandleDragAndDrop did not expose target, insertion, perform, and visual-mode data.");
    }

    private static void VerifyIntCompatibilityWrappers()
    {
        var state = new TreeViewState();
        var tree = new IntTree(state);
        tree.Reload();
        tree.ExpandAll();
        tree.SetSelection([2], TreeViewSelectionOptions.FireSelectionChanged);
        Require(tree.GetSelection().SequenceEqual([2]) && tree.GetRows().Select(item => item.id)
                    .SequenceEqual([1, 2]) && state is TreeViewState<int>,
            "The non-generic int TreeView, TreeViewItem, or TreeViewState wrapper is not functional.");
    }

    private static void VerifyMultiColumnHeader()
    {
        var state = new MultiColumnHeaderState(
        [
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Name"), width = 170, minWidth = 80, maxWidth = 260,
                autoResize = true, allowToggleVisibility = false, canSort = true
            },
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Kind"), width = 100, minWidth = 60, maxWidth = 180,
                autoResize = true, allowToggleVisibility = true, canSort = true
            }
        ]);
        var header = new MultiColumnHeader(state) { canSort = true };
        var sortingChanges = 0;
        var visibilityChanges = 0;
        header.sortingChanged += _ => sortingChanges++;
        header.visibleColumnsChanged += _ => visibilityChanges++;

        header.SetSorting(1, ascending: false);
        Require(header.sortedColumnIndex == 1 && !header.IsSortedAscending(1) && sortingChanges == 1 &&
                state.sortedColumns.SequenceEqual([1]),
            "MultiColumnHeader sorting state or callback is incorrect.");
        Require(header.IsColumnVisible(1) && header.GetVisibleColumnIndex(1) == 1 &&
                header.GetColumnIndex(1) == 1,
            "MultiColumnHeader initial visible-column mapping is incorrect.");
        header.ToggleVisibility(1);
        Require(!header.IsColumnVisible(1) && visibilityChanges == 1 &&
                state.visibleColumns.SequenceEqual([0]),
            "MultiColumnHeader did not toggle a permitted column or publish visibility changes.");
        header.ToggleVisibility(1);
        Require(header.IsColumnVisible(1) && visibilityChanges == 2,
            "MultiColumnHeader did not restore a hidden column.");

        var nameCell = header.GetCellRect(0, new Rect(5, 50, 300, 24));
        var kindCell = header.GetCellRect(1, new Rect(5, 50, 300, 24));
        Require(nameCell.x == 5 && nameCell.width > kindCell.width && kindCell.x >= nameCell.xMax,
            "MultiColumnHeader cell layout ignored visible order or configured widths.");

        var tree = new GuidTree(new TreeViewState<Guid>(), header);
        tree.Reload();
        tree.ExpandAll();
        var commands = Render(() => tree.OnGUI(new Rect(0, 0, 330, 220)), 330, 220);
        Require(tree.VisibleColumnCounts.Count == 4 && tree.VisibleColumnCounts.All(count => count == 2) &&
                tree.FirstVisibleColumns.All(column => column == 0) &&
                tree.SecondCellWidths.All(width => width > 0),
            "TreeView RowGUIArgs did not expose visible multi-column indices and cell rectangles.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "Name") &&
                commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content.StartsWith("Kind", StringComparison.Ordinal)),
            "MultiColumnHeader.OnGUI did not render configured header content.");
    }

    private static void VerifySearchAndAdvancedDropdown()
    {
        var field = new SearchField();
        var search = string.Empty;
        var commands = Render(() => search = field.OnGUI(new Rect(8, 8, 210, 24), "atlas"), 240, 60);
        Require(search == "atlas" && commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                     command.Content == "atlas"),
            "SearchField could not be publicly constructed and drawn.");

        var toolbarCommands = Render(() => search = field.OnToolbarGUI(
            new Rect(8, 8, 210, 24), "sprite"), 240, 60);
        Require(search == "sprite" && toolbarCommands.Any(command =>
                    command.Type == GpuCanvasCommandType.Text && command.Content == "sprite"),
            "SearchField.OnToolbarGUI did not render through the toolbar search style.");

        var dropdownState = new AdvancedDropdownState();
        var dropdown = new TestDropdown(dropdownState);
        var root = dropdown.BuildForTest();
        Require(root.name == "Assets" && root.children.Select(item => item.name)
                    .SequenceEqual(["Texture", "Sprite"]),
            "AdvancedDropdown construction or hierarchical AdvancedDropdownItem children are broken.");
    }

    private static List<GpuCanvasCommand> Render(Action draw, int width, int height)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [new Event(EventType.Repaint), width, height, commands]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    private static bool HasSolidRect(IEnumerable<GpuCanvasCommand> commands, Color color) =>
        commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                command.Color == GpuCanvasColor.FromColor(color));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class GuidTree : TreeView<Guid>
    {
        public static readonly Guid RootId = Guid.Parse("10000000-0000-0000-0000-000000000000");
        public static readonly Guid GroupId = Guid.Parse("20000000-0000-0000-0000-000000000000");
        public static readonly Guid AlphaId = Guid.Parse("30000000-0000-0000-0000-000000000000");
        public static readonly Guid BetaId = Guid.Parse("40000000-0000-0000-0000-000000000000");
        public static readonly Guid GammaId = Guid.Parse("50000000-0000-0000-0000-000000000000");
        public const int TallRowHeight = 37;

        public GuidTree(TreeViewState<Guid> state) : base(state)
        {
            rowHeight = 22;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
        }

        public GuidTree(TreeViewState<Guid> state, MultiColumnHeader header) : base(state, header)
        {
            rowHeight = 22;
            columnIndexForTreeFoldouts = 0;
        }

        public int BuildCount { get; private set; }
        public int SelectionChanges { get; private set; }
        public int ExpandedChanges { get; private set; }
        public int SearchChanges { get; private set; }
        public int BeforeRowsCount { get; private set; }
        public int AfterRowsCount { get; private set; }
        public int CanRenameCalls { get; private set; }
        public string LastSearch { get; private set; } = string.Empty;
        public IList<Guid> LastSelection { get; private set; } = [];
        public List<(Guid Id, int Height)> DrawnRows { get; } = [];
        public List<int> VisibleColumnCounts { get; } = [];
        public List<int> FirstVisibleColumns { get; } = [];
        public List<int> SecondCellWidths { get; } = [];
        public IList<Guid> SetupDraggedIds { get; private set; } = [];
        public (bool Accepted, string Original, string Renamed)? LastRename { get; private set; }
        public (string Position, int InsertAtIndex, bool PerformDrop)? LastDrop { get; private set; }
        public TreeViewItem<Guid> Root => rootItem;
        public TreeViewItem<Guid> Group { get; private set; } = null!;
        public TreeViewItem<Guid> Alpha { get; private set; } = null!;
        public TreeViewItem<Guid> Beta { get; private set; } = null!;
        public TreeViewItem<Guid> Gamma { get; private set; } = null!;

        protected override TreeViewItem<Guid> BuildRoot()
        {
            BuildCount++;
            var root = new TreeViewItem<Guid>(RootId, -1, "Root");
            Group = new TreeViewItem<Guid>(GroupId, 0, "Group");
            Alpha = new TreeViewItem<Guid>(AlphaId, 1, "Alpha");
            Beta = new TreeViewItem<Guid>(BetaId, 1, "Beta");
            Gamma = new TreeViewItem<Guid>(GammaId, 0, "Gamma");
            SetupParentsAndChildrenFromDepths(root, [Group, Alpha, Beta, Gamma]);
            return root;
        }

        protected override void SelectionChanged(IList<Guid> selectedIds)
        {
            SelectionChanges++;
            LastSelection = selectedIds.ToArray();
        }

        protected override void ExpandedStateChanged() => ExpandedChanges++;

        protected override void SearchChanged(string newSearch)
        {
            SearchChanges++;
            LastSearch = newSearch;
        }

        protected override void BeforeRowsGUI()
        {
            BeforeRowsCount++;
            base.BeforeRowsGUI();
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            DrawnRows.Add((args.item.id, (int)args.rowRect.height));
            if (multiColumnHeader is not null)
            {
                VisibleColumnCounts.Add(args.GetNumVisibleColumns());
                FirstVisibleColumns.Add(args.GetColumn(0));
                SecondCellWidths.Add((int)args.GetCellRect(1).width);
            }
            base.RowGUI(args);
        }

        protected override void AfterRowsGUI() => AfterRowsCount++;

        protected override float GetCustomRowHeight(int row, TreeViewItem<Guid> item) =>
            item.id == BetaId ? TallRowHeight : 22;

        protected override bool CanRename(TreeViewItem<Guid> item)
        {
            CanRenameCalls++;
            return item.id != GroupId;
        }

        protected override void RenameEnded(RenameEndedArgs args) =>
            LastRename = (args.acceptedRename, args.originalName, args.newName);

        protected override bool CanStartDrag(CanStartDragArgs args) =>
            args.draggedItem.id != GroupId && args.draggedItemIDs.Count > 0;

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args) =>
            SetupDraggedIds = args.draggedItemIDs.ToArray();

        protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
        {
            LastDrop = (args.dragAndDropPosition.ToString(), args.insertAtIndex, args.performDrop);
            return DragAndDropVisualMode.Move;
        }

        public void InvokeRenameEnded(Guid id, string original, string renamed, bool accepted) =>
            RenameEnded(new RenameEndedArgs
            {
                acceptedRename = accepted, itemID = id, originalName = original, newName = renamed
            });

        public bool InvokeCanStartDrag(TreeViewItem<Guid> item, IList<Guid> ids) =>
            CanStartDrag(new CanStartDragArgs { draggedItem = item, draggedItemIDs = ids });

        public void InvokeSetupDrag(IList<Guid> ids) =>
            SetupDragAndDrop(new SetupDragAndDropArgs { draggedItemIDs = ids });

        public DragAndDropVisualMode InvokeDrop(TreeViewItem<Guid> parent, int insertAtIndex,
            bool performDrop) => HandleDragAndDrop(new DragAndDropArgs
        {
            dragAndDropPosition = DragAndDropPosition.UponItem,
            parentItem = parent,
            insertAtIndex = insertAtIndex,
            performDrop = performDrop
        });
    }

    private sealed class IntTree(TreeViewState state) : TreeView(state)
    {
        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem(0, -1, "Root");
            var parent = new TreeViewItem(1, 0, "Parent");
            var child = new TreeViewItem(2, 1, "Child");
            SetupParentsAndChildrenFromDepths(root, [parent, child]);
            return root;
        }
    }

    private sealed class TestDropdown(AdvancedDropdownState state) : AdvancedDropdown(state)
    {
        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Assets");
            root.AddChild(new AdvancedDropdownItem("Texture"));
            root.AddChild(new AdvancedDropdownItem("Sprite"));
            return root;
        }

        public AdvancedDropdownItem BuildForTest() => BuildRoot();
    }
}
