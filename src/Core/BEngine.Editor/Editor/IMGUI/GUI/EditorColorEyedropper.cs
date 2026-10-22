namespace BEngine.Editor;

internal static class EditorColorEyedropper
{
    private const KeyCode LeftButton = KeyCode.Mouse0;
    private const KeyCode EscapeKey = KeyCode.Escape;
    private static Action<Color>? _picked;
    private static bool _waitingForRelease;

    internal static bool active => _picked is not null;

    internal static void Begin(Action<Color> picked)
    {
        ArgumentNullException.ThrowIfNull(picked);
        Cancel();
        _picked = picked;
        _waitingForRelease = DesktopUtility.Current.IsKeyPressed(LeftButton);
        EditorApplication.update += Poll;
    }

    internal static void Cancel()
    {
        EditorApplication.update -= Poll;
        _picked = null;
        _waitingForRelease = false;
    }

    private static void Poll()
    {
        if (_picked is null) return;
        if (DesktopUtility.Current.IsKeyPressed(EscapeKey))
        {
            Cancel();
            return;
        }
        var pressed = DesktopUtility.Current.IsKeyPressed(LeftButton);
        if (_waitingForRelease)
        {
            if (!pressed) _waitingForRelease = false;
            return;
        }
        if (!pressed || !DesktopUtility.Current.TrySamplePointerColor(out var color)) return;
        var callback = _picked;
        Cancel();
        if (callback is not null)
            EditorFeatureGuard.Invoke("ColorEyedropper.Picked", () => callback(color));
    }

}
