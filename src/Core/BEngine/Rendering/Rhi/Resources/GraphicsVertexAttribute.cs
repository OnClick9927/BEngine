namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsVertexAttribute(int Location, int ComponentCount, int OffsetBytes)
{
    internal void Validate(int strideBytes)
    {
        if (Location < 0) throw new ArgumentOutOfRangeException(nameof(Location));
        if (ComponentCount is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(ComponentCount));
        if (OffsetBytes < 0 || OffsetBytes + ComponentCount * sizeof(float) > strideBytes)
            throw new ArgumentOutOfRangeException(nameof(OffsetBytes));
    }
}
