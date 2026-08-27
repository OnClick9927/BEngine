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
            Require(disabled.Color == GpuCanvasColor.FromColor(GUI.skin.menuItemDisabled.normal.textColor),
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
                                                GUI.skin.notificationBackground.disabled.backgroundColor)),
                "Popup menus do not render the Unity-style shadow layer.");
            popupType.GetMethod("Close")!.Invoke(popup, null);

            var enumMenu = new GenericMenu();
            enumMenu.AddItem(new GUIContent("Alpha"), true, () => { });
            enumMenu.AddItem(new GUIContent("Beta"), false, () => { });
            var enumAnchor = new Rect(100, 90, 120, 20);
            open.Invoke(popup, [Items(enumMenu), new Vector2(100, enumAnchor.yMax)]);
            var anchorHover = new Event(EventType.MouseMove)
            {
                mousePosition = new Vector2(110, 100)
            };
            Dispatch(draw, popup, anchorHover, [], enumAnchor);
            Require(anchorHover.type == EventType.Used && (bool)isOpen.GetValue(popup)!,
                "A short popup closed while the pointer was still over its source field anchor.");
            var menuHover = new Event(EventType.MouseMove)
            {
                mousePosition = new Vector2(110, 130)
            };
            Dispatch(draw, popup, menuHover, [], enumAnchor);
            Require(menuHover.type == EventType.Used && (bool)isOpen.GetValue(popup)!,
                "A short popup closed while moving from its source field into the menu.");
            popupType.GetMethod("Close")!.Invoke(popup, null);

            VerifyAdvancedDropdown();

            Menu.SetChecked("Window/Test", true);
            Menu.SetEnabled("Window/Test", false);
            Require(Menu.GetChecked("Window/Test") && !Menu.GetEnabled("Window/Test"),
                "Menu checked/enabled compatibility state is invalid.");
            Menu.SetChecked("Window/Test", false);
            Menu.SetEnabled("Window/Test", true);

            Console.WriteLine(
                "GPU_MENU_HIERARCHY_OK|component-root,submenus,checked,disabled,input-blocking,popup-anchor,advanced-hierarchy,advanced-search,advanced-mouse-reset,advanced-keyboard,advanced-scroll");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyAdvancedDropdown()
    {
        var popupType = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.ImGuiAdvancedDropdown", true)!;
        var popup = Activator.CreateInstance(popupType, nonPublic: true)!;
        var open = popupType.GetMethod("Open")!;
        var draw = popupType.GetMethod("Draw")!;
        var close = popupType.GetMethod("Close")!;

        var filteredInvoked = false;
        var searchMenu = new GenericMenu();
        searchMenu.AddItem(new GUIContent("Rendering/Sprite"), true, () => { });
        searchMenu.AddItem(new GUIContent("Rendering/UI/Panel"), false,
            () => filteredInvoked = true);
        searchMenu.AddItem(new GUIContent("Physics/Body"), false, () => { });
        searchMenu.AddItem(new GUIContent("Audio/UI/Panel"), false, () => { });
        open.Invoke(popup, [Items(searchMenu), new Vector2(20, 20), null]);

        var rootCommands = new List<GpuCanvasCommand>();
        DispatchAdvanced(draw, popup, new Event(EventType.Repaint), rootCommands);
        var focusedControl = (string?)typeof(GUI).GetField("_focusedControlName",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        Require(focusedControl == "BEngine.AdvancedDropdown.Search",
            "Advanced dropdown did not focus its search field when opened.");
        Require(Get<IReadOnlyList<string>>(popup, "visiblePaths").SequenceEqual(
                ["Rendering", "Physics", "Audio"], StringComparer.Ordinal),
            "Advanced dropdown did not collapse slash paths into root-level groups.");
        Require(rootCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                            command.Content == "Rendering") &&
                !rootCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                             command.Content == "Rendering/Sprite"),
            "Advanced dropdown rendered complete paths instead of the current hierarchy level.");

        var renderingPoint = PointForText(rootCommands, "Rendering");
        DispatchAdvanced(draw, popup, new Event(EventType.MouseMove)
        {
            mousePosition = renderingPoint
        }, []);
        Require(GetField<int>(popup, "_selectedIndex") == 0,
            "Hovering an advanced-dropdown group did not update its selection.");
        ClickAdvanced(draw, popup, renderingPoint);
        Require(Get<string>(popup, "currentPath") == "Rendering" &&
                Get<IReadOnlyList<string>>(popup, "visiblePaths").SequenceEqual(
                    ["Rendering/Sprite", "Rendering/UI"], StringComparer.Ordinal),
            "Clicking an advanced-dropdown group did not enter its child level.");

        var renderingCommands = new List<GpuCanvasCommand>();
        DispatchAdvanced(draw, popup, new Event(EventType.Repaint), renderingCommands);
        Require(renderingCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                 command.Content == "Sprite") &&
                renderingCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                 command.Content == "UI"),
            "The advanced-dropdown child level did not render leaf and group labels.");
        Require(renderingCommands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                                 command.Content.EndsWith("Check.png",
                                                     StringComparison.Ordinal)),
            "A checked nested advanced-dropdown item did not render its check mark.");
        ClickAdvanced(draw, popup, PointForText(renderingCommands, "UI"));
        Require(Get<string>(popup, "currentPath") == "Rendering/UI" &&
                Get<IReadOnlyList<string>>(popup, "visiblePaths").SequenceEqual(
                    ["Rendering/UI/Panel"], StringComparer.Ordinal),
            "Advanced dropdown did not support a second slash-delimited child level.");

        var nestedCommands = new List<GpuCanvasCommand>();
        DispatchAdvanced(draw, popup, new Event(EventType.Repaint), nestedCommands);
        ClickAdvanced(draw, popup, PointForText(nestedCommands, "Rendering/UI"));
        Require(Get<string>(popup, "currentPath") == "Rendering",
            "Clicking the advanced-dropdown path header did not return to its parent level.");
        renderingCommands.Clear();
        DispatchAdvanced(draw, popup, new Event(EventType.Repaint), renderingCommands);
        ClickAdvanced(draw, popup, PointForText(renderingCommands, "UI"));

        var backspace = new Event(EventType.KeyDown) { keyCode = KeyCode.Backspace };
        DispatchAdvanced(draw, popup, backspace, []);
        Require(backspace.type == EventType.Used && Get<string>(popup, "currentPath") == "Rendering",
            "Backspace did not navigate to the parent advanced-dropdown level.");
        var left = Event.KeyboardEvent("left");
        DispatchAdvanced(draw, popup, left, []);
        Require(left.type == EventType.Used && Get<string>(popup, "currentPath").Length == 0,
            "Left Arrow did not navigate to the root advanced-dropdown level.");

        TypeText(draw, popup, "rEnDeRiNg/uI");
        var enteredSearch = Get<string>(popup, "search");
        Require(enteredSearch == "rEnDeRiNg/uI",
            $"Advanced dropdown search did not accept keyboard input (actual: '{enteredSearch}').");
        var visiblePaths = Get<IReadOnlyList<string>>(popup, "visiblePaths");
        Require(visiblePaths.SequenceEqual(["Rendering/UI/Panel"], StringComparer.Ordinal),
            "Advanced dropdown search is not case-insensitive across the complete menu path.");

        var filteredCommands = new List<GpuCanvasCommand>();
        DispatchAdvanced(draw, popup, new Event(EventType.Repaint), filteredCommands);
        var filteredResult = filteredCommands.Single(command =>
            command.Type == GpuCanvasCommandType.Text &&
            command.Content == "Rendering/UI/Panel");
        var filteredPoint = new Vector2((Fix64)(filteredResult.Rect.X + 8),
            (Fix64)(filteredResult.Rect.Y + filteredResult.Rect.Height / 2));
        var filteredDown = new Event(EventType.MouseDown)
        {
            mousePosition = filteredPoint,
            button = 0
        };
        DispatchAdvanced(draw, popup, filteredDown, []);
        var filteredUp = new Event(EventType.MouseUp)
        {
            mousePosition = filteredPoint,
            button = 0
        };
        DispatchAdvanced(draw, popup, filteredUp, []);
        Require(filteredDown.type == EventType.Used && filteredUp.type == EventType.Used &&
                filteredInvoked && !Get<bool>(popup, "isOpen"),
            "Clicking a filtered advanced-dropdown result did not invoke it and close the popup.");

        open.Invoke(popup, [Items(searchMenu), new Vector2(20, 20), null]);
        DispatchAdvanced(draw, popup, new Event(EventType.Repaint), []);
        Require(Get<string>(popup, "search").Length == 0 &&
                Get<IReadOnlyList<string>>(popup, "visiblePaths").Count == 3,
            "Reopening an advanced dropdown restored stale text from the previous search.");

        TypeText(draw, popup, "Panel");
        Require(Get<IReadOnlyList<string>>(popup, "visiblePaths").SequenceEqual(
                ["Rendering/UI/Panel", "Audio/UI/Panel"], StringComparer.Ordinal),
            "Search results did not retain complete paths for duplicate leaf names.");

        var escape = Event.KeyboardEvent("escape");
        DispatchAdvanced(draw, popup, escape, []);
        Require(escape.type == EventType.Used && !Get<bool>(popup, "isOpen"),
            "Escape did not consume the key event and close the advanced dropdown.");

        var firstInvoked = false;
        var targetInvoked = false;
        var keyboardMenu = new GenericMenu();
        keyboardMenu.AddItem(new GUIContent("First"), false, () => firstInvoked = true);
        keyboardMenu.AddDisabledItem(new GUIContent("Disabled"));
        keyboardMenu.AddItem(new GUIContent("Target"), false, () => targetInvoked = true);
        open.Invoke(popup, [Items(keyboardMenu), new Vector2(20, 20), null]);
        Require(Get<IReadOnlyList<string>>(popup, "visiblePaths").Count == 3 &&
                GetField<int>(popup, "_selectedIndex") == 0,
            $"Advanced dropdown did not initialize keyboard selection " +
            $"(items: {Get<IReadOnlyList<string>>(popup, "visiblePaths").Count}, " +
            $"selected: {GetField<int>(popup, "_selectedIndex")}).");
        DispatchAdvanced(draw, popup, new Event(EventType.Repaint), []);
        var down = Event.KeyboardEvent("down");
        DispatchAdvanced(draw, popup, down, []);
        Require(down.type == EventType.Used,
            "Advanced dropdown leaked a keyboard navigation event.");
        Require(GetField<int>(popup, "_selectedIndex") == 2,
            $"Advanced dropdown did not skip its disabled row (selected: " +
            $"{GetField<int>(popup, "_selectedIndex")}).");
        var enter = Event.KeyboardEvent("enter");
        DispatchAdvanced(draw, popup, enter, []);
        Require(enter.type == EventType.Used && targetInvoked && !firstInvoked &&
                !Get<bool>(popup, "isOpen"),
            $"Down/Enter did not skip the disabled row, execute the next item, and close " +
            $"(event: {enter.type}, first: {firstInvoked}, target: {targetInvoked}, " +
            $"open: {Get<bool>(popup, "isOpen")}).");

        var externalInvoked = false;
        var hierarchyMenu = new GenericMenu();
        hierarchyMenu.AddItem(new GUIContent("External/Create/Sprite"), false, () => { });
        hierarchyMenu.AddItem(new GUIContent("External/Create/Tile"), false,
            () => externalInvoked = true);
        hierarchyMenu.AddDisabledItem(new GUIContent("External/Disabled"));
        open.Invoke(popup, [Items(hierarchyMenu), new Vector2(20, 20), null]);
        var enterGroup = Event.KeyboardEvent("enter");
        DispatchAdvanced(draw, popup, enterGroup, []);
        Require(enterGroup.type == EventType.Used && Get<string>(popup, "currentPath") == "External",
            "Enter did not open the selected root group from external menu data.");
        var rightGroup = Event.KeyboardEvent("right");
        DispatchAdvanced(draw, popup, rightGroup, []);
        Require(rightGroup.type == EventType.Used &&
                Get<string>(popup, "currentPath") == "External/Create",
            "Right Arrow did not open the selected nested group.");
        var selectTile = Event.KeyboardEvent("down");
        DispatchAdvanced(draw, popup, selectTile, []);
        var invokeTile = Event.KeyboardEvent("enter");
        DispatchAdvanced(draw, popup, invokeTile, []);
        Require(selectTile.type == EventType.Used && invokeTile.type == EventType.Used &&
                externalInvoked && !Get<bool>(popup, "isOpen"),
            "Keyboard navigation did not execute a nested item supplied through GenericMenu.");

        var longMenu = new GenericMenu();
        for (var index = 0; index < 63; index++)
        {
            var captured = index;
            longMenu.AddItem(new GUIContent($"Layers/Layer {index:00}"), false, () => _ = captured);
        }

        var anchor = new Rect(750, 570, 40, 20);
        open.Invoke(popup, [Items(longMenu), new Vector2(790, 590), anchor]);
        var commands = new List<GpuCanvasCommand>();
        DispatchAdvanced(draw, popup,
            new Event(EventType.Repaint) { mousePosition = new Vector2(790, 590) }, commands);
        var panel = commands
            .Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                              command.Color == GpuCanvasColor.FromColor(
                                  GUI.skin.dropDownList.normal.backgroundColor))
            .OrderByDescending(command => command.Rect.Width * command.Rect.Height)
            .First();
        Require(panel.Rect.X >= 0 && panel.Rect.Y >= 0 &&
                panel.Rect.X + panel.Rect.Width <= 800 &&
                panel.Rect.Y + panel.Rect.Height <= 600,
            "Advanced dropdown was not clamped inside the editor window.");
        ClickAdvanced(draw, popup, PointForText(commands, "Layers"));
        commands.Clear();
        DispatchAdvanced(draw, popup,
            new Event(EventType.Repaint) { mousePosition = new Vector2(790, 590) }, commands);
        panel = commands
            .Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                              command.Color == GpuCanvasColor.FromColor(
                                  GUI.skin.dropDownList.normal.backgroundColor))
            .OrderByDescending(command => command.Rect.Width * command.Rect.Height)
            .First();
        Require(panel.Rect.X >= 0 && panel.Rect.Y >= 0 &&
                panel.Rect.X + panel.Rect.Width <= 800 &&
                panel.Rect.Y + panel.Rect.Height <= 600 &&
                panel.Rect.Height < 400 && panel.Rect.Y + panel.Rect.Height <= (float)anchor.y,
            "The expanded child level was not height-limited or clamped above its bottom-edge anchor.");

        var scroll = new Event(EventType.ScrollWheel)
        {
            mousePosition = new Vector2((Fix64)(panel.Rect.X + 20), (Fix64)(panel.Rect.Y + 70)),
            delta = new Vector2(0, 5)
        };
        DispatchAdvanced(draw, popup, scroll, []);
        Require(scroll.type == EventType.Used && GetField<Vector2>(popup, "_scrollPosition").y > 0 &&
                Get<bool>(popup, "isOpen"),
            "A long advanced dropdown did not consume wheel input and scroll its bounded list.");
        close.Invoke(popup, null);
    }

    private static Vector2 PointForText(IEnumerable<GpuCanvasCommand> commands, string text)
    {
        var command = commands.Single(candidate => candidate.Type == GpuCanvasCommandType.Text &&
                                                   candidate.Content == text);
        return new Vector2((Fix64)(command.Rect.X + 8),
            (Fix64)(command.Rect.Y + command.Rect.Height / 2));
    }

    private static void ClickAdvanced(MethodInfo draw, object popup, Vector2 point)
    {
        DispatchAdvanced(draw, popup, new Event(EventType.MouseDown)
        {
            mousePosition = point,
            button = 0
        }, []);
        DispatchAdvanced(draw, popup, new Event(EventType.MouseUp)
        {
            mousePosition = point,
            button = 0
        }, []);
    }

    private static void Dispatch(MethodInfo draw, object popup, Event evt,
        List<GpuCanvasCommand> commands, Rect? anchor = null)
    {
        BeginFrame.Invoke(null, [evt, 800, 600, commands]);
        try { draw.Invoke(popup, [anchor]); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static void DispatchAdvanced(MethodInfo draw, object popup, Event evt,
        List<GpuCanvasCommand> commands)
    {
        BeginFrame.Invoke(null, [evt, 800, 600, commands]);
        try { draw.Invoke(popup, null); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static void TypeText(MethodInfo draw, object popup, string text)
    {
        foreach (var character in text)
            DispatchAdvanced(draw, popup, new Event(EventType.KeyDown) { character = character }, []);
    }

    private static object Items(GenericMenu menu) =>
        typeof(GenericMenu).GetProperty("Items", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(menu)!;

    private static T Get<T>(object instance, string property) =>
        (T)instance.GetType().GetProperty(property,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static T GetField<T>(object instance, string field) =>
        (T)instance.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
