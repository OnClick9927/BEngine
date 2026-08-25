using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using AssetEditor = BEngine.Editor.Editor;

namespace BEngine.ExampleTests.AssetPreview;

internal sealed class InspectorHarness : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod(
        "BeginFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod(
        "EndFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo DrawWindow = typeof(EditorWindow).GetMethod("OnGUIInternal", Members)!;
    private static readonly MethodInfo OpenWindow = typeof(EditorWindow).GetMethod("OpenInternal", Members)!;
    private static readonly MethodInfo CloseWindow = typeof(EditorWindow).GetMethod("CloseInternal", Members)!;
    private readonly MethodInfo _rebuildEditor;

    private readonly object _application;
    private readonly Type _applicationType;
    private readonly EditorWindow _window;

    internal InspectorHarness(BObject target, string assetPath)
    {
        GUIUtility.hotControl = 0;
        GUIUtility.keyboardControl = 0;
        _applicationType = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.GpuEditorApplication", throwOnError: true)!;
        _application = RuntimeHelpers.GetUninitializedObject(_applicationType);
        SetTarget(target, assetPath);

        var inspectorType = _applicationType.GetNestedType("ImGuiInspectorWindow", BindingFlags.NonPublic) ??
                            throw new TypeLoadException("GpuEditorApplication.ImGuiInspectorWindow");
        _window = (EditorWindow)(Activator.CreateInstance(inspectorType, Members, binder: null,
            [_application], culture: null) ??
                                 throw new InvalidOperationException("Could not create the Inspector window."));
        _rebuildEditor = inspectorType.GetMethod("RebuildEditor", Members) ??
                         throw new MissingMethodException(inspectorType.FullName, "RebuildEditor");
        SetField("_inspector", _window);
        OpenWindow.Invoke(_window, null);
    }

    internal AssetEditor CurrentEditor => (AssetEditor)(_window.GetType().GetField("_editor", Members)!
        .GetValue(_window) ?? throw new InvalidOperationException("Inspector did not create an Editor."));

    internal bool IsLocked
    {
        get => _window.isLocked;
        set => _window.isLocked = value;
    }

    internal void Select(BObject target, string assetPath)
    {
        SetField("_selected", null);
        SetField("_selectedAsset", target);
        SetField("_selectedAssetPath", assetPath);
    }

    internal IReadOnlyList<GpuCanvasCommand> Repaint(int width = 500, int height = 520)
    {
        Render(new Event(EventType.Layout), width, height);
        return Render(new Event(EventType.Repaint), width, height);
    }

    internal static bool IsEnabled(AssetEditor editor) =>
        (bool)typeof(AssetEditor).GetField("_enabled", Members)!.GetValue(editor)!;

    internal void CloseAndReopen()
    {
        CloseWindow.Invoke(_window, null);
        OpenWindow.Invoke(_window, null);
    }

    internal void RebuildEditor(bool force) => _rebuildEditor.Invoke(_window, [force]);

    public void Dispose()
    {
        try { CloseWindow.Invoke(_window, null); }
        catch (TargetInvocationException) { }
        GUIUtility.hotControl = 0;
        GUIUtility.keyboardControl = 0;
    }

    private IReadOnlyList<GpuCanvasCommand> Render(Event evt, int width, int height)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [evt, width, height, commands]);
        try { DrawWindow.Invoke(_window, null); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    private void SetTarget(BObject target, string assetPath)
    {
        ArgumentNullException.ThrowIfNull(target);
        Select(target, assetPath);
    }

    private void SetField(string name, object? value) =>
        _applicationType.GetField(name, Members)!.SetValue(_application, value);
}
