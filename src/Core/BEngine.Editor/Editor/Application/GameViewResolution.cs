namespace BEngine.Editor;

internal sealed record GameViewResolution(
    string Id,
    string Name,
    int Width,
    int Height,
    string Category,
    bool IsBuiltIn)
{
    public bool IsFreeAspect => Width <= 0 || Height <= 0;
    public string SizeLabel => IsFreeAspect ? "Free Aspect" : $"{Width} x {Height}";
    public string MenuLabel => IsFreeAspect ? Name : $"{Name} ({SizeLabel})";
}
