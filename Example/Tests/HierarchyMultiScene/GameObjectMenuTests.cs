using System.Collections;
using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class GameObjectMenuTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public |
                                                         BindingFlags.NonPublic;

    private static readonly string[] RequiredPaths =
    [
        "Create Empty",
        "Create Empty Child",
        "Create Empty Parent",
        "2D Object/Sprite",
        "Effects/Particle System 2D",
        "Camera 2D",
        "Set Active",
        "Set Inactive",
        "Transform/Reset",
        "Transform/Reset Position",
        "Transform/Reset Rotation",
        "Transform/Reset Scale",
        "Hierarchy/Select Parent",
        "Hierarchy/Select Children",
        "Hierarchy/Move To Root",
        "Hierarchy/Move Up",
        "Hierarchy/Move Down",
        "Hierarchy/Set As First Sibling",
        "Hierarchy/Set As Last Sibling",
        "Hierarchy/Center On Children",
        "Frame Selected",
        "Copy Hierarchy Path",
        "Rename",
        "Duplicate",
        "Delete"
    ];

    public static void Run(SceneFixture fixture)
    {
        VerifySharedMenusAndValidation(fixture);
        VerifyCreationAndObjectCommands(fixture);
        VerifyHierarchyCommands(fixture);
    }

    private static void VerifySharedMenusAndValidation(SceneFixture fixture)
    {
        Undo.ClearAll();
        using var harness = new EditorApplicationHarness(fixture);
        var root = RequireObject(harness.InitialScene, "First Root");
        var child = RequireObject(harness.InitialScene, "First Child");

        Selection.activeGameObject = child;
        var main = CaptureMainMenu(harness);
        var context = CaptureContextMenu(harness, child);
        foreach (var path in RequiredPaths)
        {
            var mainItem = RequireItem(main, path, "GameObject main menu");
            var contextItem = RequireItem(context, path, "Hierarchy context menu");
            TestAssert.Require(mainItem.Enabled == contextItem.Enabled,
                $"'{path}' has different enabled states in GameObject and Hierarchy menus.");
            TestAssert.Require(!mainItem.Enabled || mainItem.Action is not null,
                $"Enabled GameObject command '{path}' has no action.");
            TestAssert.Require(!contextItem.Enabled || contextItem.Action is not null,
                $"Enabled Hierarchy command '{path}' has no action.");
        }
        TestAssert.Require(main.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() == main.Count,
            "GameObject main menu contains duplicate command paths.");
        TestAssert.Require(context.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() == context.Count,
            "Hierarchy GameObject context menu contains duplicate command paths.");

        Selection.activeGameObject = null;
        var emptySelection = CaptureMainMenu(harness);
        foreach (var path in new[]
                 {
                     "Create Empty", "2D Object/Sprite", "Effects/Particle System 2D", "Camera 2D"
                 })
            TestAssert.Require(RequireItem(emptySelection, path, "empty-selection GameObject menu").Enabled,
                $"'{path}' should be available without a selection.");
        foreach (var path in RequiredPaths.Except(new[]
                 {
                     "Create Empty", "2D Object/Sprite", "Effects/Particle System 2D", "Camera 2D"
                 }, StringComparer.Ordinal))
            TestAssert.Require(!RequireItem(emptySelection, path, "empty-selection GameObject menu").Enabled,
                $"Selection command '{path}' is enabled without a selected GameObject.");

        Selection.activeGameObject = child;
        var childSelection = CaptureMainMenu(harness);
        foreach (var path in new[]
                 {
                     "Create Empty Child", "Create Empty Parent", "Set Inactive", "Transform/Reset",
                     "Hierarchy/Select Parent", "Hierarchy/Move To Root", "Copy Hierarchy Path", "Rename",
                     "Duplicate", "Delete"
                 })
            TestAssert.Require(RequireItem(childSelection, path, "child-selection GameObject menu").Enabled,
                $"'{path}' should be enabled for a selected child GameObject.");
        TestAssert.Require(!RequireItem(childSelection, "Set Active", "child-selection GameObject menu").Enabled,
            "Set Active should be disabled for an already active GameObject.");
        TestAssert.Require(!RequireItem(childSelection, "Hierarchy/Select Children",
                "child-selection GameObject menu").Enabled,
            "Select Children should be disabled when the selected GameObject has no children.");

        Selection.activeGameObject = root;
        var rootSelection = CaptureMainMenu(harness);
        TestAssert.Require(!RequireItem(rootSelection, "Hierarchy/Select Parent", "root GameObject menu").Enabled,
            "Select Parent should be disabled for a Scene root.");
        TestAssert.Require(!RequireItem(rootSelection, "Hierarchy/Move To Root", "root GameObject menu").Enabled,
            "Move To Root should be disabled for a Scene root.");
        TestAssert.Require(RequireItem(rootSelection, "Hierarchy/Select Children", "root GameObject menu").Enabled,
            "Select Children should be enabled for a GameObject with children.");
    }

    private static void VerifyCreationAndObjectCommands(SceneFixture fixture)
    {
        Undo.ClearAll();
        using var harness = new EditorApplicationHarness(fixture);
        var scene = harness.InitialScene;
        var root = RequireObject(scene, "First Root");
        var originalChild = RequireObject(scene, "First Child");

        Selection.activeGameObject = null;
        var rootsBefore = scene.rootGameObjects.Count();
        Execute(harness, "Create Empty");
        var empty = harness.SelectedGameObject ??
                    throw new InvalidOperationException("Create Empty did not select its new GameObject.");
        TestAssert.Require(empty.name == "GameObject" && empty.transform.parent is null &&
                           scene.rootGameObjects.Count() == rootsBefore + 1,
            "Create Empty did not create and select a Scene root GameObject.");
        Undo.PerformUndo();
        TestAssert.Require(!scene.gameObjects.Contains(empty), "Undo did not remove Create Empty's GameObject.");

        Selection.activeGameObject = root;
        Execute(harness, "Create Empty Child");
        var child = harness.SelectedGameObject ??
                    throw new InvalidOperationException("Create Empty Child did not select its new GameObject.");
        TestAssert.Require(ReferenceEquals(child.transform.parent, root.transform),
            "Create Empty Child did not parent the created GameObject to the selection.");
        Undo.PerformUndo();

        Selection.activeGameObject = originalChild;
        var originalParent = originalChild.transform.parent;
        Execute(harness, "Create Empty Parent");
        var createdParent = harness.SelectedGameObject ??
                            throw new InvalidOperationException("Create Empty Parent did not select its new parent.");
        TestAssert.Require(ReferenceEquals(originalChild.transform.parent, createdParent.transform) &&
                           ReferenceEquals(createdParent.transform.parent, originalParent),
            "Create Empty Parent did not insert a parent above the selected GameObject.");
        Undo.PerformUndo();
        TestAssert.Require(ReferenceEquals(originalChild.transform.parent, originalParent),
            "Undo did not restore the hierarchy changed by Create Empty Parent.");

        VerifyComponentCreation(harness, "2D Object/Sprite", "Sprite", gameObject =>
            gameObject.GetComponent<SpriteRenderer>() is not null);
        VerifyComponentCreation(harness, "Effects/Particle System 2D", "Particle System 2D", gameObject =>
            gameObject.GetComponent<ParticleSystem2D>() is not null);
        VerifyComponentCreation(harness, "Camera 2D", "Camera 2D", gameObject =>
            gameObject.GetComponent<Camera2D>() is not null);

        Selection.activeGameObject = originalChild;
        Execute(harness, "Set Inactive");
        TestAssert.Require(!originalChild.activeSelf, "Set Inactive did not disable the selected GameObject.");
        var inactiveMenu = CaptureMainMenu(harness);
        TestAssert.Require(RequireItem(inactiveMenu, "Set Active", "inactive GameObject menu").Enabled &&
                           !RequireItem(inactiveMenu, "Set Inactive", "inactive GameObject menu").Enabled,
            "Set Active and Set Inactive validators do not reflect activeSelf.");
        Execute(harness, "Set Active");
        TestAssert.Require(originalChild.activeSelf, "Set Active did not enable the selected GameObject.");

        originalChild.transform.localPosition = new Vector2(4, 5);
        originalChild.transform.localRotation = 30;
        originalChild.transform.localScale = new Vector2(2, 3);
        Execute(harness, "Transform/Reset");
        TestAssert.Require(originalChild.transform.localPosition == Vector2.zero &&
                           originalChild.transform.localRotation == Fix64.Zero &&
                           originalChild.transform.localScale == Vector2.one,
            "Transform/Reset did not restore position, rotation and scale defaults.");

        GUIUtility.systemCopyBuffer = string.Empty;
        Execute(harness, "Copy Hierarchy Path");
        TestAssert.Require(GUIUtility.systemCopyBuffer.Replace('\\', '/').EndsWith(
                "First Root/First Child", StringComparison.Ordinal),
            $"Copy Hierarchy Path copied '{GUIUtility.systemCopyBuffer}'.");

        Execute(harness, "Rename");
        TestAssert.Require(harness.HierarchyRenamingId == originalChild.Id,
            "GameObject/Rename did not begin inline Hierarchy rename.");

        var childCount = root.transform.childCount;
        Execute(harness, "Duplicate");
        var duplicate = harness.SelectedGameObject ??
                        throw new InvalidOperationException("Duplicate did not select its copy.");
        TestAssert.Require(!ReferenceEquals(duplicate, originalChild) &&
                           ReferenceEquals(duplicate.transform.parent, root.transform) &&
                           root.transform.childCount == childCount + 1,
            "Duplicate did not copy the selected GameObject hierarchy beside its source.");
        Undo.PerformUndo();

        Selection.activeGameObject = originalChild;
        Execute(harness, "Delete");
        TestAssert.Require(!scene.gameObjects.Contains(originalChild),
            "Delete did not remove the selected GameObject hierarchy.");
        Undo.PerformUndo();
        TestAssert.Require(scene.gameObjects.Contains(originalChild),
            "Undo did not restore a GameObject deleted from the GameObject menu.");
    }

    private static void VerifyHierarchyCommands(SceneFixture fixture)
    {
        Undo.ClearAll();
        using var harness = new EditorApplicationHarness(fixture);
        var scene = harness.InitialScene;
        var parent = scene.CreateGameObject("Menu Parent");
        var first = scene.CreateGameObject("Menu First");
        var middle = scene.CreateGameObject("Menu Middle");
        var last = scene.CreateGameObject("Menu Last");
        first.transform.SetParent(parent.transform, false);
        middle.transform.SetParent(parent.transform, false);
        last.transform.SetParent(parent.transform, false);

        Selection.activeGameObject = middle;
        Execute(harness, "Hierarchy/Select Parent");
        TestAssert.Require(ReferenceEquals(harness.SelectedGameObject, parent),
            "Select Parent did not select the parent GameObject.");
        Execute(harness, "Hierarchy/Select Children");
        TestAssert.Require(Selection.gameObjects.Length == 3 &&
                           Selection.gameObjects.Contains(first) &&
                           Selection.gameObjects.Contains(middle) &&
                           Selection.gameObjects.Contains(last),
            "Select Children did not select all direct child GameObjects.");

        Selection.activeGameObject = middle;
        Execute(harness, "Hierarchy/Move Up");
        TestAssert.Require(middle.transform.GetSiblingIndex() == 0,
            "Move Up did not move the selected GameObject by one sibling.");
        Undo.PerformUndo();
        TestAssert.Require(middle.transform.GetSiblingIndex() == 1,
            "Undo did not restore the sibling index changed by Move Up.");

        Execute(harness, "Hierarchy/Move Down");
        TestAssert.Require(middle.transform.GetSiblingIndex() == 2,
            "Move Down did not move the selected GameObject by one sibling.");
        Undo.PerformUndo();

        Execute(harness, "Hierarchy/Set As First Sibling");
        TestAssert.Require(middle.transform.GetSiblingIndex() == 0,
            "Set As First Sibling did not move the selection to index zero.");
        Undo.PerformUndo();
        Execute(harness, "Hierarchy/Set As Last Sibling");
        TestAssert.Require(middle.transform.GetSiblingIndex() == parent.transform.childCount - 1,
            "Set As Last Sibling did not move the selection to the final index.");
        Undo.PerformUndo();

        Execute(harness, "Hierarchy/Move To Root");
        TestAssert.Require(middle.transform.parent is null && ReferenceEquals(middle.scene, scene),
            "Move To Root did not detach the selected GameObject into its Scene root.");
        Undo.PerformUndo();
        TestAssert.Require(ReferenceEquals(middle.transform.parent, parent.transform),
            "Undo did not restore the parent changed by Move To Root.");

        parent.transform.position = new Vector2(10, 0);
        first.transform.position = new Vector2(1, 2);
        middle.transform.position = new Vector2(5, 4);
        last.transform.position = new Vector2(9, 6);
        var childWorldPositions = parent.transform.children.Select(item => item.position).ToArray();
        Selection.activeGameObject = parent;
        Execute(harness, "Hierarchy/Center On Children");
        TestAssert.Require(parent.transform.position == new Vector2(5, 4) &&
                           parent.transform.children.Select(item => item.position)
                               .SequenceEqual(childWorldPositions),
            "Center On Children did not center the parent while preserving child world positions.");
        Undo.PerformUndo();
        TestAssert.Require(parent.transform.position == new Vector2(10, 0),
            "Undo did not restore the parent moved by Center On Children.");

        Selection.activeGameObject = middle;
        middle.transform.position = new Vector2(25, 10);
        harness.SetCameraPosition(new System.Numerics.Vector2(500, 500));
        Execute(harness, "Frame Selected");
        TestAssert.Require(harness.CameraPosition != new System.Numerics.Vector2(500, 500) &&
                           harness.IsSceneWindowSelected(),
            "Frame Selected did not focus the selection in the Scene window.");
    }

    private static void VerifyComponentCreation(
        EditorApplicationHarness harness,
        string command,
        string expectedName,
        Func<GameObject, bool> verify)
    {
        Selection.activeGameObject = null;
        Execute(harness, command);
        var created = harness.SelectedGameObject ??
                      throw new InvalidOperationException($"{command} did not select a created GameObject.");
        TestAssert.Require(created.name == expectedName && verify(created),
            $"{command} did not create a usable {expectedName} GameObject.");
        Undo.PerformUndo();
    }

    private static void VerifyUniqueSceneComponentCreation<T>(
        EditorApplicationHarness harness,
        string command,
        string expectedName) where T : Component
    {
        Selection.activeGameObject = null;
        Execute(harness, command);
        var created = harness.SelectedGameObject ??
                      throw new InvalidOperationException($"{command} did not select a created GameObject.");
        TestAssert.Require(created.name == expectedName && created.GetComponent<T>() is not null,
            $"{command} did not create a usable {expectedName} GameObject.");
        TestAssert.Require(!RequireItem(CaptureMainMenu(harness), command,
                $"GameObject menu with an existing {expectedName}").Enabled,
            $"{command} remained enabled after the active Scene already contained one.");
        Undo.PerformUndo();
        Selection.activeGameObject = null;
        TestAssert.Require(RequireItem(CaptureMainMenu(harness), command,
                $"GameObject menu after undoing {expectedName}").Enabled,
            $"{command} did not become available after Undo removed the existing component.");
    }

    private static IReadOnlyList<MenuSnapshot> CaptureMainMenu(EditorApplicationHarness harness)
    {
        var method = TestAssert.RequireMethod(harness.Application.GetType(), "MenuItems", InstanceMembers,
            typeof(string));
        var items = method.Invoke(harness.Application, ["GameObject"]) as IEnumerable ??
                    throw new InvalidOperationException("GameObject main menu returned no entries.");
        return items.Cast<object>().Select(item => new MenuSnapshot(
            Read<string>(item, "Label"),
            Read<bool>(item, "Enabled"),
            Read<Action?>(item, "Action"))).ToArray();
    }

    private static IReadOnlyList<MenuSnapshot> CaptureContextMenu(
        EditorApplicationHarness harness,
        GameObject target)
    {
        Selection.activeGameObject = target;
        GenericMenuCapture.Install();
        try
        {
            var method = harness.HierarchyWindow.GetType().GetMethod("ShowItemMenu", InstanceMembers) ??
                         throw new MissingMethodException(harness.HierarchyWindow.GetType().FullName,
                             "ShowItemMenu");
            method.Invoke(harness.HierarchyWindow, [target, null]);
            return GenericMenuCapture.CapturedItems
                .Where(item => !item.Separator)
                .Select(item => new MenuSnapshot(item.Path, item.Enabled, item.Action))
                .ToArray();
        }
        finally
        {
            GenericMenuCapture.Clear();
        }
    }

    private static void Execute(EditorApplicationHarness harness, string path)
    {
        var item = RequireItem(CaptureMainMenu(harness), path, "GameObject main menu");
        TestAssert.Require(item.Enabled, $"GameObject command '{path}' was unexpectedly disabled.");
        try
        {
            (item.Action ?? throw new InvalidOperationException(
                $"GameObject command '{path}' has no action."))();
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static MenuSnapshot RequireItem(
        IReadOnlyList<MenuSnapshot> items,
        string path,
        string source) =>
        items.SingleOrDefault(item => item.Path == path) ??
        throw new InvalidOperationException($"{source} does not contain '{path}'.");

    private static GameObject RequireObject(Scene scene, string name) =>
        scene.Find(name) ?? throw new InvalidOperationException($"Scene does not contain '{name}'.");

    private static T Read<T>(object item, string propertyName) =>
        (T)item.GetType().GetProperty(propertyName, InstanceMembers)!.GetValue(item)!;

    private sealed record MenuSnapshot(string Path, bool Enabled, Action? Action);
}
