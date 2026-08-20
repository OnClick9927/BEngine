using BEngine.Editor;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

internal sealed class ExitGuiEditorWindow : EditorWindow
{
    internal int GuiCalls { get; private set; }

    protected override void OnGUI()
    {
        GuiCalls++;
        GUI.enabled = false;
        GUILayout.BeginHorizontal();
        GUIUtility.ExitGUI();
    }
}
