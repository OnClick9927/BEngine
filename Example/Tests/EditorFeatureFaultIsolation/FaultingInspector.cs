using System.Runtime.CompilerServices;
using BEngine;
using BEngine.Editor;
using InspectorEditor = BEngine.Editor.Editor;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

internal sealed class FaultingInspector : InspectorEditor
{
    internal static int EnableCalls { get; private set; }
    internal static int GuiCalls { get; private set; }
    internal static int DisableCalls { get; private set; }

    protected override void OnEnable()
    {
        EnableCalls++;
        ThrowFromInspectorEnable();
    }

    public override void OnInspectorGUI()
    {
        GuiCalls++;
        GUI.enabled = false;
        GUILayout.BeginHorizontal();
        GUI.BeginScrollView(new Rect(0, 0, 180, 60), Vector2.zero,
            new Rect(0, 0, 360, 200));
        ThrowFromInspectorOnGui();
    }

    protected override void OnDisable()
    {
        DisableCalls++;
        ThrowFromInspectorDisable();
    }

    internal static void Reset()
    {
        EnableCalls = 0;
        GuiCalls = 0;
        DisableCalls = 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromInspectorEnable() =>
        throw new InvalidOperationException("INSPECTOR_ENABLE_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromInspectorOnGui() =>
        throw new InvalidOperationException("INSPECTOR_GUI_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromInspectorDisable() =>
        throw new InvalidOperationException("INSPECTOR_DISABLE_SENTINEL");
}
