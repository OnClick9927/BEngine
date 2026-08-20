using BEngine.Editor;

namespace BEngine.ExampleTests.GpuDockWindowStates;

internal sealed class ProbeWindow : EditorWindow
{
    public ProbeWindow(string title) => titleContent = new GUIContent(title);
}
