using System.Reflection;
using BEngine.UIElements;

namespace BEngine.ExampleTests.TextCaretInteraction;

internal static class Program
{
    private const int HostWidth = 360;
    private const int HostHeight = 64;

    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        var editorAssembly = Assembly.Load("BEngine.Editor");
        var hostType = editorAssembly.GetType("BEngine.UIElements.Editor.GpuVisualElementHost", true)!;
        using var host = (Control)Activator.CreateInstance(hostType, nonPublic: true)!;
        host.Size = new Size(HostWidth, HostHeight);

        VerifyTextField(hostType, host);
        VerifyPointerCaretAndDoubleClickSelection(hostType, host);
        VerifySearchField(hostType, host);
        VerifyFloatField(hostType, host);
        VerifyIntegerField(hostType, host);
        VerifyReadOnlyField(hostType, host);

        Console.WriteLine(
            "TEXT_CARET_INTERACTION_OK|visible,rendered,blink,pointer-position,double-click-select-all,replace-selection,text,search,float,integer,navigation,delete,backspace,blur,readonly");
        return 0;
    }

    private static void VerifyPointerCaretAndDoubleClickSelection(Type hostType, Control host)
    {
        var field = new TextField { value = "abcdef" };
        field.style.height = 28;
        var root = new VisualElement();
        root.style.flexGrow = 1;
        root.Add(field);
        hostType.GetProperty("Root")!.SetValue(host, root);

        var mouseX = -1;
        for (var x = 4; x < HostWidth - 24; x++)
        {
            InvokeMethod(hostType, host, "OnMouseDown",
                new MouseEventArgs(MouseButtons.Left, 1, x, 12, 0));
            if (GetCaretIndex(host, field) != 2) continue;
            mouseX = x;
            break;
        }
        Require(mouseX >= 0, "No pointer position mapped to the second text boundary.");
        Require(GetCaretIndex(host, field) == 2,
            "Single-clicking between the second and third characters did not move the caret there.");
        KeyPress(hostType, host, 'X');
        Require(field.value == "abXcdef",
            "Typing after a pointer-positioned caret did not insert at the clicked position.");

        InvokeMethod(hostType, host, "OnMouseDown",
            new MouseEventArgs(MouseButtons.Left, 2, mouseX, 12, 0));
        Require(GetSelectionStart(field) == 0 && GetSelectionEnd(field) == field.value.Length,
            "Double-clicking an input did not select its complete value.");
        var selectedCommands = UIRenderListBuilder.Build(root, HostWidth, HostHeight).Commands;
        Require(selectedCommands.Any(command => command.Type == UIRenderCommandType.SolidRect &&
                                                ReferenceEquals(command.Element, field) &&
                                                (double)command.Rect.Width > 8),
            "The GPU render list did not contain a visible full-text selection rectangle.");
        KeyPress(hostType, host, 'Z');
        Require(field.value == "Z" && GetCaretIndex(host, field) == 1,
            "Typing after double-click selection did not replace all input text.");

        field = new TextField { value = "abcdef" };
        field.style.height = 28;
        root = new VisualElement();
        root.style.flexGrow = 1;
        root.Add(field);
        hostType.GetProperty("Root")!.SetValue(host, root);
        InvokeMethod(hostType, host, "OnMouseDown",
            new MouseEventArgs(MouseButtons.Left, 2, mouseX, 12, 0));
        InvokeMethod(hostType, host, "OnMouseDown",
            new MouseEventArgs(MouseButtons.Left, 1, HostWidth - 24, 12, 0));
        KeyPress(hostType, host, '!');
        Require(field.value == "abcdef!",
            "A single click after select-all did not clear selection and place the caret at the pointer.");
    }

    private static void VerifyTextField(Type hostType, Control host)
    {
        var field = new TextField { value = "abcd" };
        var root = FocusField(hostType, host, field);

        Require(GetFocused(field), "Clicking a TextField did not focus it.");
        Require(GetCaretVisible(host, field), "Focused TextField did not show its caret immediately.");
        Require(GetCaretIndex(host, field) == 4, "TextField caret did not start at the clicked end position.");

        var visibleCommands = UIRenderListBuilder.Build(root, HostWidth, HostHeight).Commands;
        ToggleCaret(hostType, host);
        Require(!GetCaretVisible(host, field), "The caret blink tick did not hide the TextField caret.");
        var hiddenCommands = UIRenderListBuilder.Build(root, HostWidth, HostHeight).Commands;
        Require(visibleCommands.Count == hiddenCommands.Count + 1,
            "A visible caret did not add exactly one GPU render command.");
        var caretCommand = visibleCommands.FirstOrDefault(command => !hiddenCommands.Contains(command));
        Require(caretCommand.Type == UIRenderCommandType.SolidRect &&
                ReferenceEquals(caretCommand.Element, field) &&
                (double)caretCommand.Rect.Width <= 2 && (double)caretCommand.Rect.Height >= 4,
            "The extra render command was not a thin caret owned by the focused TextField.");
        ToggleCaret(hostType, host);
        Require(GetCaretVisible(host, field), "The second caret blink tick did not show the caret again.");

        KeyDown(hostType, host, Keys.Left);
        Require(GetCaretIndex(host, field) == 3, "Left did not move the TextField caret.");
        KeyPress(hostType, host, 'X');
        Require(field.value == "abcXd" && GetCaretIndex(host, field) == 4,
            "Typing did not insert at the TextField caret.");
        Require(GetCaretVisible(host, field), "Typing did not restart the visible caret phase.");

        KeyDown(hostType, host, Keys.Home);
        Require(GetCaretIndex(host, field) == 0, "Home did not move the caret to the beginning.");
        KeyDown(hostType, host, Keys.Delete);
        Require(field.value == "bcXd" && GetCaretIndex(host, field) == 0,
            "Delete did not remove the character after the caret.");
        KeyDown(hostType, host, Keys.End);
        KeyDown(hostType, host, Keys.Back);
        Require(field.value == "bcX" && GetCaretIndex(host, field) == 3,
            "Backspace did not remove the character before the caret.");
        KeyDown(hostType, host, Keys.Right);
        Require(GetCaretIndex(host, field) == 3, "Right moved the caret past the text end.");

        InvokeMethod(hostType, host, "OnLostFocus", EventArgs.Empty);
        Require(!GetFocused(field) && !GetCaretVisible(host, field),
            "Losing host focus did not hide the TextField caret.");
    }

    private static void VerifySearchField(Type hostType, Control host)
    {
        var field = new SearchField { value = "find" };
        FocusField(hostType, host, field);
        Require(GetCaretVisible(host, field), "Focused SearchField did not show its caret.");

        KeyDown(hostType, host, Keys.Home);
        KeyPress(hostType, host, 'x');
        KeyDown(hostType, host, Keys.End);
        KeyDown(hostType, host, Keys.Left);
        KeyDown(hostType, host, Keys.Back);
        Require(field.value == "xfid" && GetEditingText(host, field) == "xfid" &&
                GetCaretIndex(host, field) == 3,
            "SearchField caret navigation and editing produced the wrong text.");
    }

    private static void VerifyFloatField(Type hostType, Control host)
    {
        var field = new FloatField { value = 12.5f };
        FocusField(hostType, host, field);
        Require(GetCaretVisible(host, field), "Focused FloatField did not show its caret.");

        KeyDown(hostType, host, Keys.Home);
        KeyDown(hostType, host, Keys.Delete);
        Require(Math.Abs(field.value - 2.5f) < 0.0001f && GetCaretIndex(host, field) == 0,
            "FloatField Delete did not edit from the caret position.");
        KeyDown(hostType, host, Keys.End);
        KeyDown(hostType, host, Keys.Back);
        Require(GetEditingText(host, field) == "2." && Math.Abs(field.value - 2f) < 0.0001f,
            "FloatField Backspace did not update the edit buffer from the caret position.");
        KeyPress(hostType, host, '7');
        Require(GetEditingText(host, field) == "2.7" && Math.Abs(field.value - 2.7f) < 0.0001f,
            "FloatField did not commit the completed value after caret insertion.");
    }

    private static void VerifyIntegerField(Type hostType, Control host)
    {
        var field = new IntegerField { value = 123 };
        FocusField(hostType, host, field);
        Require(GetCaretVisible(host, field), "Focused IntegerField did not show its caret.");

        KeyDown(hostType, host, Keys.Home);
        KeyDown(hostType, host, Keys.Delete);
        Require(field.value == 23 && GetCaretIndex(host, field) == 0,
            "IntegerField Delete did not edit from the beginning.");
        KeyDown(hostType, host, Keys.End);
        KeyDown(hostType, host, Keys.Left);
        KeyDown(hostType, host, Keys.Delete);
        Require(field.value == 2 && GetEditingText(host, field) == "2" && GetCaretIndex(host, field) == 1,
            "IntegerField caret movement and Delete produced the wrong value.");
    }

    private static void VerifyReadOnlyField(Type hostType, Control host)
    {
        var field = new TextField { value = "Script.cs", isReadOnly = true };
        FocusField(hostType, host, field);
        Require(!GetCaretVisible(host, field), "A read-only TextField showed an editable caret.");
        KeyPress(hostType, host, 'X');
        Require(field.value == "Script.cs", "A read-only TextField accepted keyboard input.");
    }

    private static VisualElement FocusField(Type hostType, Control host, VisualElement field)
    {
        field.style.height = 28;
        var root = new VisualElement();
        root.style.flexGrow = 1;
        root.Add(field);
        hostType.GetProperty("Root")!.SetValue(host, root);
        InvokeMethod(hostType, host, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1,
            HostWidth - 24, 12, 0));
        return root;
    }

    private static void KeyDown(Type hostType, Control host, Keys key) =>
        InvokeMethod(hostType, host, "OnKeyDown", new KeyEventArgs(key));

    private static void KeyPress(Type hostType, Control host, char value) =>
        InvokeMethod(hostType, host, "OnKeyPress", new KeyPressEventArgs(value));

    private static void ToggleCaret(Type hostType, Control host)
    {
        var method = FindMethod(hostType, "ToggleCaretVisibility", "ToggleCaret");
        var parameters = method.GetParameters();
        method.Invoke(host, parameters.Length switch
        {
            0 => null,
            2 => [null, EventArgs.Empty],
            _ => throw new InvalidOperationException(
                $"Unsupported caret blink method signature: {method.Name}({parameters.Length}).")
        });
    }

    private static int GetCaretIndex(Control host, VisualElement field) =>
        Convert.ToInt32(ReadState(field, host,
            "textEditingCaretIndex", "caretIndex", "textCaretIndex",
            "_textEditingCaretIndex", "_caretIndex", "_textCaretIndex"));

    private static bool GetCaretVisible(Control host, VisualElement field) =>
        Convert.ToBoolean(ReadState(field, host,
            "textEditingCaretVisible", "caretVisible", "textCaretVisible",
            "_textEditingCaretVisible", "_caretVisible", "_textCaretVisible"));

    private static string GetEditingText(Control host, VisualElement field) =>
        Convert.ToString(ReadState(field, host,
            "textEditingValue", "editingText", "_textEditingValue", "editBuffer", "_editBuffer")) ?? string.Empty;

    private static int GetSelectionStart(VisualElement field) =>
        Convert.ToInt32(ReadState(field, field,
            "textEditingSelectionStart", "_textEditingSelectionStart"));

    private static int GetSelectionEnd(VisualElement field) =>
        Convert.ToInt32(ReadState(field, field,
            "textEditingSelectionEnd", "_textEditingSelectionEnd"));

    private static bool GetFocused(VisualElement field) =>
        Convert.ToBoolean(ReadState(field, field, "focused", "_focused"));

    private static object? ReadState(object preferred, object fallback, params string[] memberNames)
    {
        if (TryReadMember(preferred, memberNames, out var value)) return value;
        if (TryReadMember(fallback, memberNames, out value)) return value;
        throw new MissingMemberException(
            $"Neither {preferred.GetType().FullName} nor {fallback.GetType().FullName} exposes " +
            string.Join("/", memberNames));
    }

    private static bool TryReadMember(object target, IReadOnlyList<string> names, out object? value)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var name in names)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public |
                                                      BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                                                      BindingFlags.IgnoreCase);
                if (property is not null)
                {
                    value = property.GetValue(target);
                    return true;
                }
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                                                BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                                                BindingFlags.IgnoreCase);
                if (field is null) continue;
                value = field.GetValue(target);
                return true;
            }
        }
        value = null;
        return false;
    }

    private static void InvokeMethod(Type type, object target, string method, object argument) =>
        FindMethod(type, method).Invoke(target, [argument]);

    private static MethodInfo FindMethod(Type type, params string[] names)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var name in names)
            {
                var method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
                                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method is not null) return method;
            }
        throw new MissingMethodException(type.FullName, string.Join("/", names));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
