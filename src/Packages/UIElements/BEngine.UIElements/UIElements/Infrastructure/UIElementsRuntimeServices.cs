using BEngine.Rendering;

namespace BEngine.UIElements;

internal static class UIElementsRuntimeServices
{
    internal static IUIDocumentSourceLoader DocumentSourceLoader =>
        RuntimeServices.GetService<IUIDocumentSourceLoader>() ??
        FileUIDocumentSourceLoader.Shared;

    internal static IUIRenderResourceResolver RenderResourceResolver =>
        RuntimeServices.GetService<IUIRenderResourceResolver>() ??
        FileUIRenderResourceResolver.Shared;

    internal static ITextRasterizer? TextResourceResolver =>
        RuntimeServices.GetService<ITextRasterizer>();
}
