using System.Globalization;
using System.Text;
using BEngine.Rendering.Rhi;
using BEngine.Rendering;

namespace BEngine.ShaderGraph;

public sealed record ShaderGraphParameter(string Name, Color DefaultValue);
public sealed record ShaderGraphTextureParameter(string Name, Texture? DefaultTexture, int Slot);
public sealed record CompiledShaderGraph(GraphicsShaderProgramDescription Program, IReadOnlyList<ShaderGraphParameter> Parameters)
{
    public IReadOnlyList<ShaderGraphTextureParameter> Textures { get; init; } = [];
    public int LightingCapacity { get; init; }
    public int CookieFirstSlot { get; init; }
    public int CookieCount { get; init; }
}

/// <summary>Compiles acyclic float4 graphs and reusable subgraphs for the desktop GPU backends.</summary>
public static class ShaderGraphCompiler
{
    public static string[] Inputs(ShaderNodeKind kind) => kind switch
    {
        ShaderNodeKind.Output => ["Color", "Alpha", "Normal"],
        ShaderNodeKind.SampleTexture or ShaderNodeKind.TextureParameter => ["UV"],
        ShaderNodeKind.SubGraph or ShaderNodeKind.CustomFunction => ["A", "B", "C", "D"],
        ShaderNodeKind.Add or ShaderNodeKind.Subtract or ShaderNodeKind.Multiply or ShaderNodeKind.Divide or ShaderNodeKind.Step => ["A", "B"],
        ShaderNodeKind.Lerp => ["A", "B", "T"],
        ShaderNodeKind.Smoothstep => ["Min", "Max", "Value"],
        ShaderNodeKind.OneMinus or ShaderNodeKind.Sine or ShaderNodeKind.Fraction or ShaderNodeKind.Saturate or
            ShaderNodeKind.Red or ShaderNodeKind.Green or ShaderNodeKind.Blue or ShaderNodeKind.Alpha => ["In"],
        _ => []
    };

    public static CompiledShaderGraph Compile(ShaderGraphAsset graph, GraphicsBackend backend) => CompilePreview(graph, backend, null);

    /// <param name="previewNodeId">Optional root node to render independently; the asset is never changed.</param>
    public static CompiledShaderGraph CompilePreview(ShaderGraphAsset graph, GraphicsBackend backend, string? previewNodeId)
        => CompileSurface(graph, backend, previewNodeId, 32, 0);

    public static CompiledShaderGraph CompileSurface(ShaderGraphAsset graph, GraphicsBackend backend, string? previewNodeId, int lightingCapacity, int cookieCount, string? lightingFunctions = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (backend is not (GraphicsBackend.OpenGL or GraphicsBackend.Vulkan or GraphicsBackend.OpenGLES or GraphicsBackend.WebGL or GraphicsBackend.Metal or GraphicsBackend.Direct3D11 or GraphicsBackend.Direct3D12 or GraphicsBackend.WebGPU))
            throw new NotSupportedException($"Shader Graph requires a supported rendering backend; {backend} has no registered graph shader compiler.");
        if (!Enum.IsDefined(graph.surface)) throw new InvalidDataException("Unknown graph surface type.");
        return new Compilation(backend, previewNodeId is null && graph.surface == ShaderGraphSurface.Lit2D ? lightingCapacity : 0, cookieCount, lightingFunctions).Build(graph, previewNodeId);
    }

    private sealed class Compilation(GraphicsBackend backend, int lightingCapacity, int cookieCount, string? evaluationFunctions)
    {
        private readonly List<ShaderGraphParameter> _parameters = [];
        private readonly List<ShaderGraphTextureParameter> _textures = [];
        private readonly HashSet<ShaderGraphAsset> _activeGraphs = new(ReferenceEqualityComparer.Instance);
        private readonly StringBuilder _body = new(), _functions = new();
        private int _nodeCount, _variables, _functionCount;
        private string _normal = "vec4(0.5, 0.5, 1.0, 1.0)";
        // Metal consumes the same explicit bindings through the host's SPIR-V cross compiler.
        private bool Vulkan => backend is GraphicsBackend.Vulkan or GraphicsBackend.Metal;

        public CompiledShaderGraph Build(ShaderGraphAsset graph, string? preview)
        {
            var output = EmitGraph(graph, null, 0, preview);
            _body.Append("    fragColor = ").Append(output).AppendLine(";");
            if (lightingCapacity > 0)
                _body.Append("    fragColor.rgb = clamp(fragColor.rgb * evaluateLighting(vPosition, lightingNormal(").Append(_normal).AppendLine(".rgb)), 0.0, 1.0);");
            var capacity = Math.Max(1, _parameters.Count);
            var cookieFirstSlot = 1 + _textures.Count;
            var textureCount = cookieFirstSlot + (lightingCapacity > 0 ? cookieCount : 0);
            if (textureCount > 8) throw new InvalidDataException("Lit Shader Graph exceeds eight texture slots: Sprite, graph textures and Sprite light textures share the budget.");
            if (16 + (capacity + lightingCapacity) * 16 > 65536) throw new InvalidDataException("Shader Graph lighting exceeds the 64 KiB uniform budget.");
            var lightingUniform = lightingCapacity > 0 ? $"vec4 uLighting[{lightingCapacity}];" : string.Empty;
            var uniforms = Vulkan
                ? $"layout(set=0,binding=0) uniform GraphParameters {{ float uViewportWidth; float uViewportHeight; float uTime; float padding; vec4 uParameters[{capacity}]; {lightingUniform} }};\n"
                : $"uniform float uViewportWidth; uniform float uViewportHeight; uniform float uTime; uniform vec4 uParameters[{capacity}];\n" + (lightingCapacity > 0 ? "uniform " + lightingUniform + "\n" : "");
            var version = Vulkan ? "#version 450\n" : backend is GraphicsBackend.OpenGLES or GraphicsBackend.WebGL
                ? "#version 300 es\nprecision highp float;\nprecision highp int;\n" : "#version 330 core\n";
            var vertex = version + uniforms + """
                layout(location=0) in vec2 aPosition;
                layout(location=1) in vec4 aColor;
                layout(location=2) in vec2 aTexCoord;
                """ + (Vulkan ? "\nlayout(location=0) out vec4 vColor; layout(location=1) out vec2 vTexCoord; layout(location=2) out vec2 vPosition;\n" : "\nout vec4 vColor; out vec2 vTexCoord; out vec2 vPosition;\n") + """
                void main() {
                    vec2 p = aPosition / vec2(uViewportWidth, uViewportHeight);
                    gl_Position = vec4(p.x * 2.0 - 1.0, 1.0 - p.y * 2.0, 0.0, 1.0);
                    vColor = aColor; vTexCoord = aTexCoord; vPosition = aPosition;
                }
                """;
            var samplers = new StringBuilder();
            for (var i = 0; i < textureCount; i++)
                samplers.Append(Vulkan
                    ? $"layout(set=0,binding={1 + i * 2}) uniform texture2D GraphTexture{i}; layout(set=0,binding={2 + i * 2}) uniform sampler GraphSampler{i};\n"
                    : $"uniform sampler2D uTexture{i};\n");
            var lightingFunctions = new StringBuilder();
            if (lightingCapacity > 0)
            {
                lightingFunctions.AppendLine("vec4 sampleCookie(int index, vec2 uv) {");
                for (var i = 0; i < cookieCount; i++)
                    lightingFunctions.Append("    if (index == ").Append(i).Append(") return ").Append(Sample(cookieFirstSlot + i, "vec4(uv, 0.0, 1.0)")).AppendLine(";");
                lightingFunctions.AppendLine("    return vec4(1.0);\n}");
                lightingFunctions.AppendLine(evaluationFunctions ?? LightingShaderContext2D.ShaderFunctions);
            }
            var fragment = version + uniforms + (Vulkan
                ? "layout(location=0) in vec4 vColor; layout(location=1) in vec2 vTexCoord; layout(location=2) in vec2 vPosition;\n"
                : "in vec4 vColor; in vec2 vTexCoord; in vec2 vPosition;\n") + samplers +
                "layout(location=0) out vec4 fragColor;\n" + lightingFunctions + _functions + "void main() {\n" + _body + "}\n";
            var bindings = new List<GraphicsUniformBinding> { new("uViewportWidth", 0, 1), new("uViewportHeight", 4, 1), new("uTime", 8, 1) };
            for (var i = 0; i < capacity; i++) bindings.Add(new($"uParameters[{i}]", 16 + i * 16, 4));
            for (var i = 0; i < lightingCapacity; i++) bindings.Add(new($"uLighting[{i}]", 16 + (capacity + i) * 16, 4));
            var language = GraphicsShaderLanguage.Glsl;
            if (backend is GraphicsBackend.Direct3D11 or GraphicsBackend.Direct3D12)
            {
                (vertex, fragment) = ShaderGraphPortableSources.Hlsl(capacity, lightingCapacity, textureCount, lightingFunctions.ToString(), _functions.ToString(), _body.ToString());
                language = GraphicsShaderLanguage.Hlsl;
            }
            else if (backend == GraphicsBackend.WebGPU)
            {
                (vertex, fragment) = ShaderGraphPortableSources.Wgsl(capacity, lightingCapacity, textureCount, cookieFirstSlot, cookieCount, _functions.ToString(), _body.ToString());
                language = GraphicsShaderLanguage.Wgsl;
            }
            return new(new GraphicsShaderProgramDescription("ShaderGraph.Texture/" + graph.name,
                new(GraphicsShaderStage.Vertex, language, vertex),
                new(GraphicsShaderStage.Fragment, language, fragment))
            { UniformBufferSizeBytes = 16 + (capacity + lightingCapacity) * 16, Uniforms = bindings, SampledTextureCount = textureCount }, _parameters)
            { Textures = _textures, LightingCapacity = lightingCapacity, CookieFirstSlot = cookieFirstSlot, CookieCount = lightingCapacity > 0 ? cookieCount : 0 };
        }

        private string EmitGraph(ShaderGraphAsset graph, Dictionary<string, string>? arguments, int nesting, string? preview = null)
        {
            if (graph.isDestroyed) throw new InvalidDataException("A referenced Shader Graph was destroyed.");
            if (nesting > 16) throw new InvalidDataException("Shader Graph exceeds 16 nested subgraphs.");
            if (!_activeGraphs.Add(graph)) throw new InvalidDataException("Shader Graph contains a recursive subgraph reference.");
            try
            {
                if (graph.nodes is null || graph.connections is null || graph.nodes.Count is < 1 or > 512 || graph.connections.Count > 2048)
                    throw new InvalidDataException("Shader Graph requires 1–512 nodes and at most 2048 connections.");
                _nodeCount += graph.nodes.Count;
                if (_nodeCount > 4096) throw new InvalidDataException("Expanded Shader Graph exceeds 4096 nodes.");
                var nodes = new Dictionary<string, ShaderNode>(StringComparer.Ordinal);
                foreach (var node in graph.nodes)
                    if (node is null || string.IsNullOrWhiteSpace(node.id) || !Enum.IsDefined(node.kind) || !nodes.TryAdd(node.id, node))
                        throw new InvalidDataException("Shader Graph has a missing, duplicate or invalid node.");
                var outputs = graph.nodes.Where(node => node.kind == ShaderNodeKind.Output).ToArray();
                if (outputs.Length != 1) throw new InvalidDataException("Shader Graph requires exactly one Output node.");
                if (preview is not null && !nodes.ContainsKey(preview)) throw new InvalidDataException("The preview node no longer exists.");
                var edges = new Dictionary<(string Node, string Port), string>();
                foreach (var edge in graph.connections)
                    if (edge is null || string.IsNullOrEmpty(edge.source) || string.IsNullOrEmpty(edge.destination) ||
                        !nodes.TryGetValue(edge.source, out var source) || !nodes.TryGetValue(edge.destination, out var destination) ||
                        source.kind == ShaderNodeKind.Output || !Inputs(destination.kind).Contains(edge.input, StringComparer.Ordinal) ||
                        !edges.TryAdd((edge.destination, edge.input), edge.source))
                        throw new InvalidDataException("Shader Graph has a dangling connection, invalid port or multiple connections to one input.");
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var active = new HashSet<string>(StringComparer.Ordinal);
                var ordered = new List<ShaderNode>();
                void Visit(ShaderNode node, int depth)
                {
                    if (active.Contains(node.id)) throw new InvalidDataException($"Shader Graph cycle at node '{node.id}'.");
                    if (visited.Contains(node.id)) return;
                    if (depth > 128) throw new InvalidDataException("Shader Graph exceeds 128 nested operations.");
                    active.Add(node.id);
                    foreach (var port in Inputs(node.kind))
                        if (edges.TryGetValue((node.id, port), out var source)) Visit(nodes[source], depth + 1);
                    active.Remove(node.id); visited.Add(node.id); ordered.Add(node);
                }
                foreach (var node in graph.nodes.OrderBy(item => item.id, StringComparer.Ordinal)) Visit(node, 0);
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                string Input(ShaderNode node, string port, string fallback = "vec4(0.0)") =>
                    edges.TryGetValue((node.id, port), out var source) ? names[source] : fallback;
                foreach (var node in ordered)
                {
                    if (node.kind == ShaderNodeKind.Output) continue;
                    var a = Input(node, "A"); var b = Input(node, "B", "vec4(1.0)"); var x = Input(node, "In");
                    var expression = node.kind switch
                    {
                        ShaderNodeKind.Color => ColorLiteral(node.value),
                        ShaderNodeKind.Scalar => "vec4(" + Number(node.number) + ")",
                        ShaderNodeKind.Parameter => Parameter(node),
                        ShaderNodeKind.UV => "vec4(vTexCoord, 0.0, 1.0)",
                        ShaderNodeKind.VertexColor => "vColor",
                        ShaderNodeKind.Time => "vec4(uTime)",
                        ShaderNodeKind.SampleTexture => Sample(0, Input(node, "UV", "vec4(vTexCoord, 0.0, 1.0)")),
                        ShaderNodeKind.TextureParameter => Sample(TextureParameter(node), Input(node, "UV", "vec4(vTexCoord, 0.0, 1.0)")),
                        ShaderNodeKind.SubGraph => EmitGraph(node.subGraph ?? throw new InvalidDataException($"SubGraph '{node.id}' needs an asset."),
                            Inputs(node.kind).Where(port => edges.ContainsKey((node.id, port))).ToDictionary(port => port, port => Input(node, port), StringComparer.Ordinal), nesting + 1),
                        ShaderNodeKind.SubGraphInput => SubGraphInput(node, arguments),
                        ShaderNodeKind.CustomFunction => CustomFunction(node) + $"({a}, {b}, {Input(node, "C")}, {Input(node, "D")})",
                        ShaderNodeKind.Add => $"({a} + {b})",
                        ShaderNodeKind.Subtract => $"({a} - {b})",
                        ShaderNodeKind.Multiply => $"({a} * {b})",
                        ShaderNodeKind.Divide => $"({a} / (max(abs({b}), vec4(0.000001)) * mix(vec4(-1.0), vec4(1.0), step(vec4(0.0), {b}))))",
                        ShaderNodeKind.Lerp => $"mix({a}, {b}, {Input(node, "T", "vec4(0.5)")})",
                        ShaderNodeKind.OneMinus => $"(vec4(1.0) - {x})",
                        ShaderNodeKind.Sine => $"sin({x})",
                        ShaderNodeKind.Fraction => $"fract({x})",
                        ShaderNodeKind.Saturate => $"clamp({x}, vec4(0.0), vec4(1.0))",
                        ShaderNodeKind.Step => $"step({a}, {b})",
                        ShaderNodeKind.Smoothstep => $"smoothstep({Input(node, "Min")}, {Input(node, "Max", "vec4(1.0)")}, {Input(node, "Value")})",
                        ShaderNodeKind.Red => $"vec4({x}.r)", ShaderNodeKind.Green => $"vec4({x}.g)",
                        ShaderNodeKind.Blue => $"vec4({x}.b)", ShaderNodeKind.Alpha => $"vec4({x}.a)",
                        _ => throw new InvalidDataException("Unsupported graph node.")
                    };
                    var variable = "n" + (_variables++).ToString(CultureInfo.InvariantCulture);
                    names[node.id] = variable;
                    _body.Append("    vec4 ").Append(variable).Append(" = ").Append(expression).AppendLine(";");
                }
                if (preview is not null && nodes[preview].kind != ShaderNodeKind.Output) return names[preview];
                if (nesting == 0) _normal = Input(outputs[0], "Normal", _normal);
                var output = "n" + (_variables++).ToString(CultureInfo.InvariantCulture);
                _body.Append("    vec4 ").Append(output).Append(" = ").Append(Input(outputs[0], "Color", "vColor")).AppendLine(";");
                _body.Append("    ").Append(output).Append(".a *= ").Append(Input(outputs[0], "Alpha", "vec4(1.0)")).AppendLine(".x;");
                return output;
            }
            finally { _activeGraphs.Remove(graph); }
        }

        private string Parameter(ShaderNode node)
        {
            if (string.IsNullOrWhiteSpace(node.parameter)) throw new InvalidDataException("Parameter name is required.");
            if (_textures.Any(item => item.Name == node.parameter)) throw new InvalidDataException($"Parameter '{node.parameter}' has conflicting types.");
            var index = _parameters.FindIndex(item => item.Name == node.parameter);
            if (index < 0)
            {
                if (_parameters.Count >= 16) throw new InvalidDataException("Shader Graph supports at most 16 exposed vector parameters.");
                index = _parameters.Count; _parameters.Add(new(node.parameter, node.value));
            }
            else if (!_parameters[index].DefaultValue.Equals(node.value))
                throw new InvalidDataException($"Parameter '{node.parameter}' has conflicting defaults.");
            return $"uParameters[{index}]";
        }

        private int TextureParameter(ShaderNode node)
        {
            if (string.IsNullOrWhiteSpace(node.parameter)) throw new InvalidDataException("Texture parameter name is required.");
            if (_parameters.Any(item => item.Name == node.parameter)) throw new InvalidDataException($"Parameter '{node.parameter}' has conflicting types.");
            var entry = _textures.FirstOrDefault(item => item.Name == node.parameter);
            if (entry is not null)
            {
                if (!ReferenceEquals(entry.DefaultTexture, node.texture)) throw new InvalidDataException($"Texture '{node.parameter}' has conflicting defaults.");
                return entry.Slot;
            }
            if (_textures.Count >= 7) throw new InvalidDataException("Shader Graph supports at most seven exposed textures in addition to the Sprite texture.");
            var slot = _textures.Count + 1;
            _textures.Add(new(node.parameter, node.texture, slot));
            return slot;
        }

        private string Sample(int slot, string uv) => backend switch
        {
            GraphicsBackend.Direct3D11 or GraphicsBackend.Direct3D12 => $"GraphTexture{slot}.Sample(GraphSampler{slot}, ({uv}).xy)",
            GraphicsBackend.WebGPU => $"textureSample(GraphTexture{slot}, GraphSampler{slot}, ({uv}).xy)",
            _ => Vulkan ? $"texture(sampler2D(GraphTexture{slot}, GraphSampler{slot}), {uv}.xy)" : $"texture(uTexture{slot}, {uv}.xy)"
        };

        private static string SubGraphInput(ShaderNode node, Dictionary<string, string>? arguments)
        {
            if (node.parameter is not ("A" or "B" or "C" or "D")) throw new InvalidDataException("SubGraphInput name must be A, B, C or D.");
            return arguments is not null && arguments.TryGetValue(node.parameter, out var value) ? value : ColorLiteral(node.value);
        }

        private string CustomFunction(ShaderNode node)
        {
            var code = backend switch
            {
                GraphicsBackend.Direct3D11 or GraphicsBackend.Direct3D12 => node.codeHlsl,
                GraphicsBackend.WebGPU => node.codeWgsl,
                _ => node.code
            };
            if (string.IsNullOrWhiteSpace(code) && node.code == "return A;") code = node.code;
            if (string.IsNullOrWhiteSpace(code) || code.Length > 16384 || code.Contains('#'))
                throw new InvalidDataException($"CustomFunction '{node.id}' requires a {backend} function body of at most 16384 characters without preprocessor directives.");
            var name = "graphFunction" + (_functionCount++).ToString(CultureInfo.InvariantCulture);
            _functions.Append(backend == GraphicsBackend.WebGPU ? $"fn {name}(A: vec4<f32>, B: vec4<f32>, C: vec4<f32>, D: vec4<f32>) -> vec4<f32> {{\n" : $"vec4 {name}(vec4 A, vec4 B, vec4 C, vec4 D) {{\n").Append(code).AppendLine("\n}");
            return name;
        }
    }

    private static string Number(Fix64 number) => ((double)number).ToString("0.0################", CultureInfo.InvariantCulture);
    private static string ColorLiteral(Color color) => $"vec4({Number(color.r)}, {Number(color.g)}, {Number(color.b)}, {Number(color.a)})";
}
