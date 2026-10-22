using BEngine.Rendering.Rhi;
using BEngine.UIElements;

namespace BEngine.Rendering;

public sealed partial class UIElementsRenderer
{
    private bool TryAddAdvancedText(List<RenderBatch> batches, UIRenderCommand command)
    {
        var font = Style.ResolveFont(command.Element);
        if (font is not { supportsAdvancedText: true } ||
            !_device.Capabilities.Supports(GraphicsDeviceFeatures.SampledTextures)) return false;
        var width = Math.Clamp((int)Math.Ceiling((double)command.Rect.Width), 1, 8192);
        var height = Math.Clamp((int)Math.Ceiling((double)command.Rect.Height), 1, 8192);
        var size = command.FontSize > 0 ? command.FontSize : 14;
        var wrap = command.Element is Label;
        var direction = Style.ResolveTextDirection(command.Element);
        var key = new AdvancedTextKey(font, width, height, size, wrap, direction, command.Color, command.Content);
        if (!_advancedTextures.TryGetValue(key, out var texture))
        {
            var image = font.AdvancedRuntime.Render(command.Content, width, height, size, wrap, direction, command.Color);
            texture = _device.CreateTexture2D("BEngine.UIElements.AdvancedText",
                new GraphicsTextureDescription(image.Width, image.Height, GraphicsTextureFormat.Rgba8Unorm,
                    GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Linear, GraphicsTextureFilter.Linear,
                    GraphicsTextureAddressMode.ClampToEdge), image.Pixels);
            _advancedTextures.Add(key, texture, (long)texture.Description.Width * texture.Description.Height * 4);
        }
        var batch = GetBatch(batches, command.ClipRect, texture, command.Rect);
        AddTexturedQuad(batch.Vertices, command.Rect, new UIColor(255, 255, 255));
        return true;
    }
}
