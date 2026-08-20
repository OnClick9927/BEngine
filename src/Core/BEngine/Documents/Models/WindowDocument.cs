namespace BEngine.Documents;

public sealed class WindowDocument : Document
{
    public string Title { get; set; } = "BEngine Game";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool VSync { get; set; } = true;
}
