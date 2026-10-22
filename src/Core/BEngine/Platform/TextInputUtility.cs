namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies ITextInputService through dependency injection.</summary>
public sealed class TextInputUtility
{
    private readonly ITextInputService _implementation;

    public TextInputUtility(ITextInputService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static TextInputUtility Current => RuntimeServiceAccess.GetRequiredService<TextInputUtility>();

    public void SetCaret(IntPtr window, Rect? caret) =>
        _implementation.SetCaret(window, caret);

    public void ReleaseWindow(IntPtr window) => _implementation.ReleaseWindow(window);

    public bool IsComposing(IntPtr window) =>
        _implementation.IsComposing(window);
    public string GetComposition(IntPtr window) => _implementation.GetComposition(window);
}
