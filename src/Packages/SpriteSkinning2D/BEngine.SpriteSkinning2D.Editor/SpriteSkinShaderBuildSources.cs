using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering.Rhi;
namespace BEngine.Editor;
public sealed class SpriteSkinShaderBuildSources : IPlayerShaderSourceProvider
{
    public IEnumerable<GraphicsShaderSource> CollectSources(IReadOnlyList<AssetRecord> assets,
        IReadOnlyList<RuntimePackageResourceInput> resources, GraphicsBackend backend)
    {
        if (!resources.Any(static r => r.Address.EndsWith("/Shaders/SpriteSkinning2D/Textured.vulkan.frag.glsl", StringComparison.OrdinalIgnoreCase))) yield break;
        string Load(string path) => File.ReadAllText(resources.Single(r => r.Address.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)).FilePath);
        foreach (var capacity in new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 })
        {
            var pair = BEngine.SpriteSkinShaderSources.Create(backend, capacity, Load);
            yield return pair.Vertex; yield return pair.Fragment;
        }
    }
}
