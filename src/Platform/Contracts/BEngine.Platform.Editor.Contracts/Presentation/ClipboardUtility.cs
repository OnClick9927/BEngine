namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IClipboardService through dependency injection.</summary>
public sealed class ClipboardUtility
{
    private readonly IClipboardService _implementation;

    public ClipboardUtility(IClipboardService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static ClipboardUtility Current => EditorServices.GetRequiredService<ClipboardUtility>();

    public bool TryGetText(out string text) =>
        _implementation.TryGetText(out text);

    public bool TrySetText(string text) =>
        _implementation.TrySetText(text);
}
