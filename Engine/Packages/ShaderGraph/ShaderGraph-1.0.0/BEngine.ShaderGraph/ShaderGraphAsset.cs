namespace BEngine.ShaderGraph;

public enum ShaderGraphSurface { Unlit, Lit2D }

public enum ShaderNodeKind
{
    Color, Scalar, Parameter, UV, VertexColor, Time, SampleTexture,
    Add, Subtract, Multiply, Divide, Lerp, OneMinus, Sine, Fraction, Saturate,
    Step, Smoothstep, Red, Green, Blue, Alpha, Output,
    TextureParameter, SubGraph, SubGraphInput, CustomFunction
}

public sealed class ShaderNode
{
    public string id { get; set; } = Guid.NewGuid().ToString("N");
    public ShaderNodeKind kind { get; set; }
    public Vector2 position { get; set; }
    public Color value { get; set; } = Color.white;
    public Fix64 number { get; set; } = 1;
    public string parameter { get; set; } = "Tint";
    public Texture? texture { get; set; }
    public ShaderGraphAsset? subGraph { get; set; }
    /// <summary>GLSL function body with vec4 arguments A, B, C, D and a vec4 return value.</summary>
    public string code { get; set; } = "return A;";
    public string codeHlsl { get; set; } = "";
    public string codeWgsl { get; set; } = "";
}

public sealed class ShaderConnection
{
    public string source { get; set; } = string.Empty;
    public string destination { get; set; } = string.Empty;
    public string input { get; set; } = "Color";
}

[CreateAssetMenu(fileName = "New Shader Graph", menuName = "Rendering/Shader Graph", order = 205)]
public sealed class ShaderGraphAsset : ScriptableObject
{
    public ShaderGraphSurface surface { get; set; }
    public List<ShaderNode> nodes { get; set; } =
    [
        new() { id = "texture", kind = ShaderNodeKind.SampleTexture, position = new(30, 50) },
        new() { id = "color", kind = ShaderNodeKind.VertexColor, position = new(30, 210) },
        new() { id = "multiply", kind = ShaderNodeKind.Multiply, position = new(280, 80) },
        new() { id = "output", kind = ShaderNodeKind.Output, position = new(530, 80) }
    ];
    public List<ShaderConnection> connections { get; set; } =
    [
        new() { source = "texture", destination = "multiply", input = "A" },
        new() { source = "color", destination = "multiply", input = "B" },
        new() { source = "multiply", destination = "output", input = "Color" }
    ];

    public int ContentHash()
    {
        var hash = new HashCode();
        var active = new HashSet<ShaderGraphAsset>(ReferenceEqualityComparer.Instance);
        var completed = new Dictionary<ShaderGraphAsset, int>(ReferenceEqualityComparer.Instance);
        hash.Add(Hash(this, 0));
        return hash.ToHashCode();

        int Hash(ShaderGraphAsset graph, int depth)
        {
            if (completed.TryGetValue(graph, out var cached)) return cached;
            if (depth > 16 || !active.Add(graph)) return -1; // The compiler reports the structural error.
            var content = new HashCode();
            content.Add(graph.surface);
            foreach (var node in graph.nodes ?? [])
            {
                content.Add(node?.id); content.Add(node?.kind); content.Add(node?.value);
                content.Add(node?.number); content.Add(node?.parameter); content.Add(node?.code);
                content.Add(node?.codeHlsl); content.Add(node?.codeWgsl);
                content.Add(node?.texture?.GetInstanceID()); content.Add(node?.subGraph?.GetInstanceID());
                if (node?.kind == ShaderNodeKind.SubGraph && node.subGraph is { isDestroyed: false } child)
                    content.Add(Hash(child, depth + 1));
            }
            foreach (var edge in graph.connections ?? [])
            { content.Add(edge?.source); content.Add(edge?.destination); content.Add(edge?.input); }
            active.Remove(graph);
            return completed[graph] = content.ToHashCode();
        }
    }
}
