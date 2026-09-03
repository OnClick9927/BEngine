using BEngine.UIElements;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public sealed class UIElementsRenderer : IDisposable
{

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
    private UIRenderStatistics _lastStatistics;
    private bool _disposed;

    public GraphicsBackend Backend
    {
        get { return _device.Backend; }
    }
    public GraphicsDeviceCapabilities Capabilities
    {
        get { return _device.Capabilities; }
    }
    public UIRenderStatistics LastStatistics
    {
        get { return _lastStatistics; }
        private set => _lastStatistics = value;
    }

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
        => Render(scene, width, height, new GraphicsRect(0, 0, width, height));

    public void Render(Scene scene, int width, int height, GraphicsRect viewport)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var renderLists = scene.QueryComponents<UIDocument>().ToArray()
            .Where(item => item.gameObject.activeInHierarchy && item.enabled)
            .OrderBy(item => item.sortingOrder)
            .Select(document => UIRenderListBuilder.Build(
                document.rootVisualElement, width, height, document.ResolveScale(width, height)))
            .ToArray();
        RenderCore(renderLists, width, height, viewport);
    }

    public void Render(VisualElement root, int width, int height, Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        RenderCore([UIRenderListBuilder.Build(root, width, height, scale)], width, height);
    }

    public void Render(IEnumerable<UIRenderCommandList> renderLists, int width, int height)
    {
        RenderCore(renderLists, width, height);
    }

    public void RenderCommands(IEnumerable<UIRenderCommand> commands, int width, int height,
        GraphicsRect viewport)
    {
        RenderCore(commands, width, height, viewport);
    }

    private void RenderCore(IEnumerable<UIRenderCommandList> renderLists, int width, int height,
        GraphicsRect? viewport = null)
    {
        ArgumentNullException.ThrowIfNull(renderLists);
        RenderCore(renderLists.SelectMany(item => item.Commands), width, height, viewport);
    }

    private void RenderCore(IEnumerable<UIRenderCommand> commands, int width, int height,
        GraphicsRect? viewport = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        TrimTextureCache();
        var build = BuildBatches(commands);
        if (build.Batches.Count == 0)
        {
            LastStatistics = new UIRenderStatistics(
                build.CommandCount, 0, 0, 0, build.UnresolvedImages);
            return;
        }

        var targetViewport = viewport ?? new GraphicsRect(0, 0, width, height);
        _device.SetViewport(targetViewport);
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        _device.SetRasterizerState(GraphicsRasterizerState.Default);
        UploadFrameGeometry(build.Batches);
        IGraphicsProgram? activeProgram = null;
        var drawCalls = 0;
        var vertexCount = 0;
        try
        {
            foreach (var batch in build.Batches)
            {
                _device.SetScissor(ToGraphicsRect(batch.ClipRect,
                    width, height, targetViewport.Width, targetViewport.Height));
                if (batch.Texture is null)
                {
                    if (!ReferenceEquals(activeProgram, _shader))
                    {
                        activeProgram = _shader;
                        BindProgram(activeProgram, width, height);
                    }
                    _device.Draw(_mesh, batch.VertexCount, batch.FirstVertex);
                    vertexCount += batch.VertexCount;
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
                    _device.Draw(_textureMesh!, batch.VertexCount, batch.FirstVertex);
                    vertexCount += batch.VertexCount;
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

    private void TrimTextureCache()
    {
        const int maximumCount = 512;
        const int retainedCount = 384;
        if (_textures.Count < maximumCount) return;
        foreach (var key in _textures.Keys.Take(_textures.Count - retainedCount).ToArray())
        {
            var texture = _textures[key];
            _textures.Remove(key);
            texture.Dispose();
        }
    }

    private BatchBuildResult BuildBatches(IEnumerable<UIRenderCommand> commands)
    {
        List<RenderBatch> batches = [];
        var commandCount = 0;
        var unresolvedImages = 0;
        foreach (var command in commands)
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

            if (command.Type == UIRenderCommandType.Text && TryResolveTextTexture(command, out var textTexture))
            {
                var textBatch = GetBatch(batches, command.ClipRect, textTexture);
                AddTexturedQuad(textBatch.Vertices, command.Rect, command.Color);
                continue;
            }

            var output = GetBatch(batches, command.ClipRect, null).Vertices;
            if (command.Type == UIRenderCommandType.SolidRect)
                AddQuad(output, command.Rect, command.Color);
            else if (command.Type == UIRenderCommandType.Text)
                AddText(output, command.Content, command.Rect, command.Color, command.FontSize);
        }
        batches.RemoveAll(batch => batch.Vertices.Count == 0);
        return new BatchBuildResult(batches, commandCount, unresolvedImages);
    }

    private void UploadFrameGeometry(IReadOnlyList<RenderBatch> batches)
    {
        List<float> solidVertices = [];
        List<float> texturedVertices = [];
        foreach (var batch in batches)
        {
            var destination = batch.Texture is null ? solidVertices : texturedVertices;
            var stride = batch.Texture is null ? VertexLayout.StrideBytes : TextureVertexLayout.StrideBytes;
            batch.FirstVertex = destination.Count * sizeof(float) / stride;
            destination.AddRange(batch.Vertices);
        }
        if (solidVertices.Count > 0) _mesh.Update(CollectionsMarshal.AsSpan(solidVertices));
        if (texturedVertices.Count <= 0) return;
        EnsureTexturePipeline();
        _textureMesh!.Update(CollectionsMarshal.AsSpan(texturedVertices));
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

    private bool TryResolveTextTexture(UIRenderCommand command, out IGraphicsTexture2D? texture)
    {
        texture = null;
        if (_resourceResolver is not IUITextRenderResourceResolver textResolver ||
            !_device.Capabilities.Supports(GraphicsDeviceFeatures.SampledTextures)) return false;
        var width = Math.Max(1, (int)Math.Ceiling((double)command.Rect.Width));
        var height = Math.Max(1, (int)Math.Ceiling((double)command.Rect.Height));
        var key = $"text:{width}:{height}:{command.FontSize}:{command.Content}";
        if (_textures.TryGetValue(key, out texture)) return true;
        if (!textResolver.TryResolveText(command.Content, width, height, command.FontSize, out var data)) return false;
        data.Validate();
        texture = _device.CreateTexture2D(
            "BEngine.UIElements.Text",
            new GraphicsTextureDescription(data.Width, data.Height, data.Format,
                GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Linear, GraphicsTextureFilter.Linear),
            data.Pixels.Span);
        _textures.Add(key, texture);
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

    private static GraphicsRect ToGraphicsRect(UIElementRect rect,
        int logicalWidth, int logicalHeight, int viewportWidth, int viewportHeight)
    {
        var scaleX = Math.Max(1, viewportWidth) / (double)Math.Max(1, logicalWidth);
        var scaleY = Math.Max(1, viewportHeight) / (double)Math.Max(1, logicalHeight);
        var left = Math.Clamp((int)Math.Floor((double)rect.X * scaleX), 0, Math.Max(0, viewportWidth));
        var top = Math.Clamp((int)Math.Floor((double)rect.Y * scaleY), 0, Math.Max(0, viewportHeight));
        var right = Math.Clamp((int)Math.Ceiling((double)(rect.X + rect.Width) * scaleX),
            left, Math.Max(left, viewportWidth));
        var bottom = Math.Clamp((int)Math.Ceiling((double)(rect.Y + rect.Height) * scaleY),
            top, Math.Max(top, viewportHeight));
        return new GraphicsRect(left, top, right - left, bottom - top);
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveShaderSources(
        IGraphicsDevice device,
        bool textured)
    {
        if (device.Backend == GraphicsBackend.Vulkan)
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load(textured
                        ? "Shaders/UIElements/Textured.vulkan.vert.glsl"
                        : "Shaders/UIElements/Solid.vulkan.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load(textured
                        ? "Shaders/UIElements/Textured.vulkan.frag.glsl"
                        : "Shaders/UIElements/Solid.vulkan.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load(textured
                        ? "Shaders/UIElements/Textured.opengl.vert.glsl"
                        : "Shaders/UIElements/Solid.opengl.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load(textured
                        ? "Shaders/UIElements/Textured.opengl.frag.glsl"
                        : "Shaders/UIElements/Solid.opengl.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load(textured
                        ? "Shaders/UIElements/Textured.direct3d.vert.hlsl"
                        : "Shaders/UIElements/Solid.direct3d.vert.hlsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load(textured
                        ? "Shaders/UIElements/Textured.direct3d.frag.hlsl"
                        : "Shaders/UIElements/Solid.direct3d.frag.hlsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Wgsl))
        {
            var wgsl = DefaultShaderResources.Load(textured
                ? "Shaders/UIElements/Textured.webgpu.wgsl"
                : "Shaders/UIElements/Solid.webgpu.wgsl");
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
        public int FirstVertex { get; set; }
        public int VertexCount => Vertices.Count * sizeof(float) /
                                  (Texture is null ? VertexLayout.StrideBytes : TextureVertexLayout.StrideBytes);
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
