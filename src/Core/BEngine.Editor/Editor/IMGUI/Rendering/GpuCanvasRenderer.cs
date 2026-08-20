using System.Runtime.InteropServices;
using System.Text;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Editor.Rendering;

/// <summary>Backend-neutral immediate 2D command renderer used by IMGUI and optional UI packages.</summary>
public sealed class GpuCanvasRenderer : IDisposable
{
    private const int MaximumRasterizedTextWidth = 2046;

    private static readonly GraphicsVertexLayout Layout = new(6 * sizeof(float),
        [new GraphicsVertexAttribute(0, 2, 0), new GraphicsVertexAttribute(1, 4, 2 * sizeof(float))]);
    private static readonly GraphicsVertexLayout TextureLayout = new(8 * sizeof(float),
        [
            new GraphicsVertexAttribute(0, 2, 0),
            new GraphicsVertexAttribute(1, 4, 2 * sizeof(float)),
            new GraphicsVertexAttribute(2, 2, 6 * sizeof(float))
        ]);
    private readonly IGraphicsDevice _device;
    private readonly IGraphicsProgram _program;
    private readonly IGraphicsMesh _mesh;
    private readonly IGpuCanvasResourceResolver _resourceResolver;
    private readonly Dictionary<string, IGraphicsTexture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IGraphicsTexture2D> _textTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _textTextureAccess = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _textWidths = new(StringComparer.Ordinal);
    private readonly List<GpuCanvasBatch> _batches = [];
    private readonly List<float> _vertices = [];
    private readonly List<float> _textureVertices = [];
    private readonly bool _ownsDevice;
    private GpuCanvasAtlas? _atlas;
    private IGraphicsProgram? _textureProgram;
    private IGraphicsMesh? _textureMesh;
    private bool _disposed;
    private long _renderSequence;

    public GpuCanvasRenderStats LastRenderStats { get; private set; }

    public GpuCanvasRenderer(IGraphicsDevice device, bool ownsDevice = false,
        IGpuCanvasResourceResolver? resourceResolver = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _ownsDevice = ownsDevice;
        _resourceResolver = resourceResolver ?? FileGpuCanvasResourceResolver.Shared;
        device.Capabilities.Require(GraphicsDeviceFeatures.Rasterization |
                                    GraphicsDeviceFeatures.ShaderPrograms |
                                    GraphicsDeviceFeatures.DynamicVertexBuffers |
                                    GraphicsDeviceFeatures.AlphaBlending |
                                    GraphicsDeviceFeatures.ScissorRectangles);
        var shader = ResolveShaders(device);
        _program = device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.GpuCanvas", shader.Vertex, shader.Fragment));
        _mesh = device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.GpuCanvas.Dynamic", ReadOnlyMemory<float>.Empty, Layout,
            usage: GraphicsBufferUsage.Dynamic));
    }

    public void Render(IReadOnlyList<GpuCanvasCommand> commands, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (commands.Count == 0)
        {
            LastRenderStats = default;
            return;
        }
        _renderSequence++;
        var visibleCommands = BuildBatches(commands);
        if (_batches.Count == 0)
        {
            LastRenderStats = new GpuCanvasRenderStats(commands.Count, 0, 0, 0, 0, 0);
            return;
        }
        var uploads = 0;
        if (_vertices.Count > 0)
        {
            _mesh.Update(CollectionsMarshal.AsSpan(_vertices));
            uploads++;
        }
        if (_textureVertices.Count > 0)
        {
            EnsureTexturePipeline();
            _textureMesh!.Update(CollectionsMarshal.AsSpan(_textureVertices));
            uploads++;
        }
        _device.SetViewport(new GraphicsRect(0, 0, Math.Max(1, width), Math.Max(1, height)));
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        IGraphicsProgram? activeProgram = null;
        try
        {
            foreach (var batch in _batches)
            {
                _device.SetScissor(ToScissor(batch.Clip, width, height));
                if (batch.Texture is null)
                {
                    if (!ReferenceEquals(activeProgram, _program))
                    {
                        activeProgram = _program;
                        BindProgram(activeProgram, width, height);
                    }
                    _device.Draw(_mesh, batch.VertexCount, batch.FirstVertex);
                }
                else
                {
                    EnsureTexturePipeline();
                    if (!ReferenceEquals(activeProgram, _textureProgram))
                    {
                        activeProgram = _textureProgram!;
                        BindProgram(activeProgram, width, height);
                        activeProgram.SetInt("uTexture", 0);
                    }
                    _device.BindTexture(0, batch.Texture);
                    _device.Draw(_textureMesh!, batch.VertexCount, batch.FirstVertex);
                }
            }
        }
        finally
        {
            _device.SetScissor(null);
            _device.SetDepthState(GraphicsDepthState.Default);
            TrimTextCache();
        }
        LastRenderStats = new GpuCanvasRenderStats(commands.Count, visibleCommands, _batches.Count,
            _batches.Count, uploads, _vertices.Count / 6 + _textureVertices.Count / 8);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
        foreach (var texture in _textTextures.Values) texture.Dispose();
        _textTextures.Clear();
        _atlas?.Dispose();
        _textureMesh?.Dispose();
        _textureProgram?.Dispose();
        _mesh.Dispose();
        _program.Dispose();
        if (_ownsDevice) _device.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private int BuildBatches(IReadOnlyList<GpuCanvasCommand> commands)
    {
        _batches.Clear();
        _vertices.Clear();
        _textureVertices.Clear();
        var visibleCommands = 0;
        foreach (var command in commands)
        {
            if (command.Rect.Width <= 0 || command.Rect.Height <= 0 ||
                command.ClipRect.Width <= 0 || command.ClipRect.Height <= 0) continue;
            var before = _vertices.Count + _textureVertices.Count;
            switch (command.Type)
            {
                case GpuCanvasCommandType.SolidRect:
                    AppendSolidQuad(command.ClipRect, command.Rect, command.Color);
                    break;
                case GpuCanvasCommandType.GradientRect:
                    AppendGradientQuad(command.ClipRect, command.Rect, command.Color,
                        command.Color2, command.Color3, command.Color4);
                    break;
                case GpuCanvasCommandType.Text:
                    if (TryResolveText(command, out var textTexture, out var textRegion, out var textRect))
                        AppendTexturedQuad(command.ClipRect, textTexture!, textRegion,
                            textRect, command.Color);
                    else AppendText(command);
                    break;
                case GpuCanvasCommandType.Image:
                    if (TryResolveTexture(command.Content, out var texture, out var imageRegion))
                        AppendTexturedQuad(command.ClipRect, texture!, imageRegion,
                            command.Rect, command.Color);
                    else AppendMissingImage(command.ClipRect, command.Rect);
                    break;
            }
            if (_vertices.Count + _textureVertices.Count > before) visibleCommands++;
        }
        _atlas?.Flush();
        return visibleCommands;
    }

    private void AppendSolidQuad(GpuCanvasRect clip, GpuCanvasRect rect, GpuCanvasColor color)
    {
        if (TryGetAtlas(out var atlas))
            AppendTexturedQuad(clip, atlas.Texture, atlas.WhiteRegion, rect, color);
        else AppendQuad(clip, rect, color);
    }

    private void AppendQuad(GpuCanvasRect clip, GpuCanvasRect rect, GpuCanvasColor color)
    {
        var firstVertex = _vertices.Count / 6;
        AddQuad(_vertices, rect, color);
        AppendBatch(clip, null, firstVertex, _vertices.Count / 6 - firstVertex);
    }

    private void AppendGradientQuad(GpuCanvasRect clip, GpuCanvasRect rect,
        GpuCanvasColor topLeft, GpuCanvasColor topRight,
        GpuCanvasColor bottomRight, GpuCanvasColor bottomLeft)
    {
        if (TryGetAtlas(out var atlas))
        {
            var firstTexturedVertex = _textureVertices.Count / 8;
            AddTexturedGradientQuad(_textureVertices, rect, atlas.WhiteRegion,
                topLeft, topRight, bottomRight, bottomLeft);
            AppendBatch(clip, atlas.Texture, firstTexturedVertex,
                _textureVertices.Count / 8 - firstTexturedVertex);
            return;
        }
        var firstVertex = _vertices.Count / 6;
        AddGradientQuad(_vertices, rect, topLeft, topRight, bottomRight, bottomLeft);
        AppendBatch(clip, null, firstVertex, _vertices.Count / 6 - firstVertex);
    }

    private void AppendTexturedQuad(GpuCanvasRect clip, IGraphicsTexture2D texture,
        GpuCanvasAtlasRegion region, GpuCanvasRect rect, GpuCanvasColor color)
    {
        var firstVertex = _textureVertices.Count / 8;
        AddTexturedQuad(_textureVertices, rect, color, region);
        AppendBatch(clip, texture, firstVertex, _textureVertices.Count / 8 - firstVertex);
    }

    private void AppendText(GpuCanvasCommand command)
    {
        var firstVertex = _vertices.Count / 6;
        AddText(_vertices, command.Content, command.Rect, command.Color, command.FontSize);
        AppendBatch(command.ClipRect, null, firstVertex, _vertices.Count / 6 - firstVertex);
    }

    private void AppendMissingImage(GpuCanvasRect clip, GpuCanvasRect rect)
    {
        AppendSolidQuad(clip, rect, new GpuCanvasColor(52, 54, 58));
        var size = Math.Max(2, Math.Min(rect.Width, rect.Height) * 0.15f);
        AppendSolidQuad(clip, new GpuCanvasRect(rect.X, rect.Y, rect.Width, size),
            new GpuCanvasColor(77, 154, 210));
    }

    private void AppendBatch(GpuCanvasRect clip, IGraphicsTexture2D? texture, int firstVertex, int vertexCount)
    {
        if (vertexCount <= 0) return;
        if (_batches.Count > 0)
        {
            var last = _batches[^1];
            if (last.Clip == clip && ReferenceEquals(last.Texture, texture) &&
                last.FirstVertex + last.VertexCount == firstVertex)
            {
                last.VertexCount += vertexCount;
                _batches[^1] = last;
                return;
            }
        }
        _batches.Add(new GpuCanvasBatch(clip, texture, firstVertex, vertexCount));
    }

    private static void AddText(List<float> output, string text, GpuCanvasRect rect,
        GpuCanvasColor color, float requestedSize)
    {
        var size = requestedSize > 0 ? requestedSize : 13;
        var cell = Math.Max(1f, size / 7f);
        var advance = cell * 6;
        var x = rect.X + 4;
        var y = rect.Y + Math.Max(0, (rect.Height - cell * 7) * 0.5f);
        foreach (var character in text.Replace("\r", string.Empty, StringComparison.Ordinal))
        {
            if (character == '\n') { x = rect.X + 4; y += cell * 8; continue; }
            if (x + cell * 5 > rect.Right) break;
            if (character != ' ')
            {
                var pattern = GpuBuiltinFont.Pattern(character);
                for (var row = 0; row < 7; row++)
                for (var column = 0; column < 5; column++)
                    if (pattern[row * 5 + column] == '1')
                        AddQuad(output, new GpuCanvasRect(x + column * cell, y + row * cell,
                            Math.Max(1, cell * 0.82f), Math.Max(1, cell * 0.82f)), color);
            }
            x += advance;
        }
    }

    private static void AddQuad(List<float> output, GpuCanvasRect rect, GpuCanvasColor color)
    {
        var c = new NVector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        AddVertex(output, rect.X, rect.Y, c); AddVertex(output, rect.Right, rect.Y, c);
        AddVertex(output, rect.Right, rect.Bottom, c); AddVertex(output, rect.X, rect.Y, c);
        AddVertex(output, rect.Right, rect.Bottom, c); AddVertex(output, rect.X, rect.Bottom, c);
    }

    private static void AddGradientQuad(List<float> output, GpuCanvasRect rect,
        GpuCanvasColor topLeft, GpuCanvasColor topRight,
        GpuCanvasColor bottomRight, GpuCanvasColor bottomLeft)
    {
        var tl = ToVector(topLeft);
        var tr = ToVector(topRight);
        var br = ToVector(bottomRight);
        var bl = ToVector(bottomLeft);
        AddVertex(output, rect.X, rect.Y, tl);
        AddVertex(output, rect.Right, rect.Y, tr);
        AddVertex(output, rect.Right, rect.Bottom, br);
        AddVertex(output, rect.X, rect.Y, tl);
        AddVertex(output, rect.Right, rect.Bottom, br);
        AddVertex(output, rect.X, rect.Bottom, bl);
    }

    private static void AddTexturedGradientQuad(List<float> output, GpuCanvasRect rect,
        GpuCanvasAtlasRegion region, GpuCanvasColor topLeft, GpuCanvasColor topRight,
        GpuCanvasColor bottomRight, GpuCanvasColor bottomLeft)
    {
        var tl = ToVector(topLeft);
        var tr = ToVector(topRight);
        var br = ToVector(bottomRight);
        var bl = ToVector(bottomLeft);
        AddTexturedVertex(output, rect.X, rect.Y, tl, region.U0, region.V0);
        AddTexturedVertex(output, rect.Right, rect.Y, tr, region.U1, region.V0);
        AddTexturedVertex(output, rect.Right, rect.Bottom, br, region.U1, region.V1);
        AddTexturedVertex(output, rect.X, rect.Y, tl, region.U0, region.V0);
        AddTexturedVertex(output, rect.Right, rect.Bottom, br, region.U1, region.V1);
        AddTexturedVertex(output, rect.X, rect.Bottom, bl, region.U0, region.V1);
    }

    private static void AddTexturedQuad(List<float> output, GpuCanvasRect rect, GpuCanvasColor color,
        GpuCanvasAtlasRegion region)
    {
        var c = new NVector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        AddTexturedVertex(output, rect.X, rect.Y, c, region.U0, region.V0);
        AddTexturedVertex(output, rect.Right, rect.Y, c, region.U1, region.V0);
        AddTexturedVertex(output, rect.Right, rect.Bottom, c, region.U1, region.V1);
        AddTexturedVertex(output, rect.X, rect.Y, c, region.U0, region.V0);
        AddTexturedVertex(output, rect.Right, rect.Bottom, c, region.U1, region.V1);
        AddTexturedVertex(output, rect.X, rect.Bottom, c, region.U0, region.V1);
    }

    private static void AddVertex(List<float> output, float x, float y, NVector4 color)
    {
        output.Add(x); output.Add(y); output.Add(color.X); output.Add(color.Y); output.Add(color.Z); output.Add(color.W);
    }

    private static NVector4 ToVector(GpuCanvasColor color) => new(
        color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static void AddTexturedVertex(List<float> output, float x, float y, NVector4 color, float u, float v)
    {
        AddVertex(output, x, y, color);
        output.Add(u);
        output.Add(v);
    }

    private bool TryResolveTexture(string source, out IGraphicsTexture2D? texture,
        out GpuCanvasAtlasRegion region)
    {
        texture = null;
        region = GpuCanvasAtlasRegion.Full;
        if (!_device.Capabilities.Supports(GraphicsDeviceFeatures.SampledTextures)) return false;
        if (_textures.TryGetValue(source, out texture)) return true;
        var atlasKey = $"image|{source}";
        if (TryGetAtlas(out var atlas) && atlas.TryGet(atlasKey, out region))
        {
            texture = atlas.Texture;
            return true;
        }
        if (!_resourceResolver.TryResolveTexture(source, out var data)) return false;
        data.Validate();
        if (atlas.TryGetOrAdd(atlasKey, data, out region))
        {
            texture = atlas.Texture;
            return true;
        }
        texture = _device.CreateTexture2D(
            $"BEngine.GpuCanvas.{Path.GetFileName(source)}",
            new GraphicsTextureDescription(data.Width, data.Height, data.Format,
                GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Linear, GraphicsTextureFilter.Linear,
                GraphicsTextureAddressMode.ClampToEdge),
            data.Pixels.Span);
        region = GpuCanvasAtlasRegion.Full;
        _textures.Add(source, texture);
        return true;
    }

    private bool TryResolveText(GpuCanvasCommand command, out IGraphicsTexture2D? texture,
        out GpuCanvasAtlasRegion region, out GpuCanvasRect destination)
    {
        texture = null;
        region = GpuCanvasAtlasRegion.Full;
        destination = command.Rect;
        if (_resourceResolver is not IGpuCanvasTextResolver textResolver ||
            !_device.Capabilities.Supports(GraphicsDeviceFeatures.SampledTextures) ||
            string.IsNullOrEmpty(command.Content)) return false;
        var height = Math.Max(1, (int)Math.Ceiling(command.Rect.Height));
        var family = string.IsNullOrWhiteSpace(command.FontFamily) ? "BEngine Built-in" : command.FontFamily;
        var measurementKey = $"{family}|{command.FontSize:0.###}|{command.Content}";
        if (!_textWidths.TryGetValue(measurementKey, out var naturalWidth))
        {
            if (!textResolver.TryMeasureText(command.Content, command.FontSize, family, out naturalWidth) ||
                naturalWidth <= 0)
                naturalWidth = EstimateTextWidth(command.Content, command.FontSize);
            _textWidths.Add(measurementKey, naturalWidth);
        }
        // Rasterization belongs to the text itself, not to the current control width. Keeping
        // the layout width out of the cache key prevents live window resizing from allocating
        // and uploading a new atlas region for the same label on every pixel.
        var width = Math.Min(naturalWidth, MaximumRasterizedTextWidth);
        var visibleWidth = Math.Min(Math.Max(0, command.Rect.Width), width);
        destination = command.Rect with { Width = visibleWidth };
        var key = $"text|{family}|{command.FontSize:0.###}|{width}x{height}|{command.Content}";
        if (TryGetAtlas(out var atlas) && atlas.TryGet(key, out region))
        {
            region = CropRegionWidth(region, visibleWidth, width);
            texture = atlas.Texture;
            _textTextureAccess[key] = _renderSequence;
            return true;
        }
        if (_textTextures.TryGetValue(key, out texture))
        {
            region = CropRegionWidth(GpuCanvasAtlasRegion.Full, visibleWidth, width);
            _textTextureAccess[key] = _renderSequence;
            return true;
        }
        if (!textResolver.TryResolveText(command.Content, width, height, command.FontSize, family, out var data))
            return false;
        data.Validate();
        if (atlas.TryGetOrAdd(key, data, out region))
        {
            region = CropRegionWidth(region, visibleWidth, width);
            texture = atlas.Texture;
            _textTextureAccess[key] = _renderSequence;
            return true;
        }
        texture = _device.CreateTexture2D("BEngine.GpuCanvas.Text",
            new GraphicsTextureDescription(data.Width, data.Height, data.Format,
                GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Linear, GraphicsTextureFilter.Linear,
                GraphicsTextureAddressMode.ClampToEdge), data.Pixels.Span);
        _textTextures.Add(key, texture);
        region = CropRegionWidth(GpuCanvasAtlasRegion.Full, visibleWidth, width);
        _textTextureAccess[key] = _renderSequence;
        return true;
    }

    private static GpuCanvasAtlasRegion CropRegionWidth(
        GpuCanvasAtlasRegion region, float visibleWidth, int rasterizedWidth)
    {
        if (visibleWidth >= rasterizedWidth || rasterizedWidth <= 0) return region;
        var ratio = Math.Clamp(visibleWidth / rasterizedWidth, 0, 1);
        return region with { U1 = region.U0 + (region.U1 - region.U0) * ratio };
    }

    private static GraphicsRect ToScissor(GpuCanvasRect clip, int viewportWidth, int viewportHeight)
    {
        var left = Math.Clamp((int)MathF.Floor(clip.X), 0, Math.Max(0, viewportWidth));
        var top = Math.Clamp((int)MathF.Floor(clip.Y), 0, Math.Max(0, viewportHeight));
        var right = Math.Clamp((int)MathF.Ceiling(clip.Right), left, Math.Max(left, viewportWidth));
        var bottom = Math.Clamp((int)MathF.Ceiling(clip.Bottom), top, Math.Max(top, viewportHeight));
        return new GraphicsRect(left, top, right - left, bottom - top);
    }

    private static int EstimateTextWidth(string text, float fontSize)
    {
        var em = Math.Max(8, fontSize);
        var width = 8f;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is 0x200C or 0x200D || value is >= 0xFE00 and <= 0xFE0F ||
                value is >= 0x0300 and <= 0x036F)
                continue;
            if (Rune.IsWhiteSpace(rune)) width += em * 0.5f;
            else if (value < 0x80)
                width += em * ("ilI.,'`:;!|".Contains((char)value) ? 0.35f :
                    "MW@#%&".Contains((char)value) ? 0.95f : 0.7f);
            else width += em * 1.1f;
        }
        return Math.Max(1, (int)Math.Ceiling(width / 4f) * 4);
    }

    private bool TryGetAtlas(out GpuCanvasAtlas atlas)
    {
        atlas = null!;
        if (!_device.Capabilities.Supports(GraphicsDeviceFeatures.SampledTextures)) return false;
        atlas = _atlas ??= new GpuCanvasAtlas(_device);
        return true;
    }

    private void TrimTextCache()
    {
        const int maximumEntries = 512;
        if (_textTextureAccess.Count <= maximumEntries) return;
        foreach (var key in _textTextureAccess.Where(item => item.Value != _renderSequence)
                     .OrderBy(item => item.Value).Take(_textTextureAccess.Count - maximumEntries)
                     .Select(item => item.Key).ToArray())
        {
            _textTextureAccess.Remove(key);
            if (_textTextures.Remove(key, out var texture)) texture.Dispose();
        }
    }

    private void EnsureTexturePipeline()
    {
        if (_textureProgram is not null && _textureMesh is not null) return;
        _device.Capabilities.Require(GraphicsDeviceFeatures.SampledTextures);
        var shader = ResolveTextureShaders(_device);
        _textureProgram = _device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.GpuCanvas.Texture", shader.Vertex, shader.Fragment));
        try
        {
            _textureMesh = _device.CreateMesh(new GraphicsMeshDescription(
                "BEngine.GpuCanvas.TextureDynamic", ReadOnlyMemory<float>.Empty, TextureLayout,
                usage: GraphicsBufferUsage.Dynamic));
        }
        catch
        {
            _textureProgram.Dispose();
            _textureProgram = null;
            throw;
        }
    }

    private static void BindProgram(IGraphicsProgram program, int width, int height)
    {
        program.Bind();
        program.SetFloat("uViewportWidth", Math.Max(1, width));
        program.SetFloat("uViewportHeight", Math.Max(1, height));
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveShaders(IGraphicsDevice device)
    {
        if (device.Backend == GraphicsBackend.Vulkan)
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Solid.vulkan.vert.glsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Solid.vulkan.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Solid.opengl.vert.glsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Solid.opengl.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Solid.direct3d.vert.hlsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Solid.direct3d.frag.hlsl")));
        var wgsl = DefaultShaderResources.Load("Shaders/IMGUI/Solid.webgpu.wgsl");
        return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, wgsl, "vs_main"),
            new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, wgsl, "fs_main"));
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveTextureShaders(
        IGraphicsDevice device)
    {
        if (device.Backend == GraphicsBackend.Vulkan)
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Textured.vulkan.vert.glsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Textured.vulkan.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Textured.opengl.vert.glsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Textured.opengl.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Textured.direct3d.vert.hlsl")),
                new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/IMGUI/Textured.direct3d.frag.hlsl")));
        var wgsl = DefaultShaderResources.Load("Shaders/IMGUI/Textured.webgpu.wgsl");
        return (new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, wgsl, "vs_main"),
            new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, wgsl, "fs_main"));
    }

}
