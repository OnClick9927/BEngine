namespace BEngine.Rendering.Rhi;

public sealed class GraphicsMeshDescription
{
    public string Label { get; }
    public ReadOnlyMemory<float> Vertices { get; }
    public GraphicsVertexLayout Layout { get; }
    public GraphicsPrimitiveTopology Topology { get; }
    public GraphicsBufferUsage Usage { get; }
    public int VertexCount => Vertices.Length * sizeof(float) / Layout.StrideBytes;

    public GraphicsMeshDescription(
        string label,
        ReadOnlyMemory<float> vertices,
        GraphicsVertexLayout layout,
        GraphicsPrimitiveTopology topology = GraphicsPrimitiveTopology.TriangleList,
        GraphicsBufferUsage usage = GraphicsBufferUsage.Static)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(layout);
        if (vertices.Length * sizeof(float) % layout.StrideBytes != 0)
            throw new ArgumentException("Vertex data length must be a multiple of the layout stride.", nameof(vertices));
        Label = label;
        Vertices = vertices;
        Layout = layout;
        Topology = topology;
        Usage = usage;
    }
}
