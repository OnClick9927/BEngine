using BEngine.ProjectSystem.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>Builds the UI renderer's private atlas channel variant alongside the public alpha shader.</summary>
public sealed class SdfAtlasShaderBuildSources : IShaderSourceProvider
{
    public IEnumerable<ShaderBuildSource> CollectSources(IReadOnlyList<AssetRecord> assets,
        IReadOnlyList<ShaderBuildResourceInput> resources, GraphicsBackend backend)
    {
        if (backend != GraphicsBackend.Vulkan) yield break;
        foreach (var path in resources.Where(static resource => IsSdfFragment(resource.Address))
                     .Select(static resource => resource.FilePath)
                     .Concat(assets.Where(static asset => IsSdfFragment(asset.SourcePath))
                         .Select(static asset => asset.SourcePath)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var original = new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, File.ReadAllText(path));
            // Use the exact runtime transformation so artifact hashes match
            // when runtime shader compilation is disabled in the published application.
            var atlas = UIElementsRenderer.ConfigureSdfAtlasFragment(original);
            if (atlas.Code != original.Code) yield return ShaderBuildSourceAdapters.FromGraphics(atlas);
        }
    }

    private static bool IsSdfFragment(string path) =>
        path.Replace('\\', '/').EndsWith("/Shaders/UIElements/Sdf.vulkan.frag.glsl", StringComparison.OrdinalIgnoreCase);
}
