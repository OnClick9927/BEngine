using System.Collections;
using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class MainMenuIntegrationTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public |
                                                         BindingFlags.NonPublic;

    private static readonly string[] ShortcutTokens =
    [
        "Ctrl+Shift+P", "Ctrl+Alt+P", "Ctrl+Shift+A", "Ctrl+N", "Ctrl+O", "Ctrl+S",
        "Ctrl+Z", "Ctrl+Y", "Ctrl+C", "Ctrl+V", "Ctrl+D", "Ctrl+A", "Ctrl+P", "F2", "Del", "F"
    ];

    internal static void Run(SceneFixture fixture)
    {
        VerifyMainMenuContract(fixture);
        VerifyValidators(fixture);
        VerifyCommandBehavior(fixture);
        VerifyFaultIsolation(fixture);
    }

    private static void VerifyMainMenuContract(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["File"] =
            [
                "New Scene", "Open Scene...", "Open Scene Additive...", "Save Scene", "Save All Scenes",
                "Show Project in Explorer", "Exit"
            ],
            ["Edit"] =
            [
                "Undo", "Redo", "Copy", "Paste", "Duplicate", "Rename", "Delete", "Select All",
                "Deselect All", "Frame Selected", "Play", "Pause", "Step", "Recompile Scripts",
                "Preferences...", "Project Settings..."
            ],
            ["Assets"] =
            [
                "Create/Folder", "Create/C# Script", "Create/Scene", "Create/Prefab", "Open",
                "Show in Explorer", "Copy Path", "Copy Full Path", "Rename", "Duplicate", "Delete",
                "Reimport", "Refresh", "Import New Asset...", "Import Package...", "Export Package..."
            ],
            ["GameObject"] =
            [
                "Create Empty", "Create Empty Child", "Create Empty Parent", "2D Object/Sprite", "Camera 2D",
                "Transform/Reset", "Hierarchy/Move To Root", "Frame Selected", "Rename", "Duplicate", "Delete"
            ],
            ["Component"] =
            [
                "Enable All Components", "Disable All Components", "Reset All Components",
                "Remove Missing Scripts"
            ],
            ["Window"] =
            [
                "Layouts/Save Current", "Layouts/Save As...", "Hierarchy", "Scene", "Game", "Inspector",
                "Project", "Panels/Close Focused Tab", "Panels/Lock Focused Window",
                "Panels/Maximize Focused Tab", "Panels/Next Window", "Panels/Previous Window"
            ],
            ["Help"] =
            [
                "View Editor Log", "Reveal Logs Folder", "Copy System Info", "About BEngine"
            ]
        };

        foreach (var (root, requiredPaths) in expected)
        {
            var items = Capture(harness, root);
            TestAssert.Require(items.Count > 0, $"The {root} main menu is empty.");
            TestAssert.Require(items.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() == items.Count,
                $"The {root} main menu contains duplicate command paths.");
            foreach (var path in requiredPaths) RequireItem(items, path, root);
            TestAssert.Require(items.Where(item => item.Enabled).All(item => item.Action is not null),
                $"The {root} main menu contains an enabled item without an action.");
        }

        var file = Capture(harness, "File");
        RequireShortcut(file, "New Scene", "Ctrl+N");
        RequireShortcut(file, "Open Scene...", "Ctrl+O");
        RequireShortcut(file, "Save Scene", "Ctrl+S");

        var edit = Capture(harness, "Edit");
        RequireShortcut(edit, "Undo", "Ctrl+Z");
        RequireShortcut(edit, "Redo", "Ctrl+Y");
        RequireShortcut(edit, "Copy", "Ctrl+C");
        RequireShortcut(edit, "Paste", "Ctrl+V");
        RequireShortcut(edit, "Duplicate", "Ctrl+D");
        RequireShortcut(edit, "Rename", "F2");
        RequireShortcut(edit, "Delete", "Del");
        RequireShortcut(edit, "Select All", "Ctrl+A");
        RequireShortcut(edit, "Frame Selected", "F");
        RequireShortcut(edit, "Play", "Ctrl+P");
        RequireShortcut(edit, "Pause", "Ctrl+Shift+P");
        RequireShortcut(edit, "Step", "Ctrl+Alt+P");
    }

    private static void VerifyValidators(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        Selection.activeGameObject = null;

        var file = Capture(harness, "File");
        foreach (var path in new[]
                 {
                     "New Scene", "Open Scene...", "Open Scene Additive...", "Save Scene", "Save All Scenes",
                     "Show Project in Explorer", "Exit"
                 })
            RequireEnabled(file, path, "Edit mode with an open project");

        var edit = Capture(harness, "Edit");
        foreach (var path in new[] { "Copy", "Duplicate", "Rename", "Delete", "Deselect All", "Frame Selected" })
            RequireDisabled(edit, path, "an empty selection");
        RequireEnabled(edit, "Select All", "a loaded Scene");
        RequireEnabled(edit, "Play", "Edit mode");
        RequireDisabled(edit, "Pause", "Edit mode");
        RequireDisabled(edit, "Step", "Edit mode");

        harness.SetPlaying(true);
        file = Capture(harness, "File");
        foreach (var path in new[] { "New Scene", "Open Scene...", "Open Scene Additive...", "Save Scene" })
            RequireDisabled(file, path, "Play mode");
        edit = Capture(harness, "Edit");
        RequireEnabled(edit, "Pause", "Play mode");
        RequireEnabled(edit, "Step", "Play mode");
        RequireDisabled(edit, "Recompile Scripts", "Play mode");
        TestAssert.Require(RequireItem(edit, "Play", "Play mode").Checked,
            "Edit/Play did not display its checked state while playing.");
        harness.SetPlaying(false);

        var assets = Capture(harness, "Assets");
        RequireDisabled(assets, "Open", "no Project selection");
        RequireDisabled(assets, "Rename", "no Project selection");
        RequireEnabled(assets, "Refresh", "no Project selection");

        var gameObject = Capture(harness, "GameObject");
        RequireEnabled(gameObject, "Create Empty", "an empty selection");
        RequireDisabled(gameObject, "Delete", "an empty selection");

        var component = Capture(harness, "Component");
        foreach (var path in new[]
                 {
                     "Enable All Components", "Disable All Components",
                     "Reset All Components", "Remove Missing Scripts"
                 })
            RequireDisabled(component, path, "an empty selection");

        var help = Capture(harness, "Help");
        foreach (var path in new[] { "View Editor Log", "Reveal Logs Folder", "Copy System Info", "About BEngine" })
            RequireEnabled(help, path, "an initialized editor log and project");

        var target = harness.InitialScene.CreateGameObject("Main Menu Validator Target");
        var sprite = target.AddComponent<SpriteRenderer>();
        sprite.enabled = true;
        Selection.activeGameObject = target;

        edit = Capture(harness, "Edit");
        foreach (var path in new[] { "Copy", "Duplicate", "Rename", "Delete", "Deselect All", "Frame Selected" })
            RequireEnabled(edit, path, "a selected GameObject");
        RequireDisabled(edit, "Paste", "before anything has been copied");

        component = Capture(harness, "Component");
        RequireEnabled(component, "Disable All Components", "an enabled Component");
        RequireDisabled(component, "Enable All Components", "all Components enabled");
        RequireEnabled(component, "Reset All Components", "an editable GameObject");
        RequireDisabled(component, "Remove Missing Scripts", "a GameObject without missing scripts");

        harness.FocusHierarchy();
        var window = Capture(harness, "Window");
        foreach (var path in new[]
                 {
                     "Panels/Close Focused Tab", "Panels/Lock Focused Window", "Panels/Maximize Focused Tab",
                     "Panels/Next Window", "Panels/Previous Window"
                 })
            RequireEnabled(window, path, "a focused docked window");
    }

    private static void VerifyCommandBehavior(SceneFixture fixture)
    {
        Undo.ClearAll();
        using var harness = new EditorApplicationHarness(fixture);
        var scene = harness.InitialScene;
        var root = scene.Find("First Root") ??
                   throw new InvalidOperationException("The File/Edit menu fixture root is missing.");

        Selection.activeGameObject = root;
        var objectCount = scene.gameObjects.Count;
        Execute("Edit/Duplicate");
        TestAssert.Require(scene.gameObjects.Count > objectCount &&
                           harness.SelectedGameObject is { } duplicate && !ReferenceEquals(duplicate, root),
            "Edit/Duplicate did not duplicate and select the GameObject hierarchy.");
        Undo.PerformUndo();

        Selection.activeGameObject = root;
        Execute("Edit/Deselect All");
        TestAssert.Require(Selection.count == 0 && harness.SelectedGameObject is null,
            "Edit/Deselect All did not clear the editor selection.");
        Execute("Edit/Select All");
        TestAssert.Require(Selection.gameObjects.Length == scene.gameObjects.Count,
            "Edit/Select All did not select every GameObject in the active Scene.");

        Selection.activeGameObject = root;
        GUIUtility.systemCopyBuffer = string.Empty;
        Execute("GameObject/Copy Hierarchy Path");
        TestAssert.Require(GUIUtility.systemCopyBuffer.EndsWith("First Root", StringComparison.Ordinal),
            "GameObject/Copy Hierarchy Path was not executable through EditorApplication.ExecuteMenuItem.");

        SetField(harness.ProjectWindow, "_selectedPath", "Assets/Scenes/First.scene.yaml");
        GUIUtility.systemCopyBuffer = string.Empty;
        Execute("Assets/Copy Path");
        TestAssert.Require(GUIUtility.systemCopyBuffer == "Assets/Scenes/First.scene.yaml",
            "Assets/Copy Path was not executable through EditorApplication.ExecuteMenuItem.");

        var componentTarget = scene.CreateGameObject("Main Menu Component Target");
        var camera = componentTarget.AddComponent<Camera2D>();
        var sprite = componentTarget.AddComponent<SpriteRenderer>();
        Selection.activeGameObject = componentTarget;
        Execute("Component/Disable All Components");
        TestAssert.Require(!camera.enabled && !sprite.enabled,
            "Component/Disable All Components did not disable all non-Transform Components.");
        RequireEnabled(Capture(harness, "Component"), "Enable All Components", "disabled Components");
        Undo.PerformUndo();
        TestAssert.Require(camera.enabled && sprite.enabled,
            "Undo did not restore Components disabled from the Component menu.");

        componentTarget.transform.localPosition = new Vector2(7, 8);
        Execute("Component/Reset All Components");
        TestAssert.Require(componentTarget.transform.localPosition == Vector2.zero,
            "Component/Reset All Components did not reset Transform values.");
        Undo.PerformUndo();
        TestAssert.Require(componentTarget.transform.localPosition == new Vector2(7, 8),
            "Undo did not restore values reset from the Component menu.");

        var missing = componentTarget.AddComponent<MissingComponent>();
        Execute("Component/Remove Missing Scripts");
        TestAssert.Require(componentTarget.GetComponent<MissingComponent>() is null,
            "Component/Remove Missing Scripts did not remove a removable missing Component.");
        Undo.PerformUndo();
        TestAssert.Require(ReferenceEquals(componentTarget.GetComponent<MissingComponent>(), missing),
            "Undo did not restore the missing Component removed from the Component menu.");

        harness.FocusHierarchy();
        var hierarchy = (EditorWindow)harness.HierarchyWindow;
        Execute("Window/Panels/Lock Focused Window");
        TestAssert.Require(hierarchy.isLocked,
            "Window/Panels/Lock Focused Window did not lock the focused window.");
        TestAssert.Require(RequireItem(Capture(harness, "Window"), "Panels/Lock Focused Window",
                "a locked focused window").Checked,
            "Window/Panels/Lock Focused Window did not display its checked state.");
        Execute("Window/Panels/Lock Focused Window");
        TestAssert.Require(!hierarchy.isLocked,
            "Window/Panels/Lock Focused Window did not toggle back to unlocked.");

        var focused = EditorWindow.focusedWindow;
        Execute("Window/Panels/Next Window");
        TestAssert.Require(EditorWindow.focusedWindow is not null && !ReferenceEquals(EditorWindow.focusedWindow, focused),
            "Window/Panels/Next Window did not move focus to another open window.");
        Execute("Window/Panels/Previous Window");
        TestAssert.Require(ReferenceEquals(EditorWindow.focusedWindow, focused),
            "Window/Panels/Previous Window did not restore the previous focused window.");

        GUIUtility.systemCopyBuffer = string.Empty;
        Execute("Help/Copy System Info");
        TestAssert.Require(GUIUtility.systemCopyBuffer.Contains("BEngine", StringComparison.OrdinalIgnoreCase) &&
                           GUIUtility.systemCopyBuffer.Contains(".NET", StringComparison.OrdinalIgnoreCase),
            "Help/Copy System Info did not copy useful engine and runtime information.");

        root.name = "Saved Through File Menu";
        TestAssert.Require(EditorSceneManager.MarkSceneDirty(scene),
            "The File menu test could not mark its Scene dirty.");
        Execute("File/Save Scene");
        TestAssert.Require(File.ReadAllText(fixture.FirstScenePath)
                .Contains("Saved Through File Menu", StringComparison.Ordinal),
            "File/Save Scene was not executable through EditorApplication.ExecuteMenuItem.");
        Undo.ClearAll();
    }

    private static void VerifyFaultIsolation(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        MainMenuFaultProbe.Reset();
        TestAssert.Require(!EditorApplication.ExecuteMenuItem(MainMenuFaultProbe.FaultingPath),
            "A faulting top-level menu command reported success.");
        TestAssert.Require(EditorApplication.ExecuteMenuItem(MainMenuFaultProbe.HealthyPath) &&
                           MainMenuFaultProbe.HealthyCalls == 1,
            "A faulting File menu command prevented a healthy Help menu command from executing.");
        TestAssert.Require(EditorApplication.ExecuteMenuItem("Help/Copy System Info"),
            "A faulting extension menu command poisoned a built-in menu command.");
    }

    private static IReadOnlyList<MenuSnapshot> Capture(EditorApplicationHarness harness, string root)
    {
        var method = TestAssert.RequireMethod(harness.Application.GetType(), "MenuItems", InstanceMembers,
            typeof(string));
        var values = method.Invoke(harness.Application, [root]) as IEnumerable ??
                     throw new InvalidOperationException($"{root} main menu returned no entries.");
        return values.Cast<object>().Select(value =>
        {
            var label = Read<string>(value, "Label");
            return new MenuSnapshot(label, StripShortcut(label), Read<bool>(value, "Enabled"),
                Read<bool>(value, "Checked"), Read<Action?>(value, "Action"));
        }).ToArray();
    }

    private static void Execute(string path) => TestAssert.Require(EditorApplication.ExecuteMenuItem(path),
        $"EditorApplication.ExecuteMenuItem did not execute '{path}'.");

    private static void RequireShortcut(IReadOnlyList<MenuSnapshot> items, string path, string shortcut)
    {
        var item = RequireItem(items, path, "shortcut contract");
        TestAssert.Require(item.Label.EndsWith(shortcut, StringComparison.Ordinal),
            $"'{path}' does not display the expected {shortcut} shortcut label.");
    }

    private static void RequireEnabled(IReadOnlyList<MenuSnapshot> items, string path, string context)
    {
        var item = RequireItem(items, path, context);
        TestAssert.Require(item.Enabled && item.Action is not null,
            $"'{path}' should be enabled for {context}.");
    }

    private static void RequireDisabled(IReadOnlyList<MenuSnapshot> items, string path, string context) =>
        TestAssert.Require(!RequireItem(items, path, context).Enabled,
            $"'{path}' should be disabled for {context}.");

    private static MenuSnapshot RequireItem(IReadOnlyList<MenuSnapshot> items, string path, string source) =>
        items.SingleOrDefault(item => item.Path.Equals(path, StringComparison.Ordinal)) ??
        throw new InvalidOperationException($"The {source} menu does not contain '{path}'.");

    private static string StripShortcut(string label)
    {
        foreach (var shortcut in ShortcutTokens)
            if (label.EndsWith(shortcut, StringComparison.Ordinal))
                return label[..^shortcut.Length].TrimEnd();
        return label.Trim();
    }

    private static T Read<T>(object target, string propertyName) =>
        (T)target.GetType().GetProperty(propertyName, InstanceMembers)!.GetValue(target)!;

    private static void SetField(object target, string fieldName, object? value) =>
        (target.GetType().GetField(fieldName, InstanceMembers) ??
         throw new MissingFieldException(target.GetType().FullName, fieldName)).SetValue(target, value);

    private sealed record MenuSnapshot(string Label, string Path, bool Enabled, bool Checked, Action? Action);
}

internal static class MainMenuFaultProbe
{
    internal const string FaultingPath = "File/Fault Isolation/Throw";
    internal const string HealthyPath = "Help/Fault Isolation/Healthy";

    internal static int HealthyCalls { get; private set; }

    [MenuItem(FaultingPath, false, -50_000)]
    private static void Throw() => throw new InvalidOperationException("MAIN_MENU_FAULT_SENTINEL");

    [MenuItem(HealthyPath, false, -50_000)]
    private static void Healthy() => HealthyCalls++;

    internal static void Reset() => HealthyCalls = 0;
}
