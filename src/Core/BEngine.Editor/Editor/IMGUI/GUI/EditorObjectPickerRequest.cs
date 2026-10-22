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
