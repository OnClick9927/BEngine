using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class WindowLockingTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public |
                                                         BindingFlags.NonPublic;

    internal static void Run(SceneFixture fixture)
    {
        VerifyLockedHierarchyCommands(fixture);
        VerifyBuiltInWindowLockContexts(fixture);
    }

    private static void VerifyLockedHierarchyCommands(SceneFixture fixture)
    {
        Undo.ClearAll();
        using var harness = new EditorApplicationHarness(fixture);
        var scene = harness.InitialScene;
        var locked = scene.CreateGameObject("Locked Command Target");
        var global = scene.CreateGameObject("Global Command Target");
        locked.transform.position = new Vector2(19, 7);
        global.transform.position = new Vector2(-23, -11);

        Prepare(harness, locked, global);
        GUIUtility.systemCopyBuffer = string.Empty;
        Execute("Edit/Copy");
        TestAssert.Require(GUIUtility.systemCopyBuffer == locked.name,
            "Copy used the global selection instead of the locked Hierarchy target.");

        Prepare(harness, locked, global);
        Execute("Edit/Rename");
        TestAssert.Require(harness.HierarchyRenamingId == locked.Id,
            "Rename used the global selection instead of the locked Hierarchy target.");

        Prepare(harness, locked, global);
        Execute("Edit/Frame Selected");
        var framed = harness.CameraPosition;
        TestAssert.Require(MathF.Abs(framed.X - 19) < 0.001f && MathF.Abs(framed.Y - 7) < 0.001f,
            "Frame Selected framed the global selection instead of the locked Hierarchy target.");

        Prepare(harness, locked, global);
        Execute("Edit/Duplicate");
        var duplicate = scene.gameObjects.FirstOrDefault(item => item.name == "Locked Command Target (1)");
        TestAssert.Require(duplicate is not null,
            "Duplicate copied the global selection instead of the locked Hierarchy target.");
        Undo.PerformUndo();

        var deleted = scene.CreateGameObject("Locked Delete Target");
        Prepare(harness, deleted, global);
        Execute("Edit/Delete");
        TestAssert.Require(scene.Find(deleted.Id) is null && scene.Find(global.Id) is not null,
            "Delete removed the global selection instead of the locked Hierarchy target.");
        Undo.ClearAll();
    }

    private static void VerifyBuiltInWindowLockContexts(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        var first = harness.InitialScene.Find("First Root") ??
                    throw new InvalidOperationException("The lock context fixture root is missing.");
        var second = harness.InitialScene.gameObjects.First(item => !ReferenceEquals(item, first));

        Prepare(harness, first, second);
        var hierarchyContext = CaptureContext(harness.HierarchyWindow);
        SetField(harness.HierarchyWindow, "_lockedSelectionId", second.Id);
        RestoreContext(harness.HierarchyWindow, hierarchyContext);
        TestAssert.Require((Guid?)GetField(harness.HierarchyWindow, "_lockedSelectionId") == first.Id,
            "Hierarchy did not restore its locked GameObject id.");

        harness.LockInspector(first);
        var inspectorContext = CaptureContext(harness.InspectorWindow);
        SetField(harness.InspectorWindow, "_lastTarget", second);
        RestoreContext(harness.InspectorWindow, inspectorContext);
        TestAssert.Require(ReferenceEquals(harness.InspectorTarget, first),
            "Inspector did not restore its locked object target.");
        RestoreContext(harness.InspectorWindow, $"object:{Guid.NewGuid():N}");
        TestAssert.Require(harness.InspectorTarget is null,
            "Inspector silently locked to another selection when the persisted target was missing.");

        const string projectPath = "Assets/Scenes/First.scene.yaml";
        SetField(harness.ProjectWindow, "_selectedPath", projectPath);
        ((EditorWindow)harness.ProjectWindow).isLocked = true;
        var projectContext = CaptureContext(harness.ProjectWindow);
        SetField(harness.ProjectWindow, "_selectedPath", "Assets");
        RestoreContext(harness.ProjectWindow, projectContext);
        TestAssert.Require((string?)GetField(harness.ProjectWindow, "_selectedPath") == projectPath,
            "Project did not restore its locked path.");
        RestoreContext(harness.ProjectWindow, "Assets/Missing/No.asset");
        TestAssert.Require((string?)GetField(harness.ProjectWindow, "_selectedPath") == "Assets",
            "Project did not fall back to an existing parent when its persisted path was missing.");
    }

    private static void Prepare(EditorApplicationHarness harness, GameObject locked, GameObject global)
    {
        var hierarchy = (EditorWindow)harness.HierarchyWindow;
        if (hierarchy.isLocked) hierarchy.isLocked = false;
        Selection.activeGameObject = locked;
        harness.FocusHierarchy();
        hierarchy.isLocked = true;
        Selection.activeGameObject = global;
        harness.FocusHierarchy();
    }

    private static string? CaptureContext(object window) =>
        (string?)typeof(EditorWindow).GetMethod("CaptureLockContext", InstanceMembers)!.Invoke(window, null);

    private static void RestoreContext(object window, string? context) =>
        typeof(EditorWindow).GetMethod("RestoreLockContext", InstanceMembers)!.Invoke(window, [context]);

    private static object? GetField(object target, string name) =>
        target.GetType().GetField(name, InstanceMembers)?.GetValue(target) ??
        throw new MissingFieldException(target.GetType().FullName, name);

    private static void SetField(object target, string name, object? value) =>
        (target.GetType().GetField(name, InstanceMembers) ??
         throw new MissingFieldException(target.GetType().FullName, name)).SetValue(target, value);

    private static void Execute(string path) => TestAssert.Require(EditorApplication.ExecuteMenuItem(path),
        $"EditorApplication.ExecuteMenuItem did not execute '{path}'.");
}
