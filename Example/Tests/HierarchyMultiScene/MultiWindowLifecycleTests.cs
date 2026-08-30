using BEngine.Editor;
using BEngine.Editor.Documents;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class MultiWindowLifecycleTests
{
    public static void Run(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        var specifications = new[]
        {
            (Prototype: harness.InspectorWindow, Area: "Right"),
            (Prototype: harness.GameWindow, Area: "Center"),
            (Prototype: harness.SceneWindow, Area: "Center"),
            (Prototype: harness.HierarchyWindow, Area: "Left"),
            (Prototype: harness.ProjectWindow, Area: "Bottom")
        };
        var additional = new List<object>();
        var inspectorProbe = ScriptableObject.CreateInstance<MultiWindowInspectorProbe>();
        inspectorProbe.Value = 17;
        try
        {
            var windowMenu = harness.WindowMenuItems();
            TestAssert.Require(windowMenu.Count > 0 && windowMenu.All(item => !item.Checked),
                "The top-level Window menu still renders checked entries.");
            var addNewTabPaths = harness.AddNewTabMenuPaths();
            TestAssert.Require(addNewTabPaths.Contains("Add new tab/General/Inspector") &&
                               addNewTabPaths.Contains("Add new tab/Editor Status") &&
                               addNewTabPaths.All(path =>
                                   !path.StartsWith("Add new tab/Panels/", StringComparison.OrdinalIgnoreCase)),
                "Add new tab did not include built-in and registered extension windows, or included Window commands.");

            foreach (var specification in specifications)
            {
                TestAssert.Require(harness.SupportsMultipleBuiltIn(specification.Prototype),
                    $"{specification.Prototype.GetType().Name} is not registered as a multi-instance window.");
                var opened = harness.OpenAdditionalBuiltIn(specification.Prototype, specification.Area);
                additional.Add(opened);
                TestAssert.Require(opened.GetType() == specification.Prototype.GetType() &&
                                   !ReferenceEquals(opened, specification.Prototype) &&
                                   harness.WindowId(opened) != harness.WindowId(specification.Prototype) &&
                                   harness.IsWindowDocked(opened),
                    $"Opening another {specification.Prototype.GetType().Name} did not create a unique docked instance.");
            }
            TestAssert.Require(!harness.SupportsMultipleBuiltIn(harness.ConsoleWindow),
                "Console was unexpectedly registered as a multi-instance built-in window.");

            var inspector = additional.Single(window => window.GetType() == harness.InspectorWindow.GetType());
            harness.SetWindowLocked(harness.InspectorWindow, false);
            harness.SetWindowLocked(inspector, true);
            TestAssert.Require(!harness.IsWindowLocked(harness.InspectorWindow) &&
                               harness.IsWindowLocked(inspector),
                "Locking a secondary Inspector changed the primary Inspector lock state.");
            harness.SetWindowLocked(harness.InspectorWindow, true);
            harness.SetWindowLocked(inspector, false);
            TestAssert.Require(harness.IsWindowLocked(harness.InspectorWindow) &&
                               !harness.IsWindowLocked(inspector),
                "Unlocking a secondary Inspector changed the primary Inspector lock state.");

            var firstSelection = harness.ActiveScene.rootGameObjects.First();
            var secondSelection = harness.ActiveScene.gameObjects.First(gameObject =>
                !ReferenceEquals(gameObject, firstSelection));
            harness.SetWindowLocked(harness.InspectorWindow, false);
            harness.SelectGameObject(firstSelection);
            harness.RenderInspector(harness.InspectorWindow, new Event(EventType.Repaint));
            harness.RenderInspector(inspector, new Event(EventType.Repaint));
            harness.SetWindowLocked(inspector, true);
            harness.SelectGameObject(secondSelection);
            harness.RenderInspector(harness.InspectorWindow, new Event(EventType.Repaint));
            harness.RenderInspector(inspector, new Event(EventType.Repaint));
            TestAssert.Require(ReferenceEquals(harness.InspectorTarget, secondSelection) &&
                               ReferenceEquals(harness.InspectorTargetFor(inspector), firstSelection),
                "Selection changed the frozen target of a locked secondary Inspector.");
            harness.SetWindowLocked(inspector, false);
            TestAssert.Require(ReferenceEquals(harness.InspectorTargetFor(inspector), secondSelection),
                "Unlocking a secondary Inspector did not immediately refresh it from current Selection.");

            harness.LockInspector(inspector, inspectorProbe);
            harness.EnterPlay();
            var runtimeInspectorTarget = harness.InspectorTargetFor(inspector) as MultiWindowInspectorProbe;
            TestAssert.Require(runtimeInspectorTarget is not null &&
                               !ReferenceEquals(runtimeInspectorTarget, inspectorProbe) &&
                               runtimeInspectorTarget.Value == 17,
                "The secondary locked Inspector did not switch to an isolated Play Mode clone.");
            runtimeInspectorTarget!.Value = 99;
            harness.ExitPlay();
            TestAssert.Require(ReferenceEquals(harness.InspectorTargetFor(inspector), inspectorProbe) &&
                               inspectorProbe.Value == 17,
                "Stopping Play did not restore the secondary Inspector's original unmodified target.");

            var activeLayout = harness.CaptureLayout();
            var activeIds = activeLayout.Windows.Select(record => record.Id).ToArray();
            TestAssert.Require(activeIds.Distinct(StringComparer.Ordinal).Count() == activeIds.Length,
                "Capturing multiple EditorWindows produced duplicate persistent IDs.");
            foreach (var opened in additional)
                TestAssert.Require(activeLayout.Windows.Any(record => record.Id == harness.WindowId(opened)),
                    $"The layout omitted additional window '{harness.WindowId(opened)}'.");

            var project = additional.Single(window => window.GetType() == harness.ProjectWindow.GetType());
            harness.PrimeProjectCache(harness.ProjectWindow);
            harness.PrimeProjectCache(project);
            harness.InvalidateProjectWindows();
            TestAssert.Require(harness.IsProjectCacheInvalidated(harness.ProjectWindow) &&
                               harness.IsProjectCacheInvalidated(project),
                "Project refresh did not invalidate every open Project window instance.");

            var scene = additional.Single(window => window.GetType() == harness.SceneWindow.GetType());
            var sceneId = harness.WindowId(scene);
            var sceneDockIndex = harness.DockIndex(scene);
            TestAssert.Require(harness.ToggleMaximize(scene) && harness.IsMaximized(scene),
                "The secondary Scene window could not enter its saved maximized state.");
            harness.LayoutSaved = true;
            harness.CloseWindow(scene);
            TestAssert.Require(!harness.IsWindowOpen(scene) && !harness.IsWindowDocked(scene) &&
                               !harness.LayoutSaved,
                "Closing a secondary Scene window did not close it and mark the layout for saving.");

            var closedLayout = harness.CaptureLayout();
            var closedRecord = closedLayout.ClosedWindows.SingleOrDefault(record => record.Id == sceneId);
            TestAssert.Require(closedRecord is { Docked: true, WasMaximized: true } &&
                               closedRecord.PanelIndex == sceneDockIndex &&
                               closedLayout.Windows.All(record => record.Id != sceneId),
                "The closed Scene window did not retain its Dock index and maximized state outside the active layout.");

            harness.LayoutSaved = true;
            var restored = harness.OpenAdditionalBuiltIn(harness.SceneWindow, "Center");
            TestAssert.Require(ReferenceEquals(restored, scene) && harness.WindowId(restored) == sceneId &&
                               harness.IsWindowOpen(restored) && harness.IsWindowDocked(restored) &&
                               harness.DockIndex(restored) == sceneDockIndex && harness.IsMaximized(restored) &&
                               !harness.LayoutSaved,
                "Reopening Scene did not restore the exact instance, Dock position, and maximize state.");

            var restoredLayout = harness.CaptureLayout();
            TestAssert.Require(restoredLayout.Windows.Any(record => record.Id == sceneId) &&
                               restoredLayout.ClosedWindows.All(record => record.Id != sceneId),
                "Reopening a closed window left a stale closed-window layout record.");

            harness.CloseWindow(restored);
            var persistedLayout = harness.CaptureLayout();
            harness.ApplyLayout(persistedLayout);
            var restoredAfterLayoutLoad = harness.OpenAdditionalBuiltIn(harness.SceneWindow, "Center");
            additional.Add(restoredAfterLayoutLoad);
            TestAssert.Require(!ReferenceEquals(restoredAfterLayoutLoad, scene) &&
                               harness.WindowId(restoredAfterLayoutLoad) == sceneId &&
                               harness.IsWindowDocked(restoredAfterLayoutLoad) &&
                               harness.DockIndex(restoredAfterLayoutLoad) == sceneDockIndex &&
                               harness.IsMaximized(restoredAfterLayoutLoad),
                "Loading a saved layout did not reconstruct the closed Scene window placement.");
        }
        finally
        {
            foreach (var window in additional.Distinct(ReferenceEqualityComparer.Instance))
                if (harness.IsWindowDocked(window)) harness.CloseWindow(window);
            BObject.DestroyImmediate(inspectorProbe);
        }
    }
}

internal sealed class MultiWindowInspectorProbe : ScriptableObject
{
    public int Value { get; set; }
}
