using System.Reflection;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.UIElements;

namespace BEngine.ExampleTests.ScriptFieldInteraction;

internal static class Program
{
    private const int ViewportWidth = 360;
    private const int ViewportHeight = 64;

    private static int Main()
    {
        var field = new TextField("Script") { value = "ShowcaseMotion", isReadOnly = true };
        field.style.height = 28;
        var root = new VisualElement();
        root.style.flexGrow = 1;
        root.Add(field);

        var clicks = 0;
        var doubleClicks = 0;
        field.clicked += () => clicks++;
        field.doubleClicked += () => doubleClicks++;

        InvokeInternal(field, "RaiseClicked");
        Require(clicks == 1 && doubleClicks == 0,
            "Single click did not dispatch only the locate action.");
        InvokeInternal(field, "RaiseClicked");
        InvokeInternal(field, "RaiseDoubleClicked");
        Require(clicks == 2 && doubleClicks == 1,
            "Double click did not dispatch locate then open exactly once.");
        Require(field.value == "ShowcaseMotion", "Script field value changed through click dispatch.");

        var baseline = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight);
        SetTextEditingState(field, field.value, field.value.Length, true, 0, field.value.Length);
        var attemptedEdit = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight);
        Require(baseline.Commands.SequenceEqual(attemptedEdit.Commands),
            "A read-only Script field rendered a caret or selection.");
        Require(attemptedEdit.Commands.Any(command => command.Type == UIRenderCommandType.Text &&
                                                       ReferenceEquals(command.Element, field) &&
                                                       command.Content == "ShowcaseMotion"),
            "The Script field value was not emitted into the UI render list.");
        Require(attemptedEdit.TryGetRect(field, out var rect) && rect.Width > 0 && rect.Height > 0,
            "The Script field was not laid out in the UI render list.");

        var workspace = ProjectWorkspace.Open(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..")));
        var source = ProjectScriptSourceLocator.Find(workspace, "Game.ShowcaseMotion");
        Require(source is not null && Path.GetFileName(source).Equals("ShowcaseMotion.cs", StringComparison.OrdinalIgnoreCase),
            "Project script source could not be resolved for Project-window location.");

        var builtInSource = ProjectScriptSourceLocator.Find(workspace,
            typeof(SpriteRenderer).AssemblyQualifiedName!);
        var expectedBuiltInSource = Path.GetFullPath(Path.Combine(workspace.RootPath, "..", "src", "Core",
            "BEngine", "Core", "Components", "SpriteRenderer.cs"));
        Require(builtInSource is not null &&
                Path.GetFullPath(builtInSource).Equals(expectedBuiltInSource, StringComparison.OrdinalIgnoreCase),
            "Built-in Core component source could not be resolved from the engine repository layout.");

        Console.WriteLine("SCRIPT_FIELD_INTERACTION_OK|readonly,single-locate,double-open,no-caret,rendered," +
                          "project-source-resolved,builtin-source-resolved");
        return 0;
    }

    private static void SetTextEditingState(
        VisualElement element,
        string? text,
        int caretIndex,
        bool caretVisible,
        int selectionStart,
        int selectionEnd) => InvokeInternal(element, "SetTextEditingState",
            text, caretIndex, caretVisible, selectionStart, selectionEnd);

    private static void InvokeInternal(object target, string method, params object?[] arguments)
    {
        var member = target.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
                     typeof(VisualElement).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (member is null) throw new MissingMethodException(target.GetType().FullName, method);
        member.Invoke(target, arguments);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
