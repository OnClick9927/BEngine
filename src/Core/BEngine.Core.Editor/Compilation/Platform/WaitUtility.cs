namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IEditorWaitService through dependency injection.</summary>
public sealed class WaitUtility
{
    private readonly IEditorWaitService _implementation;

    public WaitUtility(IEditorWaitService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static WaitUtility Current => BEngine.Editor.EditorServices.GetRequiredService<WaitUtility>();

    public void Wait(TimeSpan duration) =>
        _implementation.Wait(duration);
}
