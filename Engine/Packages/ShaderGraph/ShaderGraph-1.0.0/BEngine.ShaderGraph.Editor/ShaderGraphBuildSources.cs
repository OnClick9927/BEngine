using BEngine.Editor;
using BEngine.ProjectSystem.Editor;
using BEngine.ProjectSystem;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;

namespace BEngine.ShaderGraph.Editor;

public sealed class ShaderGraphBuildSources : IPlayerShaderSourceProvider
{
    public IEnumerable<GraphicsShaderSource> CollectSources(IReadOnlyList<AssetRecord> assets, IReadOnlyList<RuntimePackageResourceInput> resources, GraphicsBackend backend)
    {
        foreach (var asset in assets.Where(static a => a.SourcePath.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase)))
        {
            var yaml = File.ReadAllText(asset.ArtifactPath);
            if (ManagedAssetSerialization.GetAssetType(yaml) != typeof(ShaderGraphAsset)) continue;
            var graph = ManagedAssetSerialization.Deserialize<ShaderGraphAsset>(yaml);
            try
            {
                var evaluation = resources.FirstOrDefault(static r => r.Address.EndsWith("/Shaders/PortableScene/Lighting.evaluation.glslinc", StringComparison.OrdinalIgnoreCase));
                var functions = graph.surface == ShaderGraphSurface.Lit2D ? File.ReadAllText(evaluation?.FilePath ??
                    throw new InvalidDataException("Lit graph requires the Lighting2D evaluation resource.")) : null;
                var unlit = ShaderGraphCompiler.CompileSurface(graph, backend, null, 0, 0);
                yield return unlit.Program.VertexShader;
                yield return unlit.Program.FragmentShader;
                if (graph.surface != ShaderGraphSurface.Lit2D) continue;
                var parameterCapacity = Math.Max(1, unlit.Parameters.Count);
                foreach (var capacity in new[] { 32, 64, 128, 256, 512, 1024, 2048, 4079 })
                {
                    if (16 + (parameterCapacity + capacity) * 16 > 65536) continue;
                    for (var cookies = 0; cookies <= Math.Min(6, 7 - unlit.Textures.Count); cookies++)
                    {
                        var program = ShaderGraphCompiler.CompileSurface(graph, backend, null, capacity, cookies, functions).Program;
                        yield return program.VertexShader;
                        yield return program.FragmentShader;
                    }
                }
            }
            finally { BObject.DestroyImmediate(graph); }
        }
    }
}
