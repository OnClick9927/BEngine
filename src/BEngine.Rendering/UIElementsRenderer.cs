using BEngine.UIElements;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public readonly record struct UIRenderStatistics(
    int CommandCount,
    int BatchCount,
    int DrawCalls,
    int VertexCount,
    int UnresolvedImages);

public sealed class UIElementsRenderer : IDisposable
{
    private const string VertexShader = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec4 aColor;
        uniform float uViewportWidth;
        uniform float uViewportHeight;
        out vec4 vColor;
        void main()
        {
            vec2 normalized = vec2(aPosition.x / uViewportWidth, aPosition.y / uViewportHeight);
            gl_Position = vec4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
            vColor = aColor;
        }
        """;

    private const string FragmentShader = """
        #version 330 core
        in vec4 vColor;
        out vec4 fragColor;
        void main() { fragColor = vColor; }
        """;

    private const string HlslVertexShader = """
        cbuffer UIViewport : register(b0)
        {
            float uViewportWidth;
            float uViewportHeight;
        };
        struct VertexInput { float2 Position : POSITION0; float4 Color : COLOR0; };
        struct VertexOutput { float4 Position : SV_Position; float4 Color : COLOR0; };
        VertexOutput main(VertexInput input)
        {
            VertexOutput output;
            float2 normalized = float2(input.Position.x / uViewportWidth, input.Position.y / uViewportHeight);
            output.Position = float4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
            output.Color = input.Color;
            return output;
        }
        """;

    private const string HlslFragmentShader = """
        struct FragmentInput { float4 Position : SV_Position; float4 Color : COLOR0; };
        float4 main(FragmentInput input) : SV_Target0 { return input.Color; }
        """;

    private const string WgslShader = """
        struct UIViewport { width: f32, height: f32 };
        @group(0) @binding(0) var<uniform> viewport: UIViewport;
        struct VertexInput { @location(0) position: vec2<f32>, @location(1) color: vec4<f32> };
        struct VertexOutput { @builtin(position) position: vec4<f32>, @location(0) color: vec4<f32> };
        @vertex fn vs_main(input: VertexInput) -> VertexOutput {
            var output: VertexOutput;
            let normalized = vec2<f32>(input.position.x / viewport.width, input.position.y / viewport.height);
            output.position = vec4<f32>(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
            output.color = input.color;
            return output;
        }
        @fragment fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> { return input.color; }
        """;

    private const string TextureVertexShader = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec4 aColor;
        layout (location = 2) in vec2 aTexCoord;
        uniform float uViewportWidth;
        uniform float uViewportHeight;
        out vec4 vColor;
        out vec2 vTexCoord;
        void main()
        {
            vec2 normalized = vec2(aPosition.x / uViewportWidth, aPosition.y / uViewportHeight);
            gl_Position = vec4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
            vColor = aColor;
            vTexCoord = aTexCoord;
        }
        """;

    private const string TextureFragmentShader = """
        #version 330 core
        in vec4 vColor;
        in vec2 vTexCoord;
        uniform sampler2D uTexture;
        out vec4 fragColor;
        void main() { fragColor = texture(uTexture, vTexCoord) * vColor; }
        """;

    private const string HlslTextureVertexShader = """
        cbuffer UIViewport : register(b0)
        {
            float uViewportWidth;
            float uViewportHeight;
        };
        struct VertexInput
        {
            float2 Position : POSITION0;
            float4 Color : COLOR0;
            float2 TexCoord : TEXCOORD0;
        };
        struct VertexOutput
        {
            float4 Position : SV_Position;
            float4 Color : COLOR0;
            float2 TexCoord : TEXCOORD0;
        };
        VertexOutput main(VertexInput input)
        {
            VertexOutput output;
            float2 normalized = float2(input.Position.x / uViewportWidth, input.Position.y / uViewportHeight);
            output.Position = float4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
            output.Color = input.Color;
            output.TexCoord = input.TexCoord;
            return output;
        }
        """;

    private const string HlslTextureFragmentShader = """
        Texture2D UITexture : register(t0);
        SamplerState UISampler : register(s0);
        struct FragmentInput
        {
            float4 Position : SV_Position;
            float4 Color : COLOR0;
            float2 TexCoord : TEXCOORD0;
        };
        float4 main(FragmentInput input) : SV_Target0
        {
            return UITexture.Sample(UISampler, input.TexCoord) * input.Color;
        }
        """;

    private const string WgslTextureShader = """
        struct UIViewport { width: f32, height: f32 };
        @group(0) @binding(0) var<uniform> viewport: UIViewport;
        @group(0) @binding(1) var uiTexture: texture_2d<f32>;
        @group(0) @binding(2) var uiSampler: sampler;
        struct VertexInput
        {
            @location(0) position: vec2<f32>,
            @location(1) color: vec4<f32>,
            @location(2) texCoord: vec2<f32>
        };
        struct VertexOutput
        {
            @builtin(position) position: vec4<f32>,
            @location(0) color: vec4<f32>,
            @location(1) texCoord: vec2<f32>
        };
        @vertex fn vs_main(input: VertexInput) -> VertexOutput {
            var output: VertexOutput;
            let normalized = vec2<f32>(input.position.x / viewport.width, input.position.y / viewport.height);
            output.position = vec4<f32>(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);
            output.color = input.color;
            output.texCoord = input.texCoord;
            return output;
        }
        @fragment fn fs_main(input: VertexOutput) -> @location(0) vec4<f32>
        {
            return textureSample(uiTexture, uiSampler, input.texCoord) * input.color;
        }
        """;

    private static readonly GraphicsVertexLayout VertexLayout = new(
        6 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float))
        ]);

    private static readonly GraphicsVertexLayout TextureVertexLayout = new(
        8 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float)),
            new GraphicsVertexAttribute(2, 2, 6 * sizeof(float))
        ]);

    private readonly IGraphicsDevice _device;
    private readonly IGraphicsProgram _shader;
    private readonly IGraphicsMesh _mesh;
    private readonly IUIRenderResourceResolver _resourceResolver;
    private readonly Dictionary<string, IGraphicsTexture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _ownsDevice;
    private IGraphicsProgram? _textureShader;
    private IGraphicsMesh? _textureMesh;
    private bool _disposed;

    public GraphicsBackend Backend => _device.Backend;
    public GraphicsDeviceCapabilities Capabilities => _device.Capabilities;
    public UIRenderStatistics LastStatistics { get; private set; }

    public UIElementsRenderer(GL gl)
        : this(new OpenGlGraphicsDevice(gl), ownsDevice: true)
    {
    }

    public UIElementsRenderer(
        IGraphicsDevice device,
        IUIRenderResourceResolver? resourceResolver = null,
        bool ownsDevice = false)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.Capabilities.Require(
            GraphicsDeviceFeatures.Rasterization |
            GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers |
            GraphicsDeviceFeatures.AlphaBlending |
            GraphicsDeviceFeatures.ScissorRectangles);
        _device = device;
        _resourceResolver = resourceResolver ?? FileUIRenderResourceResolver.Shared;
        _ownsDevice = ownsDevice;
        var (vertexShader, fragmentShader) = ResolveShaderSources(device, textured: false);
        _shader = device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.UIElements",
            vertexShader,
            fragmentShader));
        _mesh = device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.UIElements.DynamicMesh",
            ReadOnlyMemory<float>.Empty,
            VertexLayout,
            usage: GraphicsBufferUsage.Dynamic));
    }

    public void Render(Scene scene, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var renderLists = scene.gameObjects
            .Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<UIDocument>())
            .Where(item => item.enabled)
            .OrderBy(item => item.sortingOrder)
            .Select(document => UIRenderListBuilder.Build(
                document.rootVisualElement, width, height, document.ResolveScale(width, height)))
            .ToArray();
        Render(renderLists, width, height);
    }

    public void Render(VisualElement root, int width, int height, Fix64? scale = null) =>
        Render([UIRenderListBuilder.Build(root, width, height, scale)], width, height);

    public void Render(IEnumerable<UIRenderCommandList> renderLists, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderLists);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var build = BuildBatches(renderLists);
        if (build.Batches.Count == 0)
        {
            LastStatistics = new UIRenderStatistics(
                build.CommandCount, 0, 0, 0, build.UnresolvedImages);
            return;
        }

        _device.SetViewport(new GraphicsRect(0, 0, width, height));
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        IGraphicsProgram? activeProgram = null;
        var drawCalls = 0;
        var vertexCount = 0;
        try
        {
            foreach (var batch in build.Batches)
            {
                _device.SetScissor(ToGraphicsRect(batch.ClipRect));
                if (batch.Texture is null)
                {
                    if (!ReferenceEquals(activeProgram, _shader))
                    {
                        activeProgram = _shader;
                        BindProgram(activeProgram, width, height);
                    }
                    _mesh.Update(CollectionsMarshal.AsSpan(batch.Vertices));
                    _device.Draw(_mesh);
                    vertexCount += _mesh.VertexCount;
                }
                else
                {
                    EnsureTexturePipeline();
                    if (!ReferenceEquals(activeProgram, _textureShader))
                    {
                        activeProgram = _textureShader!;
                        BindProgram(activeProgram, width, height);
                        activeProgram.SetInt("uTexture", 0);
                    }
                    _device.BindTexture(0, batch.Texture);
                    _textureMesh!.Update(CollectionsMarshal.AsSpan(batch.Vertices));
                    _device.Draw(_textureMesh);
                    vertexCount += _textureMesh.VertexCount;
                }
                drawCalls++;
            }
        }
        finally
        {
            _device.SetScissor(null);
            _device.SetDepthState(GraphicsDepthState.Default);
        }
        LastStatistics = new UIRenderStatistics(
            build.CommandCount,
            build.Batches.Count,
            drawCalls,
            vertexCount,
            build.UnresolvedImages);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
        _textureMesh?.Dispose();
        _textureShader?.Dispose();
        _mesh.Dispose();
        _shader.Dispose();
        if (_ownsDevice) _device.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public bool InvalidateTexture(string source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!_textures.Remove(source, out var texture)) return false;
        texture.Dispose();
        return true;
    }

    public void ClearTextureCache()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
    }

    private BatchBuildResult BuildBatches(IEnumerable<UIRenderCommandList> renderLists)
    {
        var batches = new List<RenderBatch>();
        var commandCount = 0;
        var unresolvedImages = 0;
        foreach (var renderList in renderLists)
        {
            foreach (var command in renderList.Commands)
            {
                commandCount++;
                if (command.Type == UIRenderCommandType.Image)
                {
                    if (TryResolveTexture(command.Content, out var texture))
                    {
                        var batch = GetBatch(batches, command.ClipRect, texture);
                        AddTexturedQuad(batch.Vertices, command.Rect, command.Color);
                    }
                    else
                    {
                        unresolvedImages++;
                        AddMissingImage(GetBatch(batches, command.ClipRect, null).Vertices, command.Rect);
                    }
                    continue;
                }

                var output = GetBatch(batches, command.ClipRect, null).Vertices;
                if (command.Type == UIRenderCommandType.SolidRect)
                    AddQuad(output, command.Rect, command.Color);
                else if (command.Type == UIRenderCommandType.Text)
                    AddText(output, command.Content, command.Rect, command.Color, command.FontSize);
            }
        }
        batches.RemoveAll(batch => batch.Vertices.Count == 0);
        return new BatchBuildResult(batches, commandCount, unresolvedImages);
    }

    private static RenderBatch GetBatch(
        List<RenderBatch> batches,
        UIElementRect clipRect,
        IGraphicsTexture2D? texture)
    {
        if (batches.Count == 0 || batches[^1].ClipRect != clipRect ||
            !ReferenceEquals(batches[^1].Texture, texture))
        {
            batches.Add(new RenderBatch(clipRect, texture));
        }
        return batches[^1];
    }

    private static void AddText(
        List<float> output,
        string text,
        UIElementRect rect,
        UIColor color,
        float requestedSize)
    {
        var fontSize = requestedSize > 0 ? requestedSize : 14;
        var cell = Math.Max(1f, fontSize / 7f);
        var glyphWidth = cell * 5;
        var advance = cell * 6;
        var glyphHeight = cell * 7;
        var x = (float)rect.X + 6;
        var y = (float)rect.Y + Math.Max(0, ((float)rect.Height - glyphHeight) * 0.5f);
        foreach (var character in text.Replace("\r", string.Empty, StringComparison.Ordinal))
        {
            if (character == '\n')
            {
                x = (float)rect.X + 6;
                y += glyphHeight + cell;
                continue;
            }
            if (x + glyphWidth > (float)(rect.X + rect.Width)) break;
            if (character != ' ')
            {
                var pattern = BuiltinFont.Pattern(character);
                for (var row = 0; row < 7; row++)
                for (var column = 0; column < 5; column++)
                {
                    if (pattern[row * 5 + column] != '1') continue;
                    AddQuad(output, new UIElementRect(
                        (Fix64)(x + column * cell),
                        (Fix64)(y + row * cell),
                        (Fix64)Math.Max(1, cell * 0.82f),
                        (Fix64)Math.Max(1, cell * 0.82f)), color);
                }
            }
            x += advance;
        }
    }

    private static void AddQuad(List<float> output, UIElementRect rect, UIColor color)
    {
        var x0 = (float)rect.X;
        var x1 = (float)(rect.X + rect.Width);
        var y0 = (float)rect.Y;
        var y1 = (float)(rect.Y + rect.Height);
        var rgba = new NVector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        AddVertex(output, x0, y0, rgba);
        AddVertex(output, x1, y0, rgba);
        AddVertex(output, x1, y1, rgba);
        AddVertex(output, x0, y0, rgba);
        AddVertex(output, x1, y1, rgba);
        AddVertex(output, x0, y1, rgba);
    }

    private static void AddTexturedQuad(List<float> output, UIElementRect rect, UIColor color)
    {
        var x0 = (float)rect.X;
        var x1 = (float)(rect.X + rect.Width);
        var y0 = (float)rect.Y;
        var y1 = (float)(rect.Y + rect.Height);
        var rgba = new NVector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        AddTexturedVertex(output, x0, y0, rgba, 0, 0);
        AddTexturedVertex(output, x1, y0, rgba, 1, 0);
        AddTexturedVertex(output, x1, y1, rgba, 1, 1);
        AddTexturedVertex(output, x0, y0, rgba, 0, 0);
        AddTexturedVertex(output, x1, y1, rgba, 1, 1);
        AddTexturedVertex(output, x0, y1, rgba, 0, 1);
    }

    private static void AddMissingImage(List<float> output, UIElementRect rect)
    {
        var halfWidth = rect.Width / 2;
        var halfHeight = rect.Height / 2;
        var dark = new UIColor(48, 50, 54);
        var accent = new UIColor(194, 63, 86);
        AddQuad(output, new UIElementRect(rect.X, rect.Y, halfWidth, halfHeight), dark);
        AddQuad(output, new UIElementRect(rect.X + halfWidth, rect.Y, rect.Width - halfWidth, halfHeight), accent);
        AddQuad(output, new UIElementRect(rect.X, rect.Y + halfHeight, halfWidth, rect.Height - halfHeight), accent);
        AddQuad(output, new UIElementRect(
            rect.X + halfWidth,
            rect.Y + halfHeight,
            rect.Width - halfWidth,
            rect.Height - halfHeight), dark);
    }

    private static void AddVertex(List<float> output, float x, float y, NVector4 color)
    {
        output.Add(x);
        output.Add(y);
        output.Add(color.X);
        output.Add(color.Y);
        output.Add(color.Z);
        output.Add(color.W);
    }

    private static void AddTexturedVertex(
        List<float> output,
        float x,
        float y,
        NVector4 color,
        float u,
        float v)
    {
        AddVertex(output, x, y, color);
        output.Add(u);
        output.Add(v);
    }

    private bool TryResolveTexture(string source, out IGraphicsTexture2D? texture)
    {
        texture = null;
        if (!_device.Capabilities.Supports(GraphicsDeviceFeatures.SampledTextures)) return false;
        if (_textures.TryGetValue(source, out texture)) return true;
        if (!_resourceResolver.TryResolveTexture(source, out var data)) return false;
        data.Validate();
        texture = _device.CreateTexture2D(
            $"BEngine.UIElements.{source}",
            new GraphicsTextureDescription(
                data.Width,
                data.Height,
                data.Format,
                GraphicsTextureUsage.Sampled,
                GraphicsTextureFilter.Linear,
                GraphicsTextureFilter.Linear,
                GraphicsTextureAddressMode.ClampToEdge),
            data.Pixels.Span);
        _textures.Add(source, texture);
        return true;
    }

    private void EnsureTexturePipeline()
    {
        if (_textureShader is not null && _textureMesh is not null) return;
        _device.Capabilities.Require(GraphicsDeviceFeatures.SampledTextures);
        var (vertexShader, fragmentShader) = ResolveShaderSources(_device, textured: true);
        _textureShader = _device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.UIElements.Texture",
            vertexShader,
            fragmentShader));
        try
        {
            _textureMesh = _device.CreateMesh(new GraphicsMeshDescription(
                "BEngine.UIElements.TextureDynamicMesh",
                ReadOnlyMemory<float>.Empty,
                TextureVertexLayout,
                usage: GraphicsBufferUsage.Dynamic));
        }
        catch
        {
            _textureShader.Dispose();
            _textureShader = null;
            throw;
        }
    }

    private static void BindProgram(IGraphicsProgram program, int width, int height)
    {
        program.Bind();
        program.SetFloat("uViewportWidth", width);
        program.SetFloat("uViewportHeight", height);
    }

    private static GraphicsRect ToGraphicsRect(UIElementRect rect) => new(
        Math.Max(0, (int)rect.X),
        Math.Max(0, (int)rect.Y),
        Math.Max(0, (int)rect.Width),
        Math.Max(0, (int)rect.Height));

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveShaderSources(
        IGraphicsDevice device,
        bool textured)
    {
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    textured ? TextureVertexShader : VertexShader),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    textured ? TextureFragmentShader : FragmentShader));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Hlsl,
                    textured ? HlslTextureVertexShader : HlslVertexShader),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Hlsl,
                    textured ? HlslTextureFragmentShader : HlslFragmentShader));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Wgsl))
        {
            var wgsl = textured ? WgslTextureShader : WgslShader;
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, wgsl, "vs_main"),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, wgsl, "fs_main"));
        }
        throw new NotSupportedException(
            $"The built-in UI pipeline has no shader source for {device.Backend}. " +
            $"Supported device languages: {string.Join(", ", device.Capabilities.ShaderLanguages)}.");
    }

    private sealed class RenderBatch(UIElementRect clipRect, IGraphicsTexture2D? texture)
    {
        public UIElementRect ClipRect { get; } = clipRect;
        public IGraphicsTexture2D? Texture { get; } = texture;
        public List<float> Vertices { get; } = [];
    }

    private sealed record BatchBuildResult(
        IReadOnlyList<RenderBatch> Batches,
        int CommandCount,
        int UnresolvedImages);

    private static class BuiltinFont
    {
        private const string Missing = "11111100010001000100001000000000100";
        private static readonly Dictionary<char, string> Glyphs = new()
        {
            ['A']="01110100011000111111100011000110001", ['B']="11110100011000111110100011000111110",
            ['C']="01111100001000010000100001000001111", ['D']="11110100011000110001100011000111110",
            ['E']="11111100001000011110100001000011111", ['F']="11111100001000011110100001000010000",
            ['G']="01111100001000010111100011000101111", ['H']="10001100011000111111100011000110001",
            ['I']="11111001000010000100001000010011111", ['J']="00111000100001000010100101001001100",
            ['K']="10001100101010011000101001001010001", ['L']="10000100001000010000100001000011111",
            ['M']="10001110111010110101100011000110001", ['N']="10001110011010110011100011000110001",
            ['O']="01110100011000110001100011000101110", ['P']="11110100011000111110100001000010000",
            ['Q']="01110100011000110001101011001001101", ['R']="11110100011000111110101001001010001",
            ['S']="01111100001000001110000010000111110", ['T']="11111001000010000100001000010000100",
            ['U']="10001100011000110001100011000101110", ['V']="10001100011000110001100010101000100",
            ['W']="10001100011000110101101011101110001", ['X']="10001100010101000100010101000110001",
            ['Y']="10001100010101000100001000010000100", ['Z']="11111000010001000100010001000011111",
            ['0']="01110100011001110101110011000101110", ['1']="00100011000010000100001000010001110",
            ['2']="01110100010000100010001000100011111", ['3']="11110000010000101110000010000111110",
            ['4']="00010001100101010010111110001000010", ['5']="11111100001000011110000010000111110",
            ['6']="01110100001000011110100011000101110", ['7']="11111000010001000100010000100001000",
            ['8']="01110100011000101110100011000101110", ['9']="01110100011000101111000010000101110",
            ['.']="00000000000000000000000000011000110", [',']="00000000000000000000001100011000100",
            [':']="00000001100011000000001100011000000", ['-']="00000000000000011111000000000000000",
            ['_']="00000000000000000000000000000011111", ['!']="00100001000010000100001000000000100",
            ['?']="01110100010000100010001000000000100", ['/']="00001000100001000100010001000010000",
            ['(']="00010001000100001000010000010000010", [')']="01000001000010000010001000100001000"
        };

        public static string Pattern(char value) => Glyphs.TryGetValue(char.ToUpperInvariant(value), out var pattern)
            ? pattern
            : Missing;
    }
}
