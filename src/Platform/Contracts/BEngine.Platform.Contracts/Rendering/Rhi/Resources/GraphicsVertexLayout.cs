namespace BEngine.Rendering.Rhi;

public sealed class GraphicsVertexLayout
{
    private readonly IReadOnlyList<GraphicsVertexAttribute> _attributes;

    public int StrideBytes { get; }
    public IReadOnlyList<GraphicsVertexAttribute> Attributes => _attributes;

    public GraphicsVertexLayout(int strideBytes, IEnumerable<GraphicsVertexAttribute> attributes)
    {
        if (strideBytes <= 0 || strideBytes % sizeof(float) != 0)
            throw new ArgumentOutOfRangeException(nameof(strideBytes));
        ArgumentNullException.ThrowIfNull(attributes);
        var copiedAttributes = attributes.ToArray();
        if (copiedAttributes.Length == 0) throw new ArgumentException("A vertex layout requires attributes.", nameof(attributes));
        foreach (var attribute in copiedAttributes) attribute.Validate(strideBytes);
        if ((((global::System.Collections.Generic.IEnumerable<int>)(copiedAttributes.Select(item => item.Location))) switch
        {
            int[] { Length: 0 } => 0,
            var distinctItems843 => distinctItems843.ToHashSet<int>().Count
        }) != copiedAttributes.Length)
            throw new ArgumentException("Vertex attribute locations must be unique.", nameof(attributes));
        StrideBytes = strideBytes;
        _attributes = Array.AsReadOnly(copiedAttributes);
    }
}
