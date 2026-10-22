using BEngine.Rendering;

namespace BEngine.UIElements;

internal static class UIElementsRuntimeServices
{
    internal static IUIDocumentSourceLoader DocumentSourceLoader =>
        RuntimeServiceAccess.GetService<IUIDocumentSourceLoader>() ??
        FileUIDocumentSourceLoader.Shared;

    internal static IUIRenderResourceResolver RenderResourceResolver =>
        RuntimeServiceAccess.GetService<IUIRenderResourceResolver>() ??
        FileUIRenderResourceResolver.Shared;

    internal static ITextRasterizer? TextResourceResolver =>
        RuntimeServiceAccess.GetService<ITextRasterizer>();
}
