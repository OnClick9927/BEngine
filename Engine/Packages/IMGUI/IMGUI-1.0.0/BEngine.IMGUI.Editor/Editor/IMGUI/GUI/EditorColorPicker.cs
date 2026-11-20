namespace BEngine.Editor;

internal static class EditorColorPicker
{
    private static readonly Dictionary<int, Color> PendingValues = [];

    internal static void Open(int controlToken, Color value, bool showAlpha, bool hdr)
    {
        EditorColorPickerWindow.Open(value, showAlpha, hdr, picked =>
        {
            PendingValues[controlToken] = picked;
            EditorApplication.QueuePlayerLoopUpdate();
        });
    }

    internal static void Pick(int controlToken, Color value, bool showAlpha)
    {
        EditorColorEyedropper.Begin(sampled =>
        {
            PendingValues[controlToken] = showAlpha
                ? sampled
                : new Color(sampled.r, sampled.g, sampled.b, value.a);
            EditorApplication.QueuePlayerLoopUpdate();
        });
    }

    internal static bool TryConsume(int controlToken, out Color value) =>
        PendingValues.Remove(controlToken, out value);
}
