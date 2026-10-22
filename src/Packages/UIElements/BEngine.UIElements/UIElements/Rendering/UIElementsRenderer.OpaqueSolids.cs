using BEngine.UIElements;

namespace BEngine.Rendering;

public sealed partial class UIElementsRenderer
{
    // Text, images (including missing-image geometry), SDF glyphs and translucent
    // rectangles keep their existing blending. No commands change paint order.
    internal bool EnableOpaqueSolidBlending { get; set; } = true;

    private bool CanReplaceSolid(UIRenderCommand command) =>
        EnableOpaqueSolidBlending && command.Type == UIRenderCommandType.SolidRect && command.Color.A == 255;
}
