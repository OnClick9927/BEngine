namespace BEngine.Platform;

public interface ITextInputService
{
    // The caret is in native window client coordinates; null ends text editing.
    void SetCaret(IntPtr window, Rect? caret);
    void ReleaseWindow(IntPtr window) => SetCaret(window, null);
    bool IsComposing(IntPtr window);
    string GetComposition(IntPtr window) => string.Empty;
}
