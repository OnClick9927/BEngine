using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.GpuImGui;

internal static class EditorObjectPingTests
{
    internal static void Run()
    {
        VerifyDefaultSnapshotIsInert();
        VerifyPingDoesNotChangeSelection();
        VerifyProjectAncestorReveal();
        VerifyHierarchyAncestorReveal();
        VerifyObjectFieldDoesNotPulse();
        VerifyDeterministicPulse();
        VerifyRepaintBorder();
    }

    private static void VerifyDefaultSnapshotIsInert()
    {
        EditorObjectPing.ResetForTests();
        var commands = new List<GpuCanvasCommand>();
        GUI.BeginFrame(new Event(EventType.Repaint), 180, 80, commands);
        try
        {
            Require(!EditorObjectPing.DrawPath("Assets/NotPinged.asset", new Rect(20, 20, 80, 22)),
                "The default ping snapshot matched an asset before PingObject was called.");
        }
        finally { GUI.EndFrame(); }

        Require(EditorObjectPing.currentTarget is null && commands.Count == 0,
            "The default ping snapshot emitted visual state before PingObject was called.");
    }

    private static void VerifyPingDoesNotChangeSelection()
    {
        var scene = new Scene("Ping Selection");
        var selected = scene.CreateGameObject("Selected");
        var pinged = scene.CreateGameObject("Pinged");
        var eventCount = 0;
        BObject? eventTarget = null;
        void OnPing(BObject target)
        {
            eventCount++;
            eventTarget = target;
        }

        Selection.activeObject = selected;
        EditorObjectPing.pinged += OnPing;
        try
        {
            EditorGUIUtility.PingObject(pinged);
            Require(ReferenceEquals(Selection.activeObject, selected),
                "PingObject changed the active Selection.");
            Require(eventCount == 1 && ReferenceEquals(eventTarget, pinged),
                "PingObject did not publish exactly one target notification.");
            Require(EditorObjectPing.snapshot.InstanceId == pinged.GetInstanceID(),
                "PingObject did not retain the pinged instance identity.");
        }
        finally
        {
            EditorObjectPing.pinged -= OnPing;
            Selection.activeObject = null;
        }
    }

    private static void VerifyProjectAncestorReveal()
    {
        var window = CreateNestedWindow("ImGuiProjectWindow");
        var type = window.GetType();
        var targetPath = "Assets/Characters/Hero/Materials/Hero.asset";
        var items = new[]
        {
            new ProjectBrowserItem("Assets", "Assets", "C:/Project/Assets", "Folder", true, false),
            new ProjectBrowserItem("Assets/Characters", "Characters", "C:/Project/Assets/Characters",
                "Folder", true, false),
            new ProjectBrowserItem("Assets/Characters/Hero", "Hero", "C:/Project/Assets/Characters/Hero",
                "Folder", true, false),
            new ProjectBrowserItem("Assets/Characters/Hero/Materials", "Materials",
                "C:/Project/Assets/Characters/Hero/Materials", "Folder", true, false),
            new ProjectBrowserItem(targetPath, "Hero", "C:/Project/Assets/Characters/Hero/Materials/Hero.asset",
                "Material", false, false)
        };
        var expanded = (HashSet<string>)type.GetField("_expanded",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var asset = new Material { assetPath = targetPath };

        window.OpenInternal();
        try
        {
            type.GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, items);
            type.GetField("_projectBrowserMode", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, "TwoColumn");
            type.GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, "does-not-match");
            window.isLocked = true;
            expanded.Clear();
            EditorGUIUtility.PingObject(asset);
            Require(new[]
                {
                    "Assets", "Assets/Characters", "Assets/Characters/Hero",
                    "Assets/Characters/Hero/Materials"
                }.All(expanded.Contains),
                "Project Ping did not expand the complete asset ancestor chain.");
            Require((string)type.GetField("_search", BindingFlags.Instance |
                        BindingFlags.NonPublic)!.GetValue(window)! == string.Empty,
                "Project Ping remained hidden by the active search filter.");
            Require((string?)type.GetField("_selectedPath", BindingFlags.Instance |
                        BindingFlags.NonPublic)!.GetValue(window) ==
                    "Assets/Characters/Hero/Materials",
                "Two-column Project Ping did not navigate to the target's containing folder.");
            Require((string?)type.GetField("_framePingPath", BindingFlags.Instance |
                        BindingFlags.NonPublic)!.GetValue(window) == targetPath,
                "Locked Project Ping did not queue its target for framing.");

            var commands = new List<GpuCanvasCommand>();
            GUI.BeginFrame(new Event(EventType.Repaint), 420, 100, commands);
            try { window.OnGUIInternal(); }
            finally { GUI.EndFrame(); }
            Require(type.GetField("_framePingPath", BindingFlags.Instance |
                        BindingFlags.NonPublic)!.GetValue(window) is null,
                "Project TreeView did not frame the revealed Ping target.");
            commands.Clear();
            GUI.BeginFrame(new Event(EventType.Repaint), 420, 100, commands);
            try { window.OnGUIInternal(); }
            finally { GUI.EndFrame(); }
            Require(YellowEdges(commands).Length == 4,
                "Project did not retain the yellow Ping frame on the revealed asset row.");
        }
        finally
        {
            window.CloseInternal();
            EditorObjectPing.ResetForTests();
        }
    }

    private static void VerifyHierarchyAncestorReveal()
    {
        var window = CreateNestedWindow("ImGuiHierarchyWindow");
        var type = window.GetType();
        var expanded = (HashSet<Guid>)type.GetField("_expanded",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var expandedScenes = (HashSet<Guid>)type.GetField("_expandedScenes",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        expanded.Clear();
        expandedScenes.Clear();

        using var scene = new Scene("Hierarchy Ping");
        var root = scene.CreateGameObject("Root");
        var branch = scene.CreateGameObject("Branch");
        branch.transform.SetParent(root.transform, false);
        var leaf = scene.CreateGameObject("Leaf");
        leaf.transform.SetParent(branch.transform, false);
        var component = leaf.AddComponent<Camera2D>();

        window.OpenInternal();
        try
        {
            type.GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, "does-not-match");
            window.isLocked = true;
            EditorGUIUtility.PingObject(component);
            Require(expandedScenes.Contains(scene.Id),
                "Hierarchy Ping did not expand the target Scene.");
            Require(expanded.SetEquals([root.Id, branch.Id]),
                "Hierarchy Ping did not expand every parent GameObject in the target chain.");
            Require((string)type.GetField("_search", BindingFlags.Instance |
                        BindingFlags.NonPublic)!.GetValue(window)! == string.Empty,
                "Hierarchy Ping remained hidden by the active search filter.");
            Require((Guid?)type.GetField("_pendingRevealId", BindingFlags.Instance |
                        BindingFlags.NonPublic)!.GetValue(window) == leaf.Id,
                "Locked Hierarchy Ping did not queue the Component owner for framing.");

            var commands = new List<GpuCanvasCommand>();
            GUI.BeginFrame(new Event(EventType.Repaint), 240, 80, commands);
            try
            {
                Require(EditorObjectPing.DrawHierarchy(leaf, new Rect(20, 20, 120, 22)),
                    "Hierarchy did not map the pinged Component to its GameObject row.");
            }
            finally { GUI.EndFrame(); }
            Require(YellowEdges(commands).Length == 4,
                "Hierarchy did not retain the yellow Ping frame on the target GameObject row.");
        }
        finally
        {
            window.CloseInternal();
            EditorObjectPing.ResetForTests();
        }
    }

    private static void VerifyObjectFieldDoesNotPulse()
    {
        EditorObjectPing.ResetForTests();
        using var scene = new Scene("ObjectField Ping");
        var target = scene.CreateGameObject("Target");
        var rect = new Rect(10, 10, 180, 22);

        void Draw(Event evt, List<GpuCanvasCommand>? commands = null)
        {
            GUI.BeginFrame(evt, 240, 60, commands ?? []);
            try { _ = EditorGUI.ObjectField(rect, target, typeof(GameObject), true); }
            finally { GUI.EndFrame(); }
        }

        Draw(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(40, 20), button = 0, clickCount = 1
        });
        Draw(new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(40, 20), button = 0, clickCount = 1
        });
        Require(EditorObjectPing.snapshot.InstanceId == target.GetInstanceID(),
            "The ObjectField click did not Ping its assigned BObject.");

        var commands = new List<GpuCanvasCommand>();
        Draw(new Event(EventType.Repaint), commands);
        Require(YellowEdges(commands).Length == 0,
            "The ObjectField itself still rendered a yellow Ping pulse.");
        EditorObjectPing.ResetForTests();
    }

    private static void VerifyDeterministicPulse()
    {
        var start = EditorObjectPing.Evaluate(0);
        var expanded = EditorObjectPing.Evaluate(EditorObjectPing.DurationSeconds / 4);
        var contracted = EditorObjectPing.Evaluate(EditorObjectPing.DurationSeconds / 2);
        var late = EditorObjectPing.Evaluate(EditorObjectPing.DurationSeconds * .75);
        var complete = EditorObjectPing.Evaluate(EditorObjectPing.DurationSeconds);

        Require(start.IsActive && expanded.IsActive && contracted.IsActive && late.IsActive,
            "The ping pulse was not active throughout its advertised duration.");
        Require(expanded.Expansion > start.Expansion && expanded.Expansion > contracted.Expansion,
            "The ping outline did not expand and contract deterministically.");
        Require(late.Expansion > contracted.Expansion,
            "The ping outline did not perform its second pulse.");
        Require(late.Alpha < start.Alpha,
            "The ping outline did not fade over time.");
        Require(!complete.IsActive && !EditorObjectPing.Evaluate(-.01).IsActive,
            "The ping pulse remained active outside its duration.");

        var source = new Rect(10, 20, 40, 18);
        var result = EditorObjectPing.Expand(source, 3);
        Require(result.Equals(new Rect(7, 17, 46, 24)),
            "Ping outline expansion changed the target center or dimensions.");
    }

    private static void VerifyRepaintBorder()
    {
        var target = new Scene("Ping Border").CreateGameObject("Target");
        EditorGUIUtility.PingObject(target);
        var commands = new List<GpuCanvasCommand>();
        GUI.BeginFrame(new Event(EventType.Repaint), 180, 80, commands);
        try
        {
            Require(EditorObjectPing.Draw(target, new Rect(20, 20, 80, 22)),
                "An active ping did not request its visual highlight.");
        }
        finally { GUI.EndFrame(); }

        var yellowEdges = YellowEdges(commands);
        Require(yellowEdges.Length == 4,
            $"Ping repaint emitted {yellowEdges.Length} yellow edges instead of four.");
        Require(yellowEdges.Count(command => Math.Abs(command.Rect.Height - 2) < .01f) == 2 &&
                yellowEdges.Count(command => Math.Abs(command.Rect.Width - 2) < .01f) == 2,
            "Ping repaint did not emit two horizontal and two vertical border edges.");
    }

    private static EditorWindow CreateNestedWindow(string name)
    {
        var application = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.GpuEditorApplication", throwOnError: true)!;
        var type = application.GetNestedType(name, BindingFlags.NonPublic) ??
                   throw new InvalidOperationException($"Editor window '{name}' was not found.");
        return (EditorWindow)(Activator.CreateInstance(type, nonPublic: true) ??
                              throw new InvalidOperationException($"Editor window '{name}' could not be created."));
    }

    private static GpuCanvasCommand[] YellowEdges(IEnumerable<GpuCanvasCommand> commands) =>
        commands.Where(command =>
            command.Type == GpuCanvasCommandType.SolidRect &&
            command.Color.R == byte.MaxValue && command.Color.G is >= 195 and <= 202 &&
            command.Color.B is >= 18 and <= 24 && command.Color.A > 0).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
