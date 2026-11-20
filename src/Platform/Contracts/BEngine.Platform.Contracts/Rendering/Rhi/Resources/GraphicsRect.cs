namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsRect(int X, int Y, int Width, int Height)
{
    /// <summary>Validates a clipping or viewport rectangle before submission to a graphics backend.</summary>
    public void Validate()
    {
        if (Width < 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height < 0) throw new ArgumentOutOfRangeException(nameof(Height));
    }
}
