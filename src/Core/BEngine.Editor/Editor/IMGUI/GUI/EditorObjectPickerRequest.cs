namespace BEngine.Editor;

internal sealed record EditorObjectPickerRequest(
    Rect Anchor,
    BObject? Current,
    Type ObjectType,
    bool AllowSceneObjects,
    BObject? SelectedObject,
    IReadOnlyList<EditorObjectPickerCandidate> SceneCandidates,
    IReadOnlyList<EditorObjectPickerCandidate> ProjectCandidates,
    Action<BObject?> Select);

internal sealed record EditorObjectPickerCandidate(BObject Value, string Path);

internal static class EditorObjectPickerPopupDispatcher
{
    internal static Action<EditorObjectPickerRequest>? Handler { get; set; }

    internal static bool Show(EditorObjectPickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Handler is not { } handler) return false;
        EditorFeatureGuard.Invoke("EditorObjectPickerPopupDispatcher.Show", () => handler(request));
        return true;
    }
}
