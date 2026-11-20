namespace BEngine.Editor;

internal static class EditorObjectPickerPopupDispatcher
{
    internal static Action<EditorObjectPickerRequest>? Handler { get; set; }

    internal static bool Show(EditorObjectPickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Handler is not { } handler) return false;
        EditorCallbacks.Invoke("EditorObjectPickerPopupDispatcher.Show", () => handler(request));
        return true;
    }
}
