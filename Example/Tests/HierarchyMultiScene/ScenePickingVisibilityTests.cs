using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.TiledMap;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class ScenePickingVisibilityTests
{
    private const int SceneWidth = 640;
    private const int SceneHeight = 480;

    internal static void Run(SceneFixture fixture)
    {
        var visibility = SceneVisibilityManager.instance;
        visibility.ShowAll();
        visibility.EnableAllPicking();
        try
        {
            using var harness = new EditorApplicationHarness(fixture);
            harness.SetCameraPosition(System.Numerics.Vector2.Zero);
            harness.SetCameraSize(5);
            var bottom = CreateSprite(harness.InitialScene, "Scene Pick Bottom", 2);
            var top = CreateSprite(harness.InitialScene, "Scene Pick Top", 8);
            var child = harness.InitialScene.CreateGameObject("Scene Pick Child State");
            child.transform.SetParent(top.transform, false);
            harness.ExpandScene(harness.InitialScene);

            VerifyRenderOrderCycling(harness, top, bottom);
            VerifySceneSelectionRevealsHierarchy(harness, top);
            VerifyTiledMapPicking(harness, top, visibility);
            VerifyPickingAndVisibilityFilters(harness, top, bottom, visibility);
            VerifyHierarchyControls(harness, top, child, visibility);
            VerifyHiddenHandleAndHandleCapture(harness, top, bottom, visibility);
            VerifyPlayMirror(harness, top, visibility);
        }
        finally
        {
            visibility.ShowAll();
            visibility.EnableAllPicking();
        }
    }

    private static void VerifyTiledMapPicking(
        EditorApplicationHarness harness,
        GameObject top,
        SceneVisibilityManager visibility)
    {
        var owner = harness.InitialScene.CreateGameObject("Scene Pick Tilemap");
        try
        {
            var tilemap = owner.AddComponent<Tilemap>();
            tilemap.tileAnchor = Vector2.zero;
            var palette = new TilePalette();
            palette.SliceAtlas(1, 1);
            tilemap.SetPaletteOverride(palette);
            tilemap.SetTile(TileCoordinate.zero, 1);
            var renderer = owner.AddComponent<TilemapRenderer>();
            renderer.orderInLayer = 12;

            ClickScene(harness, SceneCenter());
            TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, owner),
                "The TiledMap editor picking provider did not select a visible tile above overlapping Sprites; " +
                $"selected '{harness.SelectedGameObject?.name ?? "<none>"}'.");
            ClickScene(harness, SceneCenter());
            TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, top),
                "Repeated Scene clicking did not cycle from a Tilemap to the next RenderSortKey2D object.");

            visibility.DisablePicking(owner, includeDescendants: false);
            ClickScene(harness, SceneCenter());
            TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, top),
                "The TiledMap picking provider ignored the Hierarchy picking-disabled state.");
            visibility.EnablePicking(owner, includeDescendants: false);
            visibility.Hide(owner, includeDescendants: false);
            ClickScene(harness, SceneCenter() + new Vector2(5, 0));
            TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, top),
                "The TiledMap picking provider ignored the Hierarchy Scene-hidden state.");
        }
        finally
        {
            visibility.Show(owner, includeDescendants: false);
            visibility.EnablePicking(owner, includeDescendants: false);
            harness.InitialScene.Destroy(owner);
        }
    }

    private static GameObject CreateSprite(Scene scene, string name, int order)
    {
        var gameObject = scene.CreateGameObject(name);
        var renderer = gameObject.AddComponent<SpriteRenderer>();
        renderer.size = new Vector2(2, 2);
        renderer.orderInLayer = order;
        return gameObject;
    }

    private static void VerifyRenderOrderCycling(
        EditorApplicationHarness harness,
        GameObject top,
        GameObject bottom)
    {
        ClickScene(harness, SceneCenter());
        TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, top),
            "The first Scene click did not select the highest RenderSortKey2D object.");
        ClickScene(harness, SceneCenter());
        TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, bottom),
            "A repeated Scene click did not cycle to the next overlapping GameObject.");
        ClickScene(harness, SceneCenter());
        TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, top),
            "Scene click cycling did not wrap back to the front-most GameObject.");
    }

    private static void VerifyPickingAndVisibilityFilters(
        EditorApplicationHarness harness,
        GameObject top,
        GameObject bottom,
        SceneVisibilityManager visibility)
    {
        visibility.DisablePicking(top, includeDescendants: false);
        ClickScene(harness, SceneCenter());
        TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, bottom),
            "A GameObject with Scene picking disabled was still selected.");

        visibility.Hide(bottom, includeDescendants: false);
        ClickScene(harness, SceneCenter());
        TestAssert.Require(harness.SelectedGameObject is null,
            "Clicking empty Scene space did not clear Selection.");
        TestAssert.Require(top.activeSelf && bottom.activeSelf && !harness.IsSceneDirty(harness.InitialScene),
            "Scene visibility/picking changed runtime active state or dirtied the Scene.");

        visibility.Show(bottom, includeDescendants: false);
        visibility.EnablePicking(top, includeDescendants: false);
        ClickScene(harness, SceneCenter());
        TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, top),
            "Restoring Scene visibility/picking did not restore normal picking.");
    }

    private static void VerifySceneSelectionRevealsHierarchy(
        EditorApplicationHarness harness,
        GameObject restoreSelection)
    {
        const int hierarchyWidth = 240;
        const int hierarchyHeight = 180;
        var created = new List<GameObject>();
        try
        {
            for (var index = 0; index < 36; index++)
                created.Add(harness.InitialScene.CreateGameObject($"Scene Pick Filler {index:D2}"));
            var parent = harness.InitialScene.CreateGameObject("Scene Pick Reveal Parent");
            created.Add(parent);
            var target = harness.InitialScene.CreateGameObject("Scene Pick Reveal Child");
            target.transform.SetParent(parent.transform, false);
            var renderer = target.AddComponent<SpriteRenderer>();
            renderer.size = new Vector2(2, 2);
            renderer.orderInLayer = 100;

            harness.RenderHierarchy(new Event(EventType.Layout), hierarchyWidth, hierarchyHeight);
            var collapsed = harness.RenderHierarchy(
                new Event(EventType.Repaint), hierarchyWidth, hierarchyHeight);
            TestAssert.Require(!collapsed.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                         command.Content == target.name),
                "The Scene-picking reveal fixture unexpectedly started expanded.");

            ClickScene(harness, SceneCenter());
            TestAssert.Require(ReferenceEquals(Selection.activeGameObject, target) &&
                               ReferenceEquals(harness.SelectedGameObject, target),
                "Scene picking did not synchronize the selected GameObject to editor Selection.");
            TestAssert.Require(harness.IsGameObjectExpanded(parent),
                "Scene picking did not expand the selected GameObject's Hierarchy ancestors.");

            harness.RenderHierarchy(new Event(EventType.Layout), hierarchyWidth, hierarchyHeight);
            var revealed = harness.RenderHierarchy(
                new Event(EventType.Repaint), hierarchyWidth, hierarchyHeight);
            var row = Text(revealed, target.name);
            TestAssert.Require(harness.HierarchyScrollPosition.y > 0 &&
                               row.Rect.Y >= row.ClipRect.Y - 0.5f &&
                               row.Rect.Bottom <= row.ClipRect.Bottom + 0.5f,
                "Scene picking did not scroll the selected GameObject into the visible Hierarchy viewport.");
            var inactiveSelection = GpuCanvasColor.FromColor(
                EditorStyles.treeViewRowSelected.disabled.backgroundColor);
            TestAssert.Require(revealed.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                       command.Color == inactiveSelection &&
                                                       command.Rect.Y <= row.Rect.Y + 0.5f &&
                                                       command.Rect.Bottom >= row.Rect.Bottom - 0.5f),
                "The Hierarchy did not render Scene-picked Selection on its corresponding row.");
        }
        finally
        {
            foreach (var gameObject in created)
                if (gameObject.scene is { } scene) scene.Destroy(gameObject);
            harness.SelectGameObject(restoreSelection);
        }
    }

    private static void VerifyHierarchyControls(
        EditorApplicationHarness harness,
        GameObject target,
        GameObject child,
        SceneVisibilityManager visibility)
    {
        visibility.Show(target, includeDescendants: false);
        visibility.EnablePicking(target, includeDescendants: false);
        harness.ExpandGameObject(target);
        harness.RenderHierarchy(new Event(EventType.Layout));
        var commands = harness.RenderHierarchy(new Event(EventType.Repaint));
        var row = Text(commands, target.name);
        var visible = RowImage(commands, row, "Visible.png");
        var childRow = Text(commands, child.name);
        var childVisible = RowImage(commands, childRow, "Visible.png");
        var unlocked = RowImage(commands, row, "Unlock.png");
        var childUnlocked = RowImage(commands, childRow, "Unlock.png");
        TestAssert.Require(MathF.Abs(visible.Rect.X - childVisible.Rect.X) < 0.1f &&
                           MathF.Abs(unlocked.Rect.X - childUnlocked.Rect.X) < 0.1f &&
                           visible.Rect.X < unlocked.Rect.X && unlocked.Rect.Right <= row.Rect.X,
            "Hierarchy Scene visibility/picking buttons are not fixed left columns independent of tree depth.");

        const int narrowWidth = 96;
        harness.RenderHierarchy(new Event(EventType.Layout), narrowWidth);
        var narrow = harness.RenderHierarchy(new Event(EventType.Repaint), narrowWidth);
        var narrowRow = Text(narrow, target.name);
        var narrowChildRow = Text(narrow, child.name);
        TestAssert.Require(MathF.Abs(RowImage(narrow, narrowRow, "Visible.png").Rect.X -
                                     RowImage(narrow, narrowChildRow, "Visible.png").Rect.X) < 0.1f &&
                           MathF.Abs(RowImage(narrow, narrowRow, "Unlock.png").Rect.X -
                                     RowImage(narrow, narrowChildRow, "Unlock.png").Rect.X) < 0.1f,
            "Narrow Hierarchy layout hid or indented its fixed Scene state columns.");

        harness.RenderHierarchy(new Event(EventType.Layout));
        commands = harness.RenderHierarchy(new Event(EventType.Repaint));
        row = Text(commands, target.name);
        visible = RowImage(commands, row, "Visible.png");
        ClickHierarchy(harness, Center(visible.Rect));
        TestAssert.Require(visibility.IsHidden(target) && visibility.IsHidden(child),
            "The Hierarchy eye button did not hide its GameObject and existing child hierarchy in Scene view.");

        commands = harness.RenderHierarchy(new Event(EventType.Repaint));
        row = Text(commands, target.name);
        unlocked = RowImage(commands, row, "Unlock.png");
        ClickHierarchy(harness, Center(unlocked.Rect));
        TestAssert.Require(visibility.IsPickingDisabled(target) && visibility.IsPickingDisabled(child),
            "The Hierarchy picking button did not disable picking for its GameObject and existing child hierarchy.");
        TestAssert.Require(target.activeSelf && !harness.IsSceneDirty(harness.InitialScene),
            "Hierarchy Scene flags changed GameObject state or Scene dirty state.");

        visibility.Show(target, includeDescendants: false);
        visibility.EnablePicking(target, includeDescendants: false);
        visibility.Show(child, includeDescendants: false);
        visibility.EnablePicking(child, includeDescendants: false);
    }

    private static void VerifyHiddenHandleAndHandleCapture(
        EditorApplicationHarness harness,
        GameObject top,
        GameObject bottom,
        SceneVisibilityManager visibility)
    {
        harness.SelectGameObject(top);
        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = KeyCode.W });
        visibility.Hide(top, includeDescendants: false);
        var hiddenCommands = harness.RenderScene(new Event(EventType.Repaint), SceneWidth, SceneHeight);
        TestAssert.Require(!HasAxisLabels(hiddenCommands),
            "A Scene-hidden selected GameObject still drew transform Handles.");

        visibility.Show(top, includeDescendants: false);
        var visibleCommands = harness.RenderScene(new Event(EventType.Repaint), SceneWidth, SceneHeight);
        TestAssert.Require(HasAxisLabels(visibleCommands),
            "Restoring Scene visibility did not restore transform Handles.");
        var down = new Event(EventType.MouseDown)
        {
            mousePosition = SceneCenter(),
            button = 0,
            clickCount = 1
        };
        harness.RenderScene(down, SceneWidth, SceneHeight);
        TestAssert.Require(down.type == EventType.Used && ReferenceEquals(harness.SelectedGameObject, top),
            "Clicking an active transform Handle penetrated through to the next Scene pick candidate.");
        harness.RenderScene(new Event(EventType.MouseUp)
        {
            mousePosition = SceneCenter(),
            button = 0
        }, SceneWidth, SceneHeight);
        TestAssert.Require(!ReferenceEquals(harness.SelectedGameObject, bottom),
            "A Handle click changed Scene Selection.");
    }

    private static void VerifyPlayMirror(
        EditorApplicationHarness harness,
        GameObject editObject,
        SceneVisibilityManager visibility)
    {
        harness.SelectGameObject(editObject);
        visibility.Hide(editObject, includeDescendants: false);
        harness.EnterPlay();
        var runtimeObject = harness.SelectedGameObject ??
                            throw new InvalidOperationException("Play Mode did not map Scene Selection.");
        TestAssert.Require(!ReferenceEquals(runtimeObject, editObject) && visibility.IsHidden(runtimeObject),
            "Scene visibility state was not mapped to the Play Mode mirror.");
        harness.ExitPlay();
        TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, editObject) &&
                           visibility.IsHidden(editObject) && editObject.activeSelf,
            "Leaving Play Mode did not restore the editor object and its editor-only Scene visibility state.");
        visibility.Show(editObject, includeDescendants: false);
    }

    private static void ClickScene(EditorApplicationHarness harness, Vector2 point)
    {
        var down = new Event(EventType.MouseDown)
        {
            mousePosition = point,
            button = 0,
            clickCount = 1
        };
        harness.RenderScene(down, SceneWidth, SceneHeight);
        TestAssert.Require(down.type == EventType.Used,
            "A Scene selection click was not consumed.");
        harness.RenderScene(new Event(EventType.MouseUp)
        {
            mousePosition = point,
            button = 0,
            clickCount = 1
        }, SceneWidth, SceneHeight);
    }

    private static void ClickHierarchy(EditorApplicationHarness harness, Vector2 point)
    {
        harness.RenderHierarchy(new Event(EventType.MouseDown)
        {
            mousePosition = point,
            button = 0,
            clickCount = 1
        });
        harness.RenderHierarchy(new Event(EventType.MouseUp)
        {
            mousePosition = point,
            button = 0,
            clickCount = 1
        });
    }

    private static Vector2 SceneCenter()
    {
        var toolbar = EditorStyles.toolbar.fixedHeight;
        return new Vector2(SceneWidth / 2, toolbar + (SceneHeight - toolbar) / 2);
    }

    private static bool HasAxisLabels(IEnumerable<GpuCanvasCommand> commands)
    {
        var labels = commands.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToHashSet(StringComparer.Ordinal);
        return labels.Contains("X") && labels.Contains("Y");
    }

    private static GpuCanvasCommand Text(IEnumerable<GpuCanvasCommand> commands, string content) =>
        commands.Single(command => command.Type == GpuCanvasCommandType.Text && command.Content == content);

    private static GpuCanvasCommand RowImage(
        IEnumerable<GpuCanvasCommand> commands,
        GpuCanvasCommand row,
        string suffix) => commands.Single(command =>
        command.Type == GpuCanvasCommandType.Image &&
        command.Content.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
        MathF.Abs(command.Rect.Y + command.Rect.Height / 2 -
                  (row.Rect.Y + row.Rect.Height / 2)) < 2);

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));
}
