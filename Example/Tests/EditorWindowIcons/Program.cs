using System.Collections;
using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.EditorWindowIcons;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "Core"));
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
        var workspaceType = editorAssembly.GetType("BEngine.Editor.DockWorkspace", true)!;
        using var workspace = (Control)Activator.CreateInstance(workspaceType, nonPublic: true)!;
        workspace.Size = new Size(800, 500);
        var zoneType = editorAssembly.GetType("BEngine.Editor.DockZone", true)!;
        var center = Enum.Parse(zoneType, "Center");
        var add = workspaceType.GetMethod("AddPanelContent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        add.Invoke(workspace, ["Custom", content, new Panel(), center, null]);

        var panels = (IDictionary)workspaceType.GetField("_panels", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(workspace)!;
        var record = panels["Custom"]!;
        var tab = (TabPage)record.GetType().GetProperty("Tab")!.GetValue(record)!;
        Require(ReferenceEquals(tab.Tag, content) && tab.Text == "Custom" &&
                tab.ToolTipText == "Custom tooltip", "Dock tab did not consume GUIContent.");

        var updated = new GUIContent("Renamed", "Icons/Windows/Inspector.png", "Updated tooltip");
        workspaceType.GetMethod("UpdatePanelContent")!.Invoke(workspace, ["Custom", updated]);
        Require(tab.Text == "Renamed" && tab.ToolTipText == "Updated tooltip" &&
                ReferenceEquals(tab.Tag, updated), "Dock tab did not synchronize updated titleContent.");

        var cacheType = editorAssembly.GetType("BEngine.Editor.DockIconCache", true)!;
        var tryImage = cacheType.GetMethod("TryGetImage")!;
        var arguments = new object?[] { updated.image, null };
        Require((bool)tryImage.Invoke(null, arguments)! && arguments[1] is Bitmap,
            "Dock icon cache could not decode an EditorResources window icon.");

        Console.WriteLine("EDITOR_WINDOW_ICONS_OK|guicontent,iconcontent,attribute,fallback,tab,tooltip,dynamic-update,png");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [EditorWindowIcon("Icons/Windows/Scene.png")]
    private sealed class CustomIconWindow : EditorWindow;

    private sealed class FallbackWindow : EditorWindow;
}
