using System.Reflection;
using BEngine.UIElements;

namespace BEngine.ExampleTests.TextCaretInteraction;

internal static class Program
{
    private const int ViewportWidth = 360;
    private const int ViewportHeight = 64;

    private static int Main()
    {
        VerifyTextFieldCaretAndSelection();
        VerifyTextFieldValues();
        VerifySearchField();
        VerifyNumericFields();
        VerifyReadOnlyField();

        Console.WriteLine(
            "TEXT_CARET_INTERACTION_OK|visible,rendered,blink,position,selection,text,search,float,integer,blur,readonly");
        return 0;
    }

    private static void VerifyTextFieldCaretAndSelection()
    {
        var field = new TextField { value = "abcd" };
        var root = CreateRoot(field);
        SetInteractionState(field, isFocused: true);
        SetTextEditingState(field, field.value, field.value.Length, true);

        Require(ReadState<bool>(field, "focused"), "Focused state was not applied to the TextField.");
        Require(ReadState<int>(field, "textEditingCaretIndex") == 4,
            "TextField caret did not start at the requested text boundary.");
        Require(ReadState<bool>(field, "textEditingCaretVisible"),
            "Focused TextField did not expose a visible caret state.");

        var visible = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;
        SetTextEditingState(field, field.value, field.value.Length, false);
        var hidden = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;
        var caretCommands = visible.Except(hidden).ToArray();
        Require(caretCommands is [{ Type: UIRenderCommandType.SolidRect } caret] &&
                ReferenceEquals(caret.Element, field) &&
                (double)caret.Rect.Width <= 2 && (double)caret.Rect.Height >= 4,
            "A visible caret did not add exactly one thin render command.");

        SetTextEditingState(field, field.value, 2, true, 0, field.value.Length);
        var selected = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;
        SetTextEditingState(field, field.value, 2, true, 2, 2);
        var unselected = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;
        var selectionCommands = selected.Except(unselected).ToArray();
        Require(selectionCommands is [{ Type: UIRenderCommandType.SolidRect } selection] &&
                ReferenceEquals(selection.Element, field) && (double)selection.Rect.Width > 8,
            "A complete text selection did not add one visible selection rectangle.");
        Require(ReadState<int>(field, "textEditingCaretIndex") == 2,
            "The requested caret boundary was not retained.");

        SetTextEditingState(field, field.value, 999, true);
        Require(ReadState<int>(field, "textEditingCaretIndex") == field.value.Length,
            "Caret state was not clamped to the current text length.");

        SetInteractionState(field, isFocused: false);
        SetTextEditingState(field, null, 0, false);
        Require(!ReadState<bool>(field, "focused") &&
                !ReadState<bool>(field, "textEditingCaretVisible"),
            "Blur state did not clear focus and hide the caret.");
    }

    private static void VerifyTextFieldValues()
    {
        var field = new TextField { value = "abcdef" };
        var changes = new List<string>();
        field.valueChanged += changes.Add;
        field.value = "abXcdef";
        field.SetValueWithoutNotify("Z");
        Require(field.value == "Z" && changes.SequenceEqual(["abXcdef"]),
            "TextField value notification or silent replacement semantics regressed.");

        var root = CreateRoot(field);
        SetTextEditingState(field, field.value, 1, true);
        var commands = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;
        Require(commands.Any(command => command.Type == UIRenderCommandType.Text &&
                                        ReferenceEquals(command.Element, field) && command.Content == "Z"),
            "Updated TextField content was not rendered.");
    }

    private static void VerifySearchField()
    {
        var field = new SearchField { placeholderText = "Find scripts" };
        var root = CreateRoot(field);
        var emptyCommands = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;
        Require(emptyCommands.Any(command => command.Type == UIRenderCommandType.Text &&
                                             ReferenceEquals(command.Element, field) &&
                                             command.Content == "Find scripts"),
            "Empty SearchField did not render its placeholder.");

        var changes = new List<string>();
        field.searchChanged += changes.Add;
        field.value = "find";
        field.ClearSearch();
        field.SetValueWithoutNotify("asset");
        Require(field.value == "asset" && changes.SequenceEqual(["find", ""]),
            "SearchField change, clear, or silent-set semantics regressed.");
    }

    private static void VerifyNumericFields()
    {
        var floatField = new FloatField { value = 2.7f };
        var integerField = new IntegerField { value = 23 };
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;
        root.Add(floatField);
        root.Add(integerField);
        floatField.style.height = 28;
        integerField.style.height = 28;

        var commands = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;
        Require(commands.Any(command => command.Type == UIRenderCommandType.Text &&
                                        ReferenceEquals(command.Element, floatField) && command.Content == "2.7"),
            "FloatField did not render its invariant-culture value.");
        Require(commands.Any(command => command.Type == UIRenderCommandType.Text &&
                                        ReferenceEquals(command.Element, integerField) && command.Content == "23"),
            "IntegerField did not render its value.");
    }

    private static void VerifyReadOnlyField()
    {
        var field = new TextField { value = "Script.cs", isReadOnly = true };
        var root = CreateRoot(field);
        var baseline = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;
        SetInteractionState(field, isFocused: true);
        SetTextEditingState(field, field.value, field.value.Length, true, 0, field.value.Length);
        var attemptedEdit = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;

        Require(attemptedEdit.Count == baseline.Count,
            "A read-only TextField rendered a caret or selection command.");
        Require(field.value == "Script.cs", "Read-only rendering changed the TextField value.");
    }

    private static VisualElement CreateRoot(VisualElement field)
    {
        field.style.height = 28;
        var root = new VisualElement();
        root.style.flexGrow = 1;
        root.Add(field);
        return root;
    }

    private static void SetInteractionState(
        VisualElement element,
        bool? isHovered = null,
        bool? isPressed = null,
        bool? isFocused = null) => InvokeVisualElementMethod(element, "SetInteractionState",
            isHovered, isPressed, isFocused);

    private static void SetTextEditingState(
        VisualElement element,
        string? text,
        int caretIndex,
        bool caretVisible,
        int selectionStart = 0,
        int selectionEnd = 0) => InvokeVisualElementMethod(element, "SetTextEditingState",
            text, caretIndex, caretVisible, selectionStart, selectionEnd);

    private static T ReadState<T>(VisualElement element, string property)
    {
        var member = typeof(VisualElement).GetProperty(property,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
                     throw new MissingMemberException(typeof(VisualElement).FullName, property);
        return (T)member.GetValue(element)!;
    }

    private static void InvokeVisualElementMethod(
        VisualElement element,
        string method,
        params object?[] arguments)
    {
        var member = typeof(VisualElement).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
                     throw new MissingMethodException(typeof(VisualElement).FullName, method);
        member.Invoke(element, arguments);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
