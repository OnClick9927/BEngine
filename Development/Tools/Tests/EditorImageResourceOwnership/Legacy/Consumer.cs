using BEngine.Editor.Rendering;
using BEngine.Rendering.Rhi;

public static class LegacyEditorImageConsumer
{
    // Compile this file against the pre-migration IMGUI binary; do not recompile it for the new owner.
    public static bool Run(string source)
    {
        IGpuCanvasResourceResolver backend = new Source();
        var facade = new CanvasResourceUtility(backend);
        return facade.TryResolveTexture("legacy", out var texture) && texture.Width == 2 &&
            FileGpuCanvasResourceResolver.Shared.TryResolveTexture(source, out var file) && file.Width == 1;
    }
    private sealed class Source : IGpuCanvasResourceResolver
    {
        public bool TryResolveTexture(string source, out EditorTextureData texture)
        { texture = new(2, 1, GraphicsTextureFormat.R8Unorm, new byte[2]); return true; }
    }
}
