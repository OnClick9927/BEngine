using System.Collections;
using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.NativeGenericMenu;

internal static partial class Program
{
    private static int Main()
    {
        try
        {
            VerifyNativeMenuModel();
            VerifyPresentationRouting();
            VerifyMouseExitPolicy();
            VerifyNativeMainMenuRoots();
            VerifyNativeMainMenuAttachment();
            Console.WriteLine(
                "NATIVE_GENERIC_MENU_OK|hierarchy,checked,disabled,separators,callbacks," +
                "system-routing,no-hwnd-fallback,mouse-exit-dismissal,native-main-menu-roots," +
                "native-main-menu-attach-cleanup");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyPresentationRouting()
    {
        var dispatcher = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.GenericMenuDispatcher", true)!;
        var handler = dispatcher.GetProperty("Handler",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var current = dispatcher.GetProperty("CurrentPresentation",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var kinds = new List<string>();
        var callbackType = handler.PropertyType;
        var target = new PresentationCapture(current, kinds);
        var parameter = System.Linq.Expressions.Expression.Parameter(
            callbackType.GenericTypeArguments[0], "items");
        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Constant(target),
            typeof(PresentationCapture).GetMethod(nameof(PresentationCapture.Capture))!,
            System.Linq.Expressions.Expression.Convert(parameter, typeof(object)));
        var capture = System.Linq.Expressions.Expression.Lambda(callbackType, call, parameter).Compile();
        handler.SetValue(null, capture);
        try
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Command"), false, () => { });
            menu.ShowAsContext();
            menu.DropDown(new Rect(1, 2, 3, 4));
            menu.ShowAsAdvancedDropdown();
        }
        finally
        {
            handler.SetValue(null, null);
        }
        Require(kinds.SequenceEqual(["Context", "DropDown", "AdvancedDropDown"]),
            "GenericMenu did not route ordinary and advanced presentations distinctly.");
    }

    private static void VerifyMouseExitPolicy()
    {
        var type = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.NativeMenuMouseExitPolicy", true)!;
        var observe = type.GetMethod("Observe", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var contextPolicy = Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [false], null)!;
        Require(!(bool)observe.Invoke(contextPolicy, [false, false])!,
            "A context menu cancelled before its native menu window was entered.");
        Require(!(bool)observe.Invoke(contextPolicy, [true, false])!,
            "Entering a native menu incorrectly cancelled it.");
        Require((bool)observe.Invoke(contextPolicy, [false, false])!,
            "Leaving a native menu did not request dismissal.");
        Require(!(bool)observe.Invoke(contextPolicy, [false, false])!,
            "The mouse-exit policy requested cancellation more than once.");

        var anchoredPolicy = Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [true], null)!;
        Require(!(bool)observe.Invoke(anchoredPolicy, [false, true])!,
            "The menu cancelled while the pointer remained on its owning title.");
        Require((bool)observe.Invoke(anchoredPolicy, [false, false])!,
            "Leaving an owning menu title did not dismiss its menu.");
    }

    private static void VerifyNativeMainMenuRoots()
    {
        var type = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.Win32MainMenuBar", true)!;
        var normalize = type.GetMethod("NormalizeRoots", BindingFlags.Static | BindingFlags.NonPublic)!;
        var roots = (string[])normalize.Invoke(null,
            [new[] { "File", "Edit", "file", string.Empty, "Tools" }])!;
        Require(roots.SequenceEqual(["File", "Edit", "Tools"]),
            "The native main menu bar did not preserve ordered, case-insensitive unique roots.");
    }

    private static void VerifyNativeMainMenuAttachment()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var owner = new System.Windows.Forms.Form();
        var handle = owner.Handle;
        var originalWindowProcedure = GetWindowLongPtr(handle, -4);
        var type = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.Win32MainMenuBar", true)!;
        var create = type.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
        var providerType = create.GetParameters()[2].ParameterType;
        var root = System.Linq.Expressions.Expression.Parameter(typeof(string), "root");
        var source = new NativeMainMenuSource();
        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Constant(source),
            typeof(NativeMainMenuSource).GetMethod(nameof(NativeMainMenuSource.Items))!, root);
        var provider = System.Linq.Expressions.Expression.Lambda(providerType,
            System.Linq.Expressions.Expression.Convert(call, providerType.GenericTypeArguments[1]), root).Compile();
        var instance = create.Invoke(null, [handle, new[] { "File", "Edit" }, provider]);
        Require(instance is not null && GetMenu(handle) != IntPtr.Zero,
            "The Win32 main menu bar was not attached to a live owner HWND.");
        var firstRoot = GetSubMenu(GetMenu(handle), 0);
        _ = SendMessage(handle, 0x0117, firstRoot, IntPtr.Zero);
        Require(GetMenuItemCount(firstRoot) == 1,
            "WM_INITMENUPOPUP did not dynamically populate the selected native menu root.");
        var command = GetMenuItemId(firstRoot, 0);
        Require(command != uint.MaxValue, "The native main-menu command did not receive an ID.");
        _ = SendMessage(handle, 0x0111, (IntPtr)command, IntPtr.Zero);
        Require(source.Invocations == 1,
            "WM_COMMAND did not route the selected native main-menu item to its callback.");
        ((IDisposable)instance!).Dispose();
        Require(GetMenu(handle) == IntPtr.Zero,
            "Disposing the Win32 main menu bar left an HMENU attached to its owner HWND.");
        Require(GetWindowLongPtr(handle, -4) == originalWindowProcedure,
            "Disposing the Win32 main menu bar did not restore the owner's window procedure.");
    }

    private static void VerifyNativeMenuModel()
    {
        var invoked = false;
        var repeatedInvocations = new List<int>();
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Parent/Checked"), true, () => invoked = true);
        menu.AddDisabledItem(new GUIContent("Parent/Disabled"));
        menu.AddSeparator("Parent/");
        menu.AddItem(new GUIContent("Standalone"), false, () => { });
        menu.AddItem(new GUIContent("Repeated/Action"), false, () => repeatedInvocations.Add(1));
        menu.AddItem(new GUIContent("Repeated/Action"), false, () => repeatedInvocations.Add(2));

        var presenterType = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.Win32GenericMenuPresenter", true)!;
        var buildNodes = presenterType.GetMethod("BuildNodes",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var items = typeof(GenericMenu).GetProperty("Items",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tryShow = presenterType.GetMethod("TryShow",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Require(!(bool)tryShow.Invoke(null, [IntPtr.Zero, Vector2.zero, items, null])!,
            "The native menu did not fall back when no native owner window was available.");
        var roots = ((IEnumerable)buildNodes.Invoke(null, [items])!).Cast<object>().ToArray();
        Require(roots.Length == 3 && Get<string>(roots[0], "Name") == "Parent" &&
                Get<string>(roots[1], "Name") == "Standalone" &&
                Get<string>(roots[2], "Name") == "Repeated",
            "The native menu did not preserve root order or slash hierarchy.");

        var children = ((IEnumerable)Get<object>(roots[0], "Children")).Cast<object>().ToArray();
        Require(children.Length == 3 && Get<string>(children[0], "Name") == "Checked" &&
                Get<bool>(children[0], "On") && Get<bool>(children[0], "IsEnabled"),
            "The native menu lost checked or enabled child state.");
        Require(Get<string>(children[1], "Name") == "Disabled" &&
                !Get<bool>(children[1], "IsEnabled"),
            "The native menu did not retain a disabled item.");
        Require(Get<bool>(children[2], "Separator"),
            "The native menu did not place a path-scoped separator in its submenu.");
        Require(Get<bool>(roots[0], "IsEnabled"),
            "A native submenu with an enabled command was incorrectly disabled.");
        var repeated = ((IEnumerable)Get<object>(roots[2], "Children")).Cast<object>().ToArray();
        Require(repeated.Length == 2 && repeated.All(node => Get<string>(node, "Name") == "Action") &&
                repeated.All(node => Get<Action?>(node, "Action") is not null),
            "The native menu collapsed repeated labels and lost a command callback.");
        foreach (var node in repeated) Get<Action>(node, "Action")();
        Require(repeatedInvocations.SequenceEqual([1, 2]),
            "The native menu routed repeated labels to the same callback.");

        Get<Action>(children[0], "Action")();
        Require(invoked, "The native menu did not retain its callback.");
    }

    private static T Get<T>(object instance, string property) =>
        (T)instance.GetType().GetProperty(property,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class PresentationCapture(PropertyInfo current, List<string> kinds)
    {
        public void Capture(object items)
        {
            var presentation = current.GetValue(null)!;
            kinds.Add(presentation.GetType().GetProperty("Kind",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(presentation)!.ToString()!);
        }
    }

    private sealed class NativeMainMenuSource
    {
        public int Invocations { get; private set; }

        public object Items(string root)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent($"{root} Command"), false, () => Invocations++);
            return typeof(GenericMenu).GetProperty("Items",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetMenu", ExactSpelling = true)]
    private static extern IntPtr GetMenu(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW",
        ExactSpelling = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetSubMenu", ExactSpelling = true)]
    private static extern IntPtr GetSubMenu(IntPtr menu, int position);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetMenuItemCount", ExactSpelling = true)]
    private static extern int GetMenuItemCount(IntPtr menu);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetMenuItemID", ExactSpelling = true)]
    private static extern uint GetMenuItemId(IntPtr menu, int position);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
