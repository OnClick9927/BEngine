using BEngine.Editor;

namespace BEngine.ExampleTests.EditorLayoutPersistence;

internal sealed class LayoutProbeWindow : EditorWindow
{
    public LayoutProbeWindow(string title) => titleContent = new GUIContent(title);

    internal string? LockContext { get; set; }
    internal override bool supportsLocking => true;
    internal override string? CaptureLockContext() => LockContext;
    internal override void RestoreLockContext(string? context) => LockContext = context;
}
