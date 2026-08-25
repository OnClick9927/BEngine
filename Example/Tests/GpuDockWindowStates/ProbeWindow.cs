using BEngine.Editor;

namespace BEngine.ExampleTests.GpuDockWindowStates;

internal sealed class ProbeWindow : EditorWindow
{
    public ProbeWindow(string title, string icon = "") => titleContent = new GUIContent(title, icon, string.Empty);
}
