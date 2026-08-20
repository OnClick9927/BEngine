using BEngine.Editor;

namespace BEngine.ExampleTests.EditorLayoutPersistence;

internal sealed class LayoutProbeWindow : EditorWindow
{
    public LayoutProbeWindow(string title) => titleContent = new GUIContent(title);
}
