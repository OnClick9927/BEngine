using BEngine.ProjectSystem.Editor;
using BEngine.Rendering.Rhi;
namespace BEngine.Editor;
public sealed class LightingShaderBuildSources : IShaderSourceProvider
{
    public IEnumerable<ShaderBuildSource> CollectSources(IReadOnlyList<AssetRecord> assets,
        IReadOnlyList<ShaderBuildResourceInput> resources, GraphicsBackend backend)
    {
        bool __queryResult367;
        {
            var __querySource367 = resources;
            ArgumentNullException.ThrowIfNull(__querySource367, "source");
            __queryResult367 = false;
            var __querySource367Type = __querySource367.GetType();
            if (__querySource367Type == typeof(BEngine.Editor.ShaderBuildResourceInput[]) || __querySource367Type == typeof(System.Collections.Generic.List<BEngine.Editor.ShaderBuildResourceInput>))
            {
                System.ReadOnlySpan<BEngine.Editor.ShaderBuildResourceInput> __querySource367Span = __querySource367Type == typeof(BEngine.Editor.ShaderBuildResourceInput[]) ? (BEngine.Editor.ShaderBuildResourceInput[])(object)__querySource367 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<BEngine.Editor.ShaderBuildResourceInput>)(object)__querySource367);
                foreach (var __queryItem367 in __querySource367Span)
                {
                    if ((__queryItem367.Address.EndsWith("/Shaders/PortableScene/Lighting.vulkan.vert.glsl", StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult367 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem367 in __querySource367)
                {
                    if ((__queryItem367.Address.EndsWith("/Shaders/PortableScene/Lighting.vulkan.vert.glsl", StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult367 = true;
                        break;
                    }
                }
            }
        }
        
        if (!__queryResult367)
            yield break;
        string Load(string path)
        {
            return File.ReadAllText(Gather2174(resources).FilePath);
            [global::BEngine.SkipLoopGuard]
            global::BEngine.Editor.ShaderBuildResourceInput Gather2174(global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.ShaderBuildResourceInput> items2174)
            {
                ArgumentNullException.ThrowIfNull(items2174, "source");
                bool Evaluate2174(global::BEngine.Editor.ShaderBuildResourceInput r) => r.Address.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase);
                global::System.ReadOnlySpan<global::BEngine.Editor.ShaderBuildResourceInput> values;
                if (items2174.GetType() == typeof(global::BEngine.Editor.ShaderBuildResourceInput[]))
                    values = (global::BEngine.Editor.ShaderBuildResourceInput[])items2174;
                else if (items2174.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Editor.ShaderBuildResourceInput>))
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Editor.ShaderBuildResourceInput>)items2174);
                else
                    return global::System.Linq.Enumerable.Single(items2174, Evaluate2174);
                var found = false;
                global::BEngine.Editor.ShaderBuildResourceInput result = default!;
                foreach (var r in values)
                {
                    if (!Evaluate2174(r))
                        continue;
                    if (found)
                        return global::System.Linq.Enumerable.Single(new global::BEngine.Editor.ShaderBuildResourceInput[2], static _ => true);
                    result = r;
                    found = true;
                }

                if (found)
                    return result;
                return global::System.Linq.Enumerable.Single(global::System.Array.Empty<global::BEngine.Editor.ShaderBuildResourceInput>(), static _ => true);
            }
        }
        foreach (var capacity in new[] { 32, 64, 128, 256, 512, 1024, 2048, 4095 })
        {
            var pair = BEngine.Rendering.LightingShaderSources.Create(backend, capacity, Load);
            yield return ShaderBuildSourceAdapters.FromGraphics(pair.Vertex); yield return ShaderBuildSourceAdapters.FromGraphics(pair.Fragment);
        }
    }
}
