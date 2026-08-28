using System.Collections;
using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.ConsoleTreeView;

internal static class Program
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Assembly EditorAssembly = typeof(EditorWindow).Assembly;
    private static readonly Type ConsoleType = EditorAssembly.GetType(
        "BEngine.Editor.GpuEditorApplication+ImGuiConsoleWindow", throwOnError: true)!;
    private static readonly Type StoreType = EditorAssembly.GetType(
        "BEngine.Editor.EditorLogStore", throwOnError: true)!;
    private static readonly Type PreferencesType = EditorAssembly.GetType(
        "BEngine.Editor.ConsolePreferences", throwOnError: true)!;
    private static readonly MethodInfo OnGUI = RequireMethod(ConsoleType, "OnGUI", HiddenInstance);
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame", HiddenStatic) ??
                                                    throw new MissingMethodException(typeof(GUI).FullName,
                                                        "BeginFrame");
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame", HiddenStatic) ??
                                                  throw new MissingMethodException(typeof(GUI).FullName,
                                                      "EndFrame");
    private static readonly MethodInfo StoreAdd = RequireMethod(StoreType, "Add", HiddenStatic);
    private static readonly MethodInfo StoreClear = RequireMethod(StoreType, "Clear", HiddenStatic);

    private static int Main()
    {
        var collapse = ReadPreference("Collapse");
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            RequireMethod(StoreType, "Initialize", HiddenStatic).Invoke(null, null);
            SetPreference("Collapse", false);
            StoreClear.Invoke(null, null);
            var first = Entry(LogType.Info, "CONSOLE_TREE_FIRST");
            var second = Entry(LogType.Warning, "CONSOLE_TREE_SECOND");
            StoreAdd.Invoke(null, [first]);
            StoreAdd.Invoke(null, [second]);

            var treeType = ConsoleType.GetNestedType("ConsoleTreeView", BindingFlags.NonPublic) ??
                           throw new MissingMemberException(ConsoleType.FullName, "ConsoleTreeView");
            Require(InheritsTreeView(treeType),
                "Console row control does not inherit UnityEditor.IMGUI.Controls.TreeView<int>.");
            Require(ConsoleType.GetField("_listScroll", HiddenInstance) is null,
                "Console retained its legacy manual list scroll region.");

            var console = Activator.CreateInstance(ConsoleType, nonPublic: true) ??
                          throw new InvalidOperationException("Console window could not be created.");
            Render(console, new Event(EventType.Layout), 900, 320);
            var commands = Render(console, new Event(EventType.Repaint), 900, 320);
            var firstLabel = commands.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                command.Content.Contains(first.Message, StringComparison.Ordinal));
            Require(firstLabel.Type == GpuCanvasCommandType.Text,
                "Console TreeView did not render its first log row.");

            var tree = ConsoleType.GetField("_treeView", HiddenInstance)?.GetValue(console) ??
                       throw new InvalidOperationException("Console did not create its TreeView.");
            var rows = GetRows(tree);
            Require(rows.Length == 2, $"Console TreeView built {rows.Length} rows instead of 2.");

            var click = new Vector2((Fix64)(firstLabel.Rect.X + firstLabel.Rect.Width / 2),
                (Fix64)(firstLabel.Rect.Y + firstLabel.Rect.Height / 2));
            Render(console, new Event(EventType.MouseDown) { mousePosition = click, button = 0 }, 900, 320);
            var selected = (LogEntry?)ConsoleType.GetField("_selected", HiddenInstance)?.GetValue(console);
            Require(selected == first, "Clicking the first Console TreeView row did not select its log.");

            Render(console, new Event(EventType.KeyDown) { keyCode = KeyCode.DownArrow }, 900, 320);
            selected = (LogEntry?)ConsoleType.GetField("_selected", HiddenInstance)?.GetValue(console);
            Require(selected == second,
                "Keyboard navigation in the Console TreeView did not update the selected log.");

            ConsoleType.GetField("_search", HiddenInstance)?.SetValue(console, "SECOND");
            Render(console, new Event(EventType.Layout), 900, 320);
            Render(console, new Event(EventType.Repaint), 900, 320);
            Require(GetRows(tree).Length == 1,
                "Filtering did not rebuild the Console TreeView with only matching rows.");

            ConsoleType.GetField("_search", HiddenInstance)?.SetValue(console, string.Empty);
            StoreAdd.Invoke(null, [first with { Timestamp = first.Timestamp.AddSeconds(1) }]);
            SetPreference("Collapse", true);
            Render(console, new Event(EventType.Layout), 900, 320);
            Render(console, new Event(EventType.Repaint), 900, 320);
            rows = GetRows(tree);
            Require(rows.Length == 2, $"Collapsed Console TreeView built {rows.Length} rows instead of 2.");
            var view = rows[0].GetType().GetProperty("View", HiddenInstance)?.GetValue(rows[0]) ??
                       throw new MissingMemberException(rows[0].GetType().FullName, "View");
            var count = (int)(view.GetType().GetProperty("Count")?.GetValue(view) ?? 0);
            Require(count == 2, "Collapsed Console TreeView row lost its repeat count.");

            Console.WriteLine(
                "CONSOLE_TREEVIEW_OK|imgui-controls,rows,selection,keyboard,filter,collapse,details-binding");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            StoreClear.Invoke(null, null);
            SetPreference("Collapse", collapse);
        }
    }

    private static bool InheritsTreeView(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition().FullName ==
                "UnityEditor.IMGUI.Controls.TreeView`1" && current.GetGenericArguments()[0] == typeof(int))
                return true;
        return false;
    }

    private static object[] GetRows(object tree) =>
        ((IEnumerable)(tree.GetType().GetMethod("GetRows", BindingFlags.Instance | BindingFlags.Public)
                           ?.Invoke(tree, null) ??
                       throw new MissingMethodException(tree.GetType().FullName, "GetRows")))
        .Cast<object>().ToArray();

    private static List<GpuCanvasCommand> Render(object console, Event evt, int width, int height)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [evt, width, height, commands]);
        try { OnGUI.Invoke(console, null); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    private static LogEntry Entry(LogType type, string message) =>
        new(DateTimeOffset.UtcNow, type, message, $"at {nameof(Program)}.{nameof(Entry)}()");

    private static bool ReadPreference(string name) =>
        (bool)(PreferencesType.GetProperty(name, HiddenStatic)?.GetValue(null) ?? false);

    private static void SetPreference(string name, bool value) =>
        (PreferencesType.GetProperty(name, HiddenStatic) ??
         throw new MissingMemberException(PreferencesType.FullName, name)).SetValue(null, value);

    private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags) =>
        type.GetMethod(name, flags) ?? throw new MissingMethodException(type.FullName, name);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
