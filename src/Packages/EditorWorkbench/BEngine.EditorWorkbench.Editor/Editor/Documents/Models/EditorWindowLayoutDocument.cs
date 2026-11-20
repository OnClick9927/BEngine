namespace BEngine.Editor.Documents;

public sealed class EditorWindowLayoutDocument
{
    public string Id { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string State { get; set; } = nameof(EditorWindowState.Normal);
    public bool Docked { get; set; } = true;
    public bool Locked { get; set; }
    public string? LockContext { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 480;
    public float Height { get; set; } = 320;
    public string PreferredDockArea { get; set; } = nameof(DockArea.Center);
    public string? PreviousPanelId { get; set; }
    public string? NextPanelId { get; set; }
    public int PanelIndex { get; set; }
    public float DockX { get; set; }
    public float DockY { get; set; }
    public bool WasMaximized { get; set; }
}
