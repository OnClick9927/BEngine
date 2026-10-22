using System.Globalization;
using BEngine.Rendering.Rhi;
namespace BEngine;
public static class SpriteSkinShaderSources
{
    public static GraphicsShaderPair Create(GraphicsBackend backend, int capacity, Func<string, string>? loader = null)
    {
        if (capacity < 1 || (capacity & (capacity - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (capacity > 1024) throw new NotSupportedException("GPU skinning exceeds the 64 KiB palette budget; use CPU skinning.");
        var load = loader ?? DefaultShaderResources.Load;
        var count = (capacity * 2).ToString(CultureInfo.InvariantCulture);
        var vulkan = backend is GraphicsBackend.Vulkan or GraphicsBackend.Metal;
        var declarations = vulkan
            ? "layout(set=0,binding=0) uniform SkinPalette { vec4 uReserved; vec4 uBones[" + count + "]; };"
            : "uniform vec4 uBones[" + count + "];";
        var embedded = backend is GraphicsBackend.OpenGLES or GraphicsBackend.WebGL;
        var vertex = (vulkan ? "#version 450\n" : embedded ? "#version 300 es\nprecision highp float;\nprecision highp int;\n" : "#version 330 core\n") + declarations + "\n" + """
            layout(location=0) in vec2 aPosition;
            layout(location=1) in vec4 aColor;
            layout(location=2) in vec2 aTexCoord;
            layout(location=3) in vec4 aBoneIndices;
            layout(location=4) in vec4 aBoneWeights;
            layout(location=0) out vec4 vColor;
            layout(location=1) out vec2 vTexCoord;
            void main() {
                vec3 bindPoint = vec3(aPosition, 1.0);
                vec2 position = vec2(0.0);
                for (int slot=0; slot<4; ++slot) {
                    int bone = int(aBoneIndices[slot]) * 2;
                    position += vec2(dot(uBones[bone].xyz, bindPoint), dot(uBones[bone+1].xyz, bindPoint)) * aBoneWeights[slot];
                }
                gl_Position = vec4(position, 0.0, 1.0);
                vColor = aColor; vTexCoord = aTexCoord;
            }
            """;
        if (!vulkan) vertex = vertex.Replace("layout(location=0) out", "out", StringComparison.Ordinal)
            .Replace("layout(location=1) out", "out", StringComparison.Ordinal);
        var fragment = load("Shaders/SpriteSkinning2D/Textured." + (vulkan ? "vulkan" : "opengl") + ".frag.glsl");
        if (embedded) fragment = fragment.Replace("#version 330 core", "#version 300 es\nprecision highp float;\nprecision highp int;", StringComparison.Ordinal);
        return new(new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl, vertex),
            new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, fragment));
    }
}
