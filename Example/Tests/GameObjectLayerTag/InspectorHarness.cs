using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.GameObjectLayerTag;

internal sealed class InspectorHarness : IDisposable
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod(
        "BeginFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod(
        "EndFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo DrawWindow = typeof(EditorWindow).GetMethod(
        "OnGUIInternal", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CloseWindow = typeof(EditorWindow).GetMethod(
        "CloseInternal", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly EditorWindow _window;

    public InspectorHarness(GameObject target)
    {
        var applicationType = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.GpuEditorApplication", throwOnError: true)!;
        var application = RuntimeHelpers.GetUninitializedObject(applicationType);
        applicationType.GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(application, target);
        var inspectorType = applicationType.GetNestedType("ImGuiInspectorWindow", BindingFlags.NonPublic) ??
                            throw new TypeLoadException("GpuEditorApplication.ImGuiInspectorWindow");
        _window = (EditorWindow)(Activator.CreateInstance(inspectorType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, [application], culture: null) ??
                                 throw new InvalidOperationException("Could not create the Inspector window."));
    }

    public IReadOnlyList<GpuCanvasCommand> Render(Event evt)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [evt, 520, 420, commands]);
        try { DrawWindow.Invoke(_window, null); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    public void Click(Vector2 point)
    {
        Render(new Event(EventType.MouseDown) { mousePosition = point, button = 0 });
        Render(new Event(EventType.MouseUp) { mousePosition = point, button = 0 });
    }

    public void Dispose()
    {
        try { CloseWindow.Invoke(_window, null); }
        catch (TargetInvocationException) { }
    }
}
