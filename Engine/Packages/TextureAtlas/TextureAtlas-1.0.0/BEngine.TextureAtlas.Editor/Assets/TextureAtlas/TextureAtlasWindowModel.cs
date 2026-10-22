namespace BEngine.Editor;

public sealed class TextureAtlasWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public TextureAtlas Atlas { get; internal set; } = new();
    public string AssetPath { get; internal set; } = string.Empty;
    public bool IsDirty { get; internal set; }
    public string Message { get; internal set; } = string.Empty;
    public Sprite? PendingSource { get; internal set; }

    public string StatusText =>
        $"{(IsDirty ? "Modified" : "Saved")} | " +
        $"{(string.IsNullOrWhiteSpace(AssetPath) ? "New atlas" : AssetPath)}" +
        (string.IsNullOrWhiteSpace(Message) ? string.Empty : $" | {Message}");
}
