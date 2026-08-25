using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.EditorWindowIcons;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var repository = FindRepository(AppContext.BaseDirectory);
        var root = Path.Combine(repository, "src", "Core");
        EditorResources.RegisterResourceRoot(root);

        var content = new GUIContent("Custom", "Icons/Windows/Scene.png", "Custom tooltip");
        Require(content.text == "Custom" && content.image.EndsWith("Scene.png") &&
                content.tooltip == "Custom tooltip", "GUIContent did not preserve text/image/tooltip.");
        var iconContent = EditorGUIUtility.IconContent("Game", "Play");
        Require(iconContent.text == "Play" && iconContent.image.EndsWith("Game.png"),
            "EditorGUIUtility.IconContent did not populate GUIContent.image.");

        var custom = new CustomIconWindow();
        Require(custom.titleContent.image == "Icons/Windows/Scene.png",
            "EditorWindowIcon did not initialize the custom window icon.");
        var fallback = new FallbackWindow();
        Require(fallback.titleContent.image == "Icons/Windows/Window.png",
            "EditorWindow without a specific icon did not receive the generic window icon.");

        var editorAssembly = Assembly.Load("BEngine.Editor");
        var workspaceType = editorAssembly.GetType("BEngine.Editor.ImGuiDockWorkspace", true)!;
        var workspace = Activator.CreateInstance(workspaceType, nonPublic: true)!;
        var areaType = editorAssembly.GetType("BEngine.Editor.DockArea", true)!;
        var center = Enum.Parse(areaType, "Center");
        var panel = workspaceType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(workspace, ["Custom", custom, center, true])!;
        var panelWindow = (EditorWindow)panel.GetType().GetProperty("Window")!.GetValue(panel)!;
        Require(ReferenceEquals(panelWindow, custom) &&
                (bool)workspaceType.GetMethod("IsSelected")!.Invoke(workspace, [custom])!,
            "IMGUI dock workspace did not retain and select the EditorWindow.");

        var updated = new GUIContent("Renamed", "Icons/Windows/Inspector.png", "Updated tooltip");
        custom.titleContent = updated;
        Require(ReferenceEquals(panelWindow.titleContent, updated) && panelWindow.titleContent.text == "Renamed" &&
                panelWindow.titleContent.tooltip == "Updated tooltip",
            "IMGUI dock panel did not observe the EditorWindow's updated titleContent.");

        var resolvedIcon = EditorResources.FindPath(updated.image);
        Require(resolvedIcon is not null && File.Exists(resolvedIcon),
            "EditorResources could not resolve the updated window icon.");

        Console.WriteLine("EDITOR_WINDOW_ICONS_OK|guicontent,iconcontent,attribute,fallback,imgui-dock,tooltip,dynamic-update,png");
        return 0;
    }

    private static string FindRepository(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate BEngine repository root.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [EditorWindowIcon("Icons/Windows/Scene.png")]
    private sealed class CustomIconWindow : EditorWindow;

    private sealed class FallbackWindow : EditorWindow;
}
