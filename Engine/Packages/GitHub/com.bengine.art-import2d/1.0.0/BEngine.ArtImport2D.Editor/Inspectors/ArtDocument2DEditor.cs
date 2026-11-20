namespace BEngine.Editor;

[CustomEditor(typeof(ArtDocument2D))]
public sealed class ArtDocument2DEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        var document = (ArtDocument2D)target;
        if (document.SourceKind == ArtDocumentSourceKind.Photoshop && GUILayout.Button("Open Layered Art Editor")) ArtDocumentWindow.Open(document);
        GUILayout.Label($"{document.SourceKind} {document.CanvasWidth} x {document.CanvasHeight}", EditorStyles.boldLabel);
        GUILayout.Label($"{document.Layers.Count} layers, {document.Frames.Count} frames, {document.Tags.Count} tags");
        GUILayout.Label($"Source: {document.SourceBitDepth}-bit, {document.SourceMetadata.Count} metadata blocks, {document.Slices.Count} slices");
        foreach (var layer in document.Layers)
        {
            GUILayout.Label($"Layer: {layer.Name} ({layer.Width} x {layer.Height}) {(layer.Visible ? "visible" : "hidden")}");
            GUILayout.Label($"  Parent: {layer.ParentIndex}, blend: {layer.BlendMode}, precision channels: {layer.PrecisionChannels.Count}");
        }
        foreach (var tag in document.Tags)
            GUILayout.Label($"Tag: {tag.Name} [{tag.FromFrame}..{tag.ToFrame}] {tag.Direction}");
        foreach (var slice in document.Slices)
            GUILayout.Label($"Slice: {slice.Name}, {slice.Keys.Count} keys");
        foreach (var metadata in document.SourceMetadata.Take(64))
            GUILayout.Label($"Metadata: {metadata.Scope}, type {metadata.Type}, frame {metadata.Frame}");
    }
}
