namespace BEngine.Editor.Documents;

public sealed class EditorDockNodeDocument
{
    public string Type { get; set; } = "Group";
    public bool SideBySide { get; set; }
    public float Ratio { get; set; } = 0.5f;
    public List<string> Panels { get; set; } = [];
    public string? SelectedId { get; set; }
    public EditorDockNodeDocument? First { get; set; }
    public EditorDockNodeDocument? Second { get; set; }
}
