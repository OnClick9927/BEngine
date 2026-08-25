using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal sealed class InspectorHarness : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod(
        "BeginFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod(
        "EndFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo DrawWindow = typeof(EditorWindow).GetMethod(
        "OnGUIInternal", Members)!;
    private static readonly MethodInfo OpenWindow = typeof(EditorWindow).GetMethod(
        "OpenInternal", Members)!;
    private static readonly MethodInfo CloseWindow = typeof(EditorWindow).GetMethod(
        "CloseInternal", Members)!;
    private readonly object _application;
    private readonly Type _applicationType;
    private readonly EditorWindow _window;
    private readonly Scene _fallbackScene = new("Inspector Harness");

    internal InspectorHarness(GameObject target, ProjectWorkspace workspace)
    {
        GUIUtility.hotControl = 0;
        GUIUtility.keyboardControl = 0;
        _applicationType = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.GpuEditorApplication", throwOnError: true)!;
        _application = RuntimeHelpers.GetUninitializedObject(_applicationType);
        InitializeField("_scriptSourceCache");
        SetField("_workspace", workspace);
        SetField("_scene", _fallbackScene);
        SetField("_selected", target);

        var openSceneType = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.EditorOpenScene", throwOnError: true)!;
        SetField("_openScenes", Activator.CreateInstance(typeof(List<>).MakeGenericType(openSceneType))!);
        var registryType = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.MenuItemRegistry", throwOnError: true)!;
        var emptyRegistry = registryType.GetMethod("Empty", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, null)!;
        SetField("_menuItems", emptyRegistry);

        var inspectorType = _applicationType.GetNestedType("ImGuiInspectorWindow", BindingFlags.NonPublic) ??
                            throw new TypeLoadException("GpuEditorApplication.ImGuiInspectorWindow");
        _window = (EditorWindow)(Activator.CreateInstance(inspectorType, Members, binder: null,
            [_application], culture: null) ??
                                 throw new InvalidOperationException("Could not create the Inspector window."));
        SetField("_inspector", _window);
        OpenWindow.Invoke(_window, null);
    }

    internal IReadOnlyList<GpuCanvasCommand> Render(Event evt)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [evt, 640, 640, commands]);
        try { DrawWindow.Invoke(_window, null); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    internal IReadOnlyList<GpuCanvasCommand> Repaint()
    {
        Render(new Event(EventType.Layout));
        return Render(new Event(EventType.Repaint));
    }

    internal void Click(GpuCanvasCommand command, int clickCount = 1) =>
        Click(Center(command.Rect), clickCount);

    internal void Click(Vector2 point, int clickCount = 1)
    {
        MouseDown(point, clickCount);
        MouseUp(point, clickCount);
    }

    internal void MouseDown(Vector2 point, int clickCount = 1) => Render(new Event(EventType.MouseDown)
        { mousePosition = point, button = 0, clickCount = clickCount });

    internal void MouseUp(Vector2 point, int clickCount = 1) => Render(new Event(EventType.MouseUp)
        { mousePosition = point, button = 0, clickCount = clickCount });

    internal void ContextClick(GpuCanvasCommand command) => Render(new Event(EventType.ContextClick)
        { mousePosition = Center(command.Rect), button = 1, clickCount = 1 });

    internal void SendKey(KeyCode key, EventModifiers modifiers = EventModifiers.None, char character = '\0') =>
        Render(new Event(EventType.KeyDown)
            { keyCode = key, modifiers = modifiers, character = character });

    internal string? FindScriptSource(InspectorActionProbe component) =>
        _applicationType.GetMethod("FindScriptSource", Members)!.Invoke(_application, [component]) as string;

    internal int CollapsedComponentCount
    {
        get
        {
            var value = _window.GetType().GetField("_collapsedComponents", Members)!.GetValue(_window)!;
            return (int)value.GetType().GetProperty("Count")!.GetValue(value)!;
        }
    }

    internal int HotControl => GUIUtility.hotControl;

    internal static Vector2 CommandCenter(GpuCanvasCommand command) => Center(command.Rect);

    public void Dispose()
    {
        try { CloseWindow.Invoke(_window, null); }
        catch (TargetInvocationException) { }
        if (_fallbackScene.isCreated) _fallbackScene.Dispose();
        GUIUtility.hotControl = 0;
        GUIUtility.keyboardControl = 0;
    }

    private void SetField(string name, object value) =>
        _applicationType.GetField(name, Members)!.SetValue(_application, value);

    private void InitializeField(string name)
    {
        var field = _applicationType.GetField(name, Members) ??
                    throw new MissingFieldException(_applicationType.FullName, name);
        var value = Activator.CreateInstance(field.FieldType) ??
                    throw new InvalidOperationException($"Could not initialize test field {name}.");
        field.SetValue(_application, value);
    }

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));
}
