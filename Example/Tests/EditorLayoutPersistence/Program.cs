using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.EditorLayoutPersistence;

internal static class Program
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngine-EditorLayout-{Guid.NewGuid():N}");
        try
        {
            VerifyProjectPackagesSplitterLayout();
            VerifyGroupedUndoHistory();
            VerifyUndoHistoryDropdown();
            VerifyLayoutRoundTrip(root);
            Console.WriteLine(
                "EDITOR_LAYOUT_PERSISTENCE_OK|yaml-v2,named,last-session,dock-tree,closed-window-placement,selection,lock-context,project-packages-splitter,project-packages-height,undo-group-collapse,undo-history-jump,toolbar-layout-menu,layout-rename,case-only-layout-rename,builtin-layout-protection");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_LAYOUT_PERSISTENCE_FAILED|{exception}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void VerifyUndoHistoryDropdown()
    {
        Undo.ClearAll();
        var target = new GameObject("Before");
        try
        {
            Undo.RecordObject(target, "First Rename");
            target.name = "First";
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(target, "Second Rename");
            target.name = "Second";

            var history = Undo.GetHistory(out var cursor);
            Require(cursor == 2 && history.Select(static item => item.Name)
                    .SequenceEqual(["First Rename", "Second Rename"]) &&
                    history.All(static item => !item.IsRedo),
                "Undo history did not expose two chronological operation groups at the current cursor.");
            var undoMenu = EditorToolbarDropdowns.CreateUndoHistoryMenu();
            Require(undoMenu.Items.Any(item => item.Path == "Undo/Second Rename" && item.Enabled) &&
                    undoMenu.Items.Any(item => item.Path == "Undo/First Rename" && item.Enabled) &&
                    undoMenu.Items.Any(item => item.Path == "Redo/No Redo History" && !item.Enabled),
                "The toolbar Undo history menu did not expose the expected undo and redo branches.");

            undoMenu.Items.Single(item => item.Path == "Undo/First Rename").Action!.Invoke();
            history = Undo.GetHistory(out cursor);
            Require(target.name == "Before" && cursor == 0 && history.All(static item => item.IsRedo),
                "Selecting an older Undo history entry did not jump back through all newer groups.");

            var redoMenu = EditorToolbarDropdowns.CreateUndoHistoryMenu();
            redoMenu.Items.Single(item => item.Path == "Redo/Second Rename").Action!.Invoke();
            Undo.GetHistory(out cursor);
            Require(target.name == "Second" && cursor == 2,
                "Selecting a later Redo history entry did not replay every preceding group.");
        }
        finally
        {
            Undo.ClearAll();
        }
    }

    private static void VerifyGroupedUndoHistory()
    {
        Undo.ClearAll();
        var target = new GameObject("Before");
        try
        {
            Undo.RecordObject(target, "Grouped Rename");
            target.name = "First";
            Undo.RecordObject(target, "Grouped Rename");
            target.name = "Second";

            var history = Undo.GetHistory(out var cursor);
            Require(cursor == 1 && history is [{ Name: "Grouped Rename", IsRedo: false }],
                "Multiple records in one Undo group were exposed as separate history entries.");
            Require(Undo.MoveToHistoryCursor(0) && target.name == "Before",
                "Jumping before a grouped history entry did not restore every record in the group.");
            history = Undo.GetHistory(out cursor);
            Require(cursor == 0 && history is [{ Name: "Grouped Rename", IsRedo: true }] &&
                    Undo.MoveToHistoryCursor(1) && target.name == "Second",
                "Replaying a grouped history entry did not restore every record in chronological order.");
        }
        finally
        {
            Undo.ClearAll();
        }
    }

    private static void VerifyProjectPackagesSplitterLayout()
    {
        var bounds = new Rect(0, 0, 280, 300);
        var layout = ProjectBrowserSplitLayoutUtility.Calculate(bounds, 90);
        Require(layout.Assets.height == bounds.height - 90 - ProjectBrowserSplitLayoutUtility.SeparatorHeight &&
                layout.Separator.height == ProjectBrowserSplitLayoutUtility.SeparatorHeight &&
                layout.Packages.height == 90 && layout.Separator.y == layout.Assets.yMax &&
                layout.Packages.y == layout.Separator.yMax,
            "The Project Packages splitter did not divide the available height without a gap or overlap.");
        Require(layout.SeparatorHitArea.height == ProjectBrowserSplitLayoutUtility.SeparatorHitHeight &&
                layout.SeparatorHitArea.Contains(new Vector2(140, layout.Separator.y)),
            "The Project Packages splitter has no practical pointer hit area around its one-pixel line.");

        var enlarged = ProjectBrowserSplitLayoutUtility.ResizePackagesHeight(90, -55, bounds.height);
        var reduced = ProjectBrowserSplitLayoutUtility.ResizePackagesHeight(90, 30, bounds.height);
        Require(enlarged == 145 && reduced == 60,
            "Dragging the Project separator did not resize the lower Packages pane in the expected direction.");
        Require(ProjectBrowserSplitLayoutUtility.ResizePackagesHeight(90, 1000, bounds.height) ==
                ProjectBrowserSplitLayoutUtility.MinimumPaneHeight &&
                ProjectBrowserSplitLayoutUtility.ResizePackagesHeight(90, -1000, bounds.height) ==
                bounds.height - ProjectBrowserSplitLayoutUtility.SeparatorHeight -
                ProjectBrowserSplitLayoutUtility.MinimumPaneHeight,
            "Project splitter dragging escaped the minimum Assets or Packages pane height.");

        var compact = ProjectBrowserSplitLayoutUtility.Calculate(new Rect(0, 0, 180, 70), enlarged);
        Require(compact.Assets.height >= 0 && compact.Packages.height >= 0 &&
                compact.Assets.height + compact.Separator.height + compact.Packages.height == 70,
            "Shrinking the Project window produced an invalid Packages split layout.");
        var initial = ProjectBrowserSplitLayoutUtility.FromAssetContentHeight(new Rect(0, 0, 280, 500), 180);
        Require(initial == 500 - ProjectBrowserSplitLayoutUtility.SeparatorHeight - 180,
            "The default Project Packages divider no longer starts immediately after visible Assets content.");
    }

    private static void VerifyLayoutRoundTrip(string root)
    {
        Directory.CreateDirectory(root);
        YamlUtility.Save(new ProjectData { Name = "Layout Test" },
            Path.Combine(root, ProjectWorkspace.ProjectFileName));
        var workspace = ProjectWorkspace.Open(root);
        var store = new EditorLayoutStore(workspace);
        var hierarchy = new LayoutProbeWindow("Hierarchy") { PersistentId = "Hierarchy" };
        var scene = new LayoutProbeWindow("Scene") { PersistentId = "Scene" };
        hierarchy.OpenInternal();
        scene.OpenInternal();

        var dock = new ImGuiDockWorkspace();
        dock.Add("Hierarchy", hierarchy, DockArea.Left, true);
        dock.Add("Scene", scene, DockArea.Center, true);
        Render(dock);
        hierarchy.isLocked = true;
        hierarchy.LockContext = "object:ca761232ed4211cebacd00aa0057b223";
        var document = new EditorLayoutDocument
        {
            Name = "Editing",
            ActiveLayout = "Editing",
            ProjectBrowserMode = "TwoColumn",
            ProjectFoldersWidth = 312,
            ProjectPackagesHeight = 168,
            ProjectThumbnailSize = 104,
            SceneCameraPositionX = 3.25f,
            SceneCameraPositionY = -1.5f,
            SceneCameraRotation = 17.5f,
            SceneCameraSize = 12.5f,
            SceneCameraBackgroundR = 0.1f,
            SceneCameraBackgroundG = 0.2f,
            SceneCameraBackgroundB = 0.3f,
            SceneCameraDrawGrid = false,
            DockRoot = dock.CaptureLayout(),
            MaximizedPanelId = dock.MaximizedPanelId,
            Windows =
            [
                WindowRecord(hierarchy),
                WindowRecord(scene)
            ],
            ClosedWindows =
            [
                new EditorWindowLayoutDocument
                {
                    Id = "Scene:closed-secondary",
                    TypeName = scene.GetType().AssemblyQualifiedName!,
                    State = nameof(EditorWindowState.Normal),
                    Docked = true,
                    Locked = true,
                    LockContext = "object:closed-secondary",
                    X = 220,
                    Y = 140,
                    Width = 640,
                    Height = 360,
                    PreferredDockArea = nameof(DockArea.Center),
                    PreviousPanelId = "Scene",
                    PanelIndex = 1,
                    DockX = 720,
                    DockY = 360,
                    WasMaximized = true
                }
            ]
        };

        var savedName = store.Save("Editing", document);
        store.SaveLastSession(document);
        Require(savedName == "Editing" && store.Names.SequenceEqual(["Editing"]),
            "Named layout was not listed after saving.");
        var loaded = store.Load("Editing");
        Require(loaded.Version == 2 && loaded.DockRoot is not null && loaded.Windows.Count == 2 &&
                loaded.ClosedWindows.Count == 1,
            "The v2 layout YAML did not preserve its window and dock records.");
        var closedScene = loaded.ClosedWindows.Single();
        Require(closedScene.Id == "Scene:closed-secondary" && closedScene.Docked &&
                closedScene.Locked && closedScene.LockContext == "object:closed-secondary" &&
                closedScene.X == 220 && closedScene.Y == 140 &&
                closedScene.Width == 640 && closedScene.Height == 360 &&
                closedScene.PreferredDockArea == nameof(DockArea.Center) &&
                closedScene.PreviousPanelId == "Scene" && closedScene.PanelIndex == 1 &&
                closedScene.DockX == 720 && closedScene.DockY == 360 && closedScene.WasMaximized,
            "The layout YAML did not preserve a closed window's bounds, Dock placement, lock, and maximize state.");
        var loadedHierarchy = loaded.Windows.Single(window => window.Id == "Hierarchy");
        Require(loadedHierarchy.Locked && loadedHierarchy.LockContext == hierarchy.LockContext,
            "The layout YAML did not preserve a locked EditorWindow context.");
        hierarchy.isLocked = false;
        hierarchy.LockContext = null;
        hierarchy.isLocked = loadedHierarchy.Locked;
        hierarchy.RestoreLockContext(loadedHierarchy.LockContext);
        Require(hierarchy.isLocked && hierarchy.LockContext == loadedHierarchy.LockContext,
            "Restoring a layout did not restore the EditorWindow lock state and context together.");
        Require(loaded.ProjectBrowserMode == "TwoColumn" && loaded.ProjectFoldersWidth == 312 &&
                loaded.ProjectPackagesHeight == 168 && loaded.ProjectThumbnailSize == 104,
            "The layout YAML did not preserve Project browser mode, folder width, Packages height, and preview scale.");
        Require(MathF.Abs(loaded.SceneCameraPositionX - 3.25f) < 0.001f &&
                MathF.Abs(loaded.SceneCameraPositionY + 1.5f) < 0.001f &&
                MathF.Abs(loaded.SceneCameraRotation - 17.5f) < 0.001f &&
                MathF.Abs(loaded.SceneCameraSize - 12.5f) < 0.001f &&
                MathF.Abs(loaded.SceneCameraBackgroundR - 0.1f) < 0.001f &&
                MathF.Abs(loaded.SceneCameraBackgroundG - 0.2f) < 0.001f &&
                MathF.Abs(loaded.SceneCameraBackgroundB - 0.3f) < 0.001f &&
                !loaded.SceneCameraDrawGrid,
            "The layout YAML did not preserve the 2D Scene camera state.");
        Require(store.LoadLastSession().ActiveLayout == "Editing",
            "The last-session layout did not remember the active named layout.");

        var menuActions = new List<string>();
        var layoutMenu = EditorToolbarDropdowns.CreateLayoutMenu(
            "Editing", store.HasLastSession, store.Names,
            () => menuActions.Add("save"),
            () => menuActions.Add("save-as"),
            () => menuActions.Add("last-session"),
            name => menuActions.Add("switch:" + name),
            name => menuActions.Add("rename:" + name),
            name => menuActions.Add("delete:" + name));
        Require(layoutMenu.Items.Any(item => item.Path == "Save Current" && item.Enabled) &&
                layoutMenu.Items.Any(item => item.Path == "Save As..." && item.Enabled) &&
                layoutMenu.Items.Any(item => item.Path == "Switch/Last Session" && item.Enabled) &&
                layoutMenu.Items.Any(item => item.Path == "Switch/Editing" && item.On) &&
                layoutMenu.Items.Any(item => item.Path == "Rename/Editing" && item.Enabled) &&
                layoutMenu.Items.Any(item => item.Path == "Delete/Editing" && item.Enabled),
            "The toolbar Layout menu did not expose save, switch, rename, and delete actions.");
        layoutMenu.Items.Single(item => item.Path == "Rename/Editing").Action!.Invoke();
        Require(menuActions.SequenceEqual(["rename:Editing"]),
            "The toolbar Layout menu dispatched the wrong named-layout action.");

        var restored = new ImGuiDockWorkspace();
        restored.Add("Hierarchy", hierarchy, DockArea.Left, true);
        restored.Add("Scene", scene, DockArea.Center, true);
        var panels = restored.RestoreLayout(loaded.DockRoot,
            new Dictionary<string, EditorWindow> { ["Hierarchy"] = hierarchy, ["Scene"] = scene });
        Render(restored);
        Require(panels.Count == 2 && restored.IsSelected(scene),
            "Restoring the dock tree lost a window or selected tab.");
        var renamed = store.Rename("Editing", "Review/Layout");
        var renamedDocument = store.Load(renamed);
        Require(renamed == "Review_Layout" && store.Names.SequenceEqual([renamed]) &&
                renamedDocument.Name == renamed && renamedDocument.ActiveLayout == renamed,
            "Renaming a named layout did not update its file and serialized identity together.");
        var caseRenamed = store.Rename(renamed, "review_layout");
        var caseRenamedDocument = store.Load(caseRenamed);
        Require(caseRenamed == "review_layout" && store.Names.SequenceEqual([caseRenamed]) &&
                caseRenamedDocument.Name == caseRenamed && caseRenamedDocument.ActiveLayout == caseRenamed,
            "Changing only a layout name's casing did not update its file and serialized identity together.");
        Require(!store.Delete(EditorLayoutStore.LastSessionName),
            "The built-in Last Session layout could be deleted.");
        RequireThrows<InvalidOperationException>(() =>
                store.Rename(EditorLayoutStore.LastSessionName, "Renamed"),
            "The built-in Last Session layout could be renamed.");
        RequireThrows<InvalidOperationException>(() =>
                store.Save(EditorLayoutStore.LastSessionName, new EditorLayoutDocument()),
            "A named layout overwrote the built-in Last Session identity.");
        Require(store.Delete(caseRenamed) && store.Names.Count == 0,
            "Deleting a renamed layout left it in the layout list.");
        hierarchy.CloseInternal();
        scene.CloseInternal();
    }

    private static EditorWindowLayoutDocument WindowRecord(EditorWindow window) => new()
    {
        Id = window.PersistentId,
        TypeName = window.GetType().AssemblyQualifiedName!,
        State = nameof(EditorWindowState.Normal),
        Docked = true,
        Locked = window.isLocked,
        LockContext = window.CaptureLockContext(),
        Width = 480,
        Height = 320
    };

    private static void Render(ImGuiDockWorkspace dock)
    {
        GUI.BeginFrame(new Event(EventType.Layout), 1200, 800, []);
        try { dock.OnGUI(new Rect(0, 0, 1200, 800)); }
        finally { GUI.EndFrame(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }
}
