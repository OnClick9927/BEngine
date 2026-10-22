using BEngine.Editor;
using Silk.NET.Shaderc;
namespace BEngine.ProjectSystem.Editor;
internal sealed class ShadercCompiler : IShaderCompiler
{
    public byte[] Compile(string source, string sourceName, ShaderCompilationStage stage, string entryPoint = "main", bool warningsAsErrors = true, int optimizationLevel = 2) => CgShaderCompiler.Compile(source, sourceName, stage switch
    {
        ShaderCompilationStage.VertexShader => ShaderKind.VertexShader,
        ShaderCompilationStage.FragmentShader => ShaderKind.FragmentShader,
        ShaderCompilationStage.ComputeShader => ShaderKind.ComputeShader,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    }, entryPoint, warningsAsErrors, optimizationLevel);
}
