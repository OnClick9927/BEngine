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
        var webClipOriginal = sources;
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
        CaptureWebClipSources(webClipOriginal, sources);
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
        // The atlas is resolved/touched above on every command, including a
        // geometry hit. A trimmed or invalidated texture is never retained by
        // the CPU cache, and an unresolved/destroyed font is never replayed.
        var style = command.Element.style;
        var effectiveSize = style.textOutlineWidth > 0 && style.textOutlineColor is { A: > 0 }
            ? UIElementLayoutEngine.EffectiveFontSize(command.Element) : Fix64.Zero;
        var geometryKey = new SdfGeometryKey(command, font, BAsset.cacheGeneration,
            style.textOutlineWidth, style.textOutlineColor, effectiveSize);
        SdfCommandGeometry? geometry = null;
        var outlined = style.textOutlineWidth > 0 && style.textOutlineColor is { A: > 0 };
        if (EnableSdfGeometryCache && !font.isDestroyed &&
            command.Content.Length <= MaximumSdfCommandVertexFloats / (outlined ? 96 : 48))
        {
            if (_sdfCommandGeometry.TryGetValue(command.Element, out geometry))
            {
                if (geometry.Valid && geometry.Key == geometryKey)
                {
                    ReplaySdfGeometry(batches, command, texture, geometry);
                    _textGeometryCacheHits++;
                    return true;
                }
            }
            else
            {
                geometry = new SdfCommandGeometry();
                _sdfCommandGeometry.Add(command.Element, geometry);
                TrimSdfGeometry();
            }
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
        var measured = ScreenSpaceGlyphGeometry.MeasureBounds(layout.Glyphs, (float)command.Rect.X, top);
        _textGeometryRebuiltGlyphs += layout.Glyphs.Length;
        if (layout.Glyphs.Length == 0) return true;
        var bounds = new UIElementRect((Fix64)measured.Left, (Fix64)measured.Top,
            (Fix64)(measured.Right - measured.Left), (Fix64)(measured.Bottom - measured.Top));
        float? outlineThreshold = null;
        if (style.textOutlineWidth > 0 && style.textOutlineColor is { A: > 0 } outline)
        {
            var scale = size / (float)effectiveSize;
            var threshold = Math.Max(.02f, .5f - (float)style.textOutlineWidth * scale / (font.distanceRange * 2 * size / font.samplingSize));
            outlineThreshold = threshold;
            if (geometry is null)
                AddGlyphs(GetBatch(batches, command.ClipRect, texture, bounds, threshold).Vertices,
                    layout, command, top, outline);
        }
        if (geometry is not null)
        {
            PrepareSdfGeometry(geometry, geometryKey, layout, command, top, bounds, outlineThreshold);
            ReplaySdfGeometry(batches, command, texture, geometry);
            return true;
        }
        AddGlyphs(GetBatch(batches, command.ClipRect, texture, bounds, .5f).Vertices,
            layout, command, top, command.Color);
        return true;
    }

    private void AddGlyphs(List<float> output, SdfTextLayout layout,
        UIRenderCommand command, float top, UIColor color)
    {
        using var budget = LoopExecutionGuard.AllowIterations(layout.Glyphs.Length + 1);
        var rgba = new NVector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        ScreenSpaceGlyphGeometry.Append(output, layout.Glyphs, (float)command.Rect.X, top, rgba);
    }
}
