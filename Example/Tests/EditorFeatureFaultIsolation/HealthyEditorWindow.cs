using BEngine.Editor;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

internal sealed class HealthyEditorWindow : EditorWindow
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
    internal int ButtonClicks { get; private set; }
    internal bool GuiWasEnabled { get; private set; }

    protected override void OnEnable() => EnableCalls++;
    protected override void OnBecameVisible() => VisibleCalls++;
    protected override void OnFocus() => FocusCalls++;
    protected override void OnLostFocus() => LostFocusCalls++;
    protected override void Update() => UpdateCalls++;
    protected override void OnInspectorUpdate() => InspectorUpdateCalls++;
    protected override void OnSelectionChange() => SelectionCalls++;
    protected override void OnHierarchyChange() => HierarchyCalls++;
    protected override void OnProjectChange() => ProjectCalls++;
    protected override void OnBecameInvisible() => InvisibleCalls++;
    protected override void OnDisable() => DisableCalls++;

    protected override void OnGUI()
    {
        GuiCalls++;
        GuiWasEnabled = GUI.enabled;
        GUILayout.Label("HEALTHY_WINDOW_LABEL");
        if (GUILayout.Button("HEALTHY_WINDOW_BUTTON")) ButtonClicks++;
    }
}
