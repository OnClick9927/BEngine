namespace BEngine.Rendering.Rhi;

/// <summary>Cumulative geometry submitted through a graphics device.</summary>
public readonly record struct GraphicsDrawStatistics(
    long DrawCallCount,
    long VertexCount,
    long TriangleCount,
    long LineCount)
{
    public static GraphicsDrawStatistics operator -(
        GraphicsDrawStatistics current,
        GraphicsDrawStatistics previous) => new(
        Math.Max(0, current.DrawCallCount - previous.DrawCallCount),
        Math.Max(0, current.VertexCount - previous.VertexCount),
        Math.Max(0, current.TriangleCount - previous.TriangleCount),
        Math.Max(0, current.LineCount - previous.LineCount));

    internal GraphicsDrawStatistics AddDraw(int vertexCount, GraphicsPrimitiveTopology topology) => new(
        DrawCallCount + 1,
        VertexCount + vertexCount,
        TriangleCount + (topology == GraphicsPrimitiveTopology.TriangleList ? vertexCount / 3 : 0),
        LineCount + (topology == GraphicsPrimitiveTopology.LineList ? vertexCount / 2 : 0));
}
