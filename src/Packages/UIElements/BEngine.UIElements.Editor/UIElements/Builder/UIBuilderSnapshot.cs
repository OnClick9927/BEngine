namespace BEngine.UIElements.Editor;

internal sealed record UIBuilderSnapshot(
    VisualElement Root,
    int[] SelectionPath,
    long Revision);
