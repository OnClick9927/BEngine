using BEngine.Editor;
using InspectorEditor = BEngine.Editor.Editor;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

internal sealed class HealthyInspector : InspectorEditor
{
    internal static int EnableCalls { get; private set; }
    internal static int GuiCalls { get; private set; }
    internal static int DisableCalls { get; private set; }
    internal static bool GuiWasEnabled { get; private set; }

    protected override void OnEnable() => EnableCalls++;

    public override void OnInspectorGUI()
    {
        GuiCalls++;
        GuiWasEnabled = GUI.enabled;
        GUILayout.Label("HEALTHY_INSPECTOR_LABEL");
    }

    protected override void OnDisable() => DisableCalls++;

    internal static void Reset()
    {
        EnableCalls = 0;
        GuiCalls = 0;
        DisableCalls = 0;
        GuiWasEnabled = false;
    }
}
