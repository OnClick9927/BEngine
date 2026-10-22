using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering.Rhi;
namespace BEngine.Editor;
public sealed class LightingShaderBuildSources : IPlayerShaderSourceProvider
{
    public IEnumerable<GraphicsShaderSource> CollectSources(IReadOnlyList<AssetRecord> assets,
        IReadOnlyList<RuntimePackageResourceInput> resources, GraphicsBackend backend)
    {
        if (!resources.Any(static r => r.Address.EndsWith("/Shaders/PortableScene/Lighting.vulkan.vert.glsl", StringComparison.OrdinalIgnoreCase))) yield break;
        string Load(string path) => File.ReadAllText(resources.Single(r => r.Address.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)).FilePath);
        foreach (var capacity in new[] { 32, 64, 128, 256, 512, 1024, 2048, 4095 })
        {
            var pair = BEngine.Rendering.LightingShaderSources.Create(backend, capacity, Load);
            yield return pair.Vertex; yield return pair.Fragment;
        }
    }
}
