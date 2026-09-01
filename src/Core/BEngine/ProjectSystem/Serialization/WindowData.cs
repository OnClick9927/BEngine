namespace BEngine.ProjectSystem;

public sealed class WindowData
{
    public string Title { get; set; } = "BEngine Game";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool VSync { get; set; } = true;
}
