namespace BEngine.Editor.Documents;

public sealed class EditorWindowLayoutDocument
{
    public string Id { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string State { get; set; } = nameof(EditorWindowState.Normal);
    public bool Docked { get; set; } = true;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 480;
    public float Height { get; set; } = 320;
}
