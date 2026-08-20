using System.Reflection;
using BEngine.ProjectSystem.Editor;
using BEngine.Serialization;
using BEngine.UIElements;

namespace BEngine.ExampleTests.ScriptFieldInteraction;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        var editorAssembly = Assembly.Load("BEngine.Editor");
        var hostType = editorAssembly.GetType("BEngine.UIElements.Editor.GpuVisualElementHost", true)!;
        using var host = (Control)Activator.CreateInstance(hostType, nonPublic: true)!;
        host.Size = new Size(360, 64);

        var field = new TextField("Script") { value = "Rotator", isReadOnly = true };
        field.style.height = 28;
        var clicks = 0;
        var doubleClicks = 0;
        field.clicked += () => clicks++;
        field.doubleClicked += () => doubleClicks++;
        var root = new VisualElement();
        root.Add(field);
        hostType.GetProperty("Root")!.SetValue(host, root);

        InvokeMouse(hostType, host, 1);
        Require(clicks == 1 && doubleClicks == 0, "Single click did not dispatch only the locate action.");
        Require(!ReadBoolean(field, "focused", "_focused"), "Read-only Script field entered text focus.");
        Require(!ReadBoolean(field, "textEditingCaretVisible", "_textEditingCaretVisible"),
            "Read-only Script field displayed an input caret.");

        InvokeMouse(hostType, host, 2);
        Require(clicks == 2 && doubleClicks == 1, "Double click did not dispatch locate then open exactly once.");
        Require(field.value == "Rotator", "Script field value changed through pointer interaction.");

        var workspace = ProjectWorkspace.Open(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..")));
        var source = ProjectScriptSourceLocator.Find(workspace, "Game.Rotator");
        Require(source is not null && Path.GetFileName(source).Equals("Rotator.cs", StringComparison.OrdinalIgnoreCase),
            "Project script source could not be resolved for Project-window location.");

        Console.WriteLine("SCRIPT_FIELD_INTERACTION_OK|readonly,single-locate,double-open,no-caret,source-resolved");
        return 0;
    }

    private static void InvokeMouse(Type hostType, Control host, int clicks) =>
        hostType.GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host,
            [new MouseEventArgs(MouseButtons.Left, clicks, 330, 12, 0)]);

    private static bool ReadBoolean(object target, params string[] names)
    {
        foreach (var type in EnumerateTypes(target.GetType()))
        foreach (var name in names)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic |
                                                  BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (property is not null) return Convert.ToBoolean(property.GetValue(target));
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic |
                                            BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field is not null) return Convert.ToBoolean(field.GetValue(target));
        }
        throw new MissingMemberException(target.GetType().FullName, string.Join('/', names));
    }

    private static IEnumerable<Type> EnumerateTypes(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType) yield return current;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
