using BEngine.UIElements;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public sealed partial class UIElementsRenderer
{
    private readonly BoundedLruCache<(SdfFontAsset Font, string Text, float Size, Fix64 Width, bool Wrap), SdfTextLayout> _sdfLayouts = new();
    private const string SdfDistanceChannel = ".a/*BEngineSdfDistance*/";
    internal static GraphicsShaderSource ConfigureSdfAtlasFragment(GraphicsShaderSource source) =>
        source with { Code = source.Code.Replace(SdfDistanceChannel, ".r", StringComparison.Ordinal) };
    private bool _sdfUsesRedDistance, _sdfUsesR8Atlas;
    private IGraphicsProgram EnsureSdfPipeline()
    {
        if (_sdfShader is not null) return _sdfShader;
        var sources = GraphicsUtility.Current.ResolveShaderVariant(_device.Capabilities, "Shaders/UIElements/Sdf");
        // Keep the public resource's alpha-channel convention. Only this
        // renderer's atlas/program pair opts into the single-channel format;
        // custom shader overrides without the marker retain their old input.
        var redDistance = sources.Fragment.Code.Contains(SdfDistanceChannel, StringComparison.Ordinal);
        if (redDistance)
            sources = sources with { Fragment = ConfigureSdfAtlasFragment(sources.Fragment) };
        var r8Atlas = false;
        if (redDistance && _device is IGraphicsTextureFormatSupport formats)
        {
            try { r8Atlas = formats.SupportsTextureFormat(GraphicsTextureFormat.R8Unorm); }
            catch (NotSupportedException) { } // Optional format-query implementations may decline the query.
        }
        var program = _device.CreateProgram(new GraphicsShaderProgramDescription("BEngine.UIElements.Sdf", sources.Vertex, sources.Fragment)
        { Uniforms = [new("uViewportWidth", 0, 1), new("uViewportHeight", 4, 1), new("uSdfThreshold", 8, 1)] });
        _sdfUsesRedDistance = redDistance;
        _sdfUsesR8Atlas = r8Atlas;
        return _sdfShader = program;
    }
    private bool TryAddSdfText(List<RenderBatch> batches, UIRenderCommand command)
    {
        var font = Style.ResolveFont(command.Element);
        if (font is null || font.supportsAdvancedText || !_device.Capabilities.Supports(GraphicsDeviceFeatures.SampledTextures)) return false;
        var key = font.textureKey;
        if (!_textures.TryGetValue(key, out var texture))
        {
            EnsureSdfPipeline();
            var distance = font.distancePixels.Span;
            ReadOnlySpan<byte> pixels = distance;
            var format = GraphicsTextureFormat.R8Unorm;
            if (!_sdfUsesR8Atlas)
            {
                var rgba = new byte[checked(distance.Length * 4)];
                using var budget = LoopExecutionGuard.AllowIterations(distance.Length);
                for (var i = 0; i < distance.Length; i++)
                {
                    var rgb = _sdfUsesRedDistance ? distance[i] : (byte)255;
                    rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = rgb;
                    rgba[i * 4 + 3] = distance[i];
                }
                pixels = rgba;
                format = GraphicsTextureFormat.Rgba8Unorm;
            }
            texture = _device.CreateTexture2D("BEngine.UIElements.SdfAtlas", new GraphicsTextureDescription(font.atlasWidth,
                font.atlasHeight, format, GraphicsTextureUsage.Sampled,
                GraphicsTextureFilter.Linear, GraphicsTextureFilter.Linear, GraphicsTextureAddressMode.ClampToEdge), pixels);
            _textures.Add(key, texture, texture.Description.DataByteSize);
        }
        var size = command.FontSize > 0 ? command.FontSize : 14;
        var layoutKey = (font, command.Content, size, command.Rect.Width, command.Element is Label);
        if (!_sdfLayouts.TryGetValue(layoutKey, out var layout))
        {
            layout = SdfTextLayout.Build(font, command.Content, size, (float)command.Rect.Width, command.Element is Label);
            _sdfLayouts.Add(layoutKey, layout, (long)command.Content.Length * 2 + layout.Glyphs.Length * 64L);
            _sdfLayouts.Trim(1024, 4 * 1024 * 1024);
        }
        var top = (float)command.Rect.Y + Math.Max(0, ((float)command.Rect.Height - layout.Height) / 2);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var placement in layout.Glyphs)
        {
            var x = (float)command.Rect.X + placement.X; var y = top + placement.Y;
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x + placement.Width); y1 = Math.Max(y1, y + placement.Height);
        }
        if (layout.Glyphs.Length == 0) return true;
        var bounds = new UIElementRect((Fix64)x0, (Fix64)y0, (Fix64)(x1 - x0), (Fix64)(y1 - y0));
        var style = command.Element.style;
        if (style.textOutlineWidth > 0 && style.textOutlineColor is { A: > 0 } outline)
        {
            var scale = size / UIElementLayoutEngine.EffectiveFontSize(command.Element);
            var threshold = Math.Max(.02f, .5f - style.textOutlineWidth * scale / (font.distanceRange * 2 * size / font.samplingSize));
            AddGlyphs(GetBatch(batches, command.ClipRect, texture, bounds, threshold).Vertices,
                layout, command, top, font, outline);
        }
        AddGlyphs(GetBatch(batches, command.ClipRect, texture, bounds, .5f).Vertices,
            layout, command, top, font, command.Color);
        return true;
    }

    private static void AddGlyphs(List<float> output, SdfTextLayout layout,
        UIRenderCommand command, float top, SdfFontAsset font, UIColor color)
    {
        using var budget = LoopExecutionGuard.AllowIterations(layout.Glyphs.Length + 1);
        var rgba = new NVector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        foreach (var placement in layout.Glyphs)
        {
            var glyph = placement.Glyph;
            var x0 = (float)command.Rect.X + placement.X; var y0 = top + placement.Y;
            var x1 = x0 + placement.Width; var y1 = y0 + placement.Height;
            var u0 = glyph.X / (float)font.atlasWidth; var v0 = glyph.Y / (float)font.atlasHeight;
            var u1 = (glyph.X + glyph.Width) / (float)font.atlasWidth; var v1 = (glyph.Y + glyph.Height) / (float)font.atlasHeight;
            AddTexturedVertex(output, x0, y0, rgba, u0, v0); AddTexturedVertex(output, x1, y0, rgba, u1, v0);
            AddTexturedVertex(output, x1, y1, rgba, u1, v1); AddTexturedVertex(output, x0, y0, rgba, u0, v0);
            AddTexturedVertex(output, x1, y1, rgba, u1, v1); AddTexturedVertex(output, x0, y1, rgba, u0, v1);
        }
    }
}
