using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.Rendering.Rhi;
using Veldrid.SPIRV;
using ShaderStages = Veldrid.ShaderStages;

namespace BEngine.Platform;

/// <summary>The native SPIR-V tool adapter used by editor build workflows.</summary>
internal static class SpirvShaderBuildCompiler
{
    [ModuleInitializer]
    internal static void Initialize() => ShaderBuildPipeline.RegisterCompiler(GraphicsBackend.Vulkan, Compile);

    private static byte[] Compile(ShaderBuildSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Language != ShaderBuildLanguage.Glsl || source.EntryPoint != "main" ||
            source.Code.Length > 1024 * 1024 || source.Stage is not (ShaderBuildStage.Vertex or ShaderBuildStage.Fragment))
            throw new InvalidDataException("Offline Vulkan compilation requires GLSL vertex/fragment shaders, entry 'main', at most 1 MiB.");
        var bytes = SpirvCompilation.CompileGlslToSpirv(source.Code, "bengine.glsl",
            source.Stage == ShaderBuildStage.Vertex ? ShaderStages.Vertex : ShaderStages.Fragment,
            GlslCompileOptions.Default).SpirvBytes;
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }
}
