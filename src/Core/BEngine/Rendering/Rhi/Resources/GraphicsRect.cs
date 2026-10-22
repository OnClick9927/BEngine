namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsRect(int X, int Y, int Width, int Height)
{
    internal void Validate()
    {
        if (Width < 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height < 0) throw new ArgumentOutOfRangeException(nameof(Height));
    }
}
