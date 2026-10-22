namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IEditorHostOutput through dependency injection.</summary>
public sealed class HostOutputUtility
{
    private readonly IEditorHostOutput _implementation;

    public HostOutputUtility(IEditorHostOutput implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static HostOutputUtility Current => BEngine.Editor.EditorServices.GetRequiredService<HostOutputUtility>();

    public void Beep() =>
        _implementation.Beep();
}
