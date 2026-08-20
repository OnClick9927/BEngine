using System.Collections;
using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.GpuMenuHierarchy;

internal static class Program
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            var applicationType = typeof(EditorWindow).Assembly.GetType(
                "BEngine.Editor.GpuEditorApplication", true)!;
            var builtInRoots = (string[])applicationType.GetField("BuiltInMenuRoots",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Require(builtInRoots.Contains("Component", StringComparer.Ordinal),
                "The editor menu bar is missing its Component root.");
            var invoked = false;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Parent/Child"), true, () => invoked = true);
            menu.AddDisabledItem(new GUIContent("Parent/Disabled"));
            menu.AddSeparator("Parent/");
            menu.AddItem(new GUIContent("Standalone"), false, () => { });

            var items = typeof(GenericMenu).GetProperty("Items", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(menu)!;
            var itemArray = ((IEnumerable)items).Cast<object>().ToArray();
            Require(itemArray.Any(item => Get<bool>(item, "On")), "GenericMenu did not retain checked state.");
            Require(itemArray.Any(item => !Get<bool>(item, "Enabled") && !Get<bool>(item, "Separator")),
                "GenericMenu did not retain disabled state.");

            var popupType = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.ImGuiPopupMenu", true)!;
            var popup = Activator.CreateInstance(popupType, nonPublic: true)!;
            var open = popupType.GetMethod("Open")!;
            var draw = popupType.GetMethod("Draw")!;
            var isOpen = popupType.GetProperty("isOpen")!;
            open.Invoke(popup, [items, new Vector2(20, 20)]);

            var hoverEvent = new Event(EventType.MouseMove) { mousePosition = new Vector2(35, 32) };
            Dispatch(draw, popup, hoverEvent, []);
            Require(hoverEvent.type == EventType.Used,
                "A popup hover event leaked through to the GUI underneath.");
            var commands = new List<GpuCanvasCommand>();
            Dispatch(draw, popup, new Event(EventType.Repaint) { mousePosition = new Vector2(35, 32) }, commands);
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == "Parent"),
                "Root menu was not rendered.");
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == "Child"),
                "Slash path did not open a child menu.");
            Require(!commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                             command.Content.Contains('/', StringComparison.Ordinal)),
                "Slash path was rendered as flat text.");
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                             command.Content.EndsWith("Check.png", StringComparison.Ordinal)),
                "Checked menu item did not render a check mark.");

            var disabled = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                       command.Content == "Disabled");
            Require(disabled.Color == GpuCanvasColor.FromColor(EditorAppearance.palette.DisabledText),
                "Disabled menu item was not visually disabled.");
            var scrollEvent = new Event(EventType.ScrollWheel)
            {
                mousePosition = new Vector2(35, 32),
                delta = new Vector2(0, 1)
            };
            Dispatch(draw, popup, scrollEvent, []);
            Require(scrollEvent.type == EventType.Used && (bool)isOpen.GetValue(popup)!,
                "A popup scroll event leaked through or closed the popup.");
            var child = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                    command.Content == "Child");
            var childPoint = new Vector2((Fix64)(child.Rect.X + 8), (Fix64)(child.Rect.Y + child.Rect.Height / 2));
            Dispatch(draw, popup, new Event(EventType.MouseDown) { mousePosition = childPoint, button = 0 }, []);
            Dispatch(draw, popup, new Event(EventType.MouseUp) { mousePosition = childPoint, button = 0 }, []);
            Require(invoked && !(bool)isOpen.GetValue(popup)!, "Enabled child item was not invoked and closed.");

            open.Invoke(popup, [items, new Vector2(20, 20)]);
            var leaveEvent = new Event(EventType.MouseMove) { mousePosition = new Vector2(700, 500) };
            Dispatch(draw, popup, leaveEvent, []);
            Require(!(bool)isOpen.GetValue(popup)!, "Menu did not close when the mouse left its menu tree.");
            Require(leaveEvent.type == EventType.Used,
                "The event which dismissed a popup leaked through to the GUI underneath.");

            open.Invoke(popup, [items, new Vector2(760, 20)]);
            Dispatch(draw, popup, new Event(EventType.MouseMove) { mousePosition = new Vector2(640, 32) }, []);
            commands.Clear();
            Dispatch(draw, popup, new Event(EventType.Repaint) { mousePosition = new Vector2(640, 32) }, commands);
            var edgeParent = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                        command.Content == "Parent");
            var edgeChild = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                       command.Content == "Child");
            Require(edgeChild.Rect.X + edgeChild.Rect.Width <= edgeParent.Rect.X,
                "A right-edge submenu overlaps its parent instead of opening to the left.");
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                            command.Color == GpuCanvasColor.FromColor(
                                                EditorAppearance.palette.Shadow)),
                "Popup menus do not render the Unity-style shadow layer.");
            popupType.GetMethod("Close")!.Invoke(popup, null);

            Menu.SetChecked("Window/Test", true);
            Menu.SetEnabled("Window/Test", false);
            Require(Menu.GetChecked("Window/Test") && !Menu.GetEnabled("Window/Test"),
                "Menu checked/enabled compatibility state is invalid.");
            Menu.SetChecked("Window/Test", false);
            Menu.SetEnabled("Window/Test", true);

            Console.WriteLine("GPU_MENU_HIERARCHY_OK|component-root,submenus,checked,disabled,input-blocking");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Dispatch(MethodInfo draw, object popup, Event evt, List<GpuCanvasCommand> commands)
    {
        BeginFrame.Invoke(null, [evt, 800, 600, commands]);
        try { draw.Invoke(popup, [null]); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static T Get<T>(object instance, string property) =>
        (T)instance.GetType().GetProperty(property)!.GetValue(instance)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
