using BEngine.Documents;
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
            VerifyLayoutRoundTrip(root);
            Console.WriteLine("EDITOR_LAYOUT_PERSISTENCE_OK|yaml-v2,named,last-session,dock-tree,selection");
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

    private static void VerifyLayoutRoundTrip(string root)
    {
        Directory.CreateDirectory(root);
        new ProjectDocument { Name = "Layout Test" }.Save(Path.Combine(root, ProjectWorkspace.ProjectFileName));
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
        var document = new EditorLayoutDocument
        {
            Name = "Editing",
            ActiveLayout = "Editing",
            ProjectBrowserMode = "TwoColumn",
            ProjectFoldersWidth = 312,
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
            ]
        };

        var savedName = store.Save("Editing", document);
        store.SaveLastSession(document);
        Require(savedName == "Editing" && store.Names.SequenceEqual(["Editing"]),
            "Named layout was not listed after saving.");
        var loaded = store.Load("Editing");
        Require(loaded.Version == 2 && loaded.DockRoot is not null && loaded.Windows.Count == 2,
            "The v2 layout YAML did not preserve its window and dock records.");
        Require(loaded.ProjectBrowserMode == "TwoColumn" && loaded.ProjectFoldersWidth == 312 &&
                loaded.ProjectThumbnailSize == 104,
            "The layout YAML did not preserve Project browser mode, folder width, and preview scale.");
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

        var restored = new ImGuiDockWorkspace();
        restored.Add("Hierarchy", hierarchy, DockArea.Left, true);
        restored.Add("Scene", scene, DockArea.Center, true);
        var panels = restored.RestoreLayout(loaded.DockRoot,
            new Dictionary<string, EditorWindow> { ["Hierarchy"] = hierarchy, ["Scene"] = scene });
        Render(restored);
        Require(panels.Count == 2 && restored.IsSelected(scene),
            "Restoring the dock tree lost a window or selected tab.");
        Require(store.Delete("Editing") && store.Names.Count == 0,
            "Deleting a named layout left it in the layout list.");
        hierarchy.CloseInternal();
        scene.CloseInternal();
    }

    private static EditorWindowLayoutDocument WindowRecord(EditorWindow window) => new()
    {
        Id = window.PersistentId,
        TypeName = window.GetType().AssemblyQualifiedName!,
        State = nameof(EditorWindowState.Normal),
        Docked = true,
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
}
