using System.Runtime.CompilerServices;
using BEngine;
using BEngine.Editor;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

internal sealed class FaultingEditorWindow : EditorWindow
{
    internal int EnableCalls { get; private set; }
    internal int VisibleCalls { get; private set; }
    internal int FocusCalls { get; private set; }
    internal int LostFocusCalls { get; private set; }
    internal int UpdateCalls { get; private set; }
    internal int InspectorUpdateCalls { get; private set; }
    internal int GuiCalls { get; private set; }
    internal int SelectionCalls { get; private set; }
    internal int HierarchyCalls { get; private set; }
    internal int ProjectCalls { get; private set; }
    internal int InvisibleCalls { get; private set; }
    internal int DisableCalls { get; private set; }

    protected override void OnEnable()
    {
        EnableCalls++;
        ThrowFromWindowEnable();
    }

    protected override void OnBecameVisible()
    {
        VisibleCalls++;
        ThrowFromWindowVisible();
    }

    protected override void OnFocus()
    {
        FocusCalls++;
        ThrowFromWindowFocus();
    }

    protected override void OnLostFocus()
    {
        LostFocusCalls++;
        ThrowFromWindowLostFocus();
    }

    protected override void Update()
    {
        UpdateCalls++;
        ThrowFromWindowUpdate();
    }

    protected override void OnInspectorUpdate()
    {
        InspectorUpdateCalls++;
        ThrowFromWindowInspectorUpdate();
    }

    protected override void OnGUI()
    {
        GuiCalls++;
        GUI.enabled = false;
        GUILayout.BeginHorizontal();
        GUI.BeginScrollView(new Rect(2, 2, 180, 80), Vector2.zero,
            new Rect(0, 0, 360, 240));
        ThrowFromWindowOnGui();
    }

    protected override void OnSelectionChange()
    {
        SelectionCalls++;
        ThrowFromWindowSelection();
    }

    protected override void OnHierarchyChange()
    {
        HierarchyCalls++;
        ThrowFromWindowHierarchy();
    }

    protected override void OnProjectChange()
    {
        ProjectCalls++;
        ThrowFromWindowProject();
    }

    protected override void OnBecameInvisible()
    {
        InvisibleCalls++;
        ThrowFromWindowInvisible();
    }

    protected override void OnDisable()
    {
        DisableCalls++;
        ThrowFromWindowDisable();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowEnable() =>
        throw new InvalidOperationException("WINDOW_ENABLE_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowVisible() =>
        throw new InvalidOperationException("WINDOW_VISIBLE_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowFocus() =>
        throw new InvalidOperationException("WINDOW_FOCUS_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowLostFocus() =>
        throw new InvalidOperationException("WINDOW_LOST_FOCUS_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowUpdate() =>
        throw new InvalidOperationException("WINDOW_UPDATE_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowInspectorUpdate() =>
        throw new InvalidOperationException("WINDOW_INSPECTOR_UPDATE_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowOnGui() =>
        throw new InvalidOperationException("WINDOW_GUI_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowSelection() =>
        throw new InvalidOperationException("WINDOW_SELECTION_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowHierarchy() =>
        throw new InvalidOperationException("WINDOW_HIERARCHY_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowProject() =>
        throw new InvalidOperationException("WINDOW_PROJECT_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowInvisible() =>
        throw new InvalidOperationException("WINDOW_INVISIBLE_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWindowDisable() =>
        throw new InvalidOperationException("WINDOW_DISABLE_SENTINEL");
}
