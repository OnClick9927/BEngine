using System.Globalization;
using BEngine.Rendering.Rhi;
namespace BEngine.Rendering;
public static class LightingShaderSources
{
    public static GraphicsShaderPair Create(GraphicsBackend backend, int capacity, Func<string, string>? loader = null)
    {
        if (capacity is not (32 or 64 or 128 or 256 or 512 or 1024 or 2048 or 4095)) throw new ArgumentOutOfRangeException(nameof(capacity));
        var load = loader ?? DefaultShaderResources.Load;
        var platform = backend == GraphicsBackend.Vulkan ? "vulkan" : "opengl";
        var count = capacity.ToString(CultureInfo.InvariantCulture);
        var vertex = load($"Shaders/PortableScene/Lighting.{platform}.vert.glsl")
            .Replace("__LIGHTING_CAPACITY__", count, StringComparison.Ordinal);
        var fragment = load($"Shaders/PortableScene/Lighting.{platform}.frag.glsl")
            .Replace("__LIGHTING_CAPACITY__", count, StringComparison.Ordinal) + "\n" +
            load("Shaders/PortableScene/Lighting.evaluation.glslinc") + "\n" +
            load("Shaders/PortableScene/Lighting.common.glslinc");
        return new(new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl, vertex),
            new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, fragment));
    }
}
