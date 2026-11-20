using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BEngine.DependencyInjection;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public static class ShaderBuildSourceAdapters
{
    public static ShaderBuildSource FromGraphics(GraphicsShaderSource source) =>
        new((ShaderBuildStage)source.Stage, (ShaderBuildLanguage)source.Language, source.Code, source.EntryPoint);
}

internal sealed class GlslBuildSourceReader : IShaderBuildSourceReader
{
    [ModuleInitializer]
    internal static void Register() => RuntimeServiceContributions.RegisterDefault<IShaderBuildSourceReader>(new GlslBuildSourceReader());

    public bool TryReadSource(string path, GraphicsBackend backend, out ShaderBuildSource source)
    {
        source = default;
        var suffix = backend switch
        {
            GraphicsBackend.Vulkan => ".vulkan", GraphicsBackend.OpenGL => ".opengl",
            GraphicsBackend.OpenGLES => ".opengles", GraphicsBackend.WebGL => ".webgl", _ => null
        };
        if (suffix is null || (!path.EndsWith(suffix + ".vert.glsl", StringComparison.OrdinalIgnoreCase) &&
                               !path.EndsWith(suffix + ".frag.glsl", StringComparison.OrdinalIgnoreCase))) return false;
        source = new(path.EndsWith(".vert.glsl", StringComparison.OrdinalIgnoreCase) ? ShaderBuildStage.Vertex : ShaderBuildStage.Fragment,
            ShaderBuildLanguage.Glsl, File.ReadAllText(path));
        return !Regex.IsMatch(source.Code, "__[A-Z_]+__");
    }
}
