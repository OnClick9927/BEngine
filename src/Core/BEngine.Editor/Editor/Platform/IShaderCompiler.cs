namespace BEngine.Editor;
public interface IShaderCompiler
{
    byte[] Compile(string source, string sourceName, ShaderCompilationStage stage, string entryPoint = "main", bool warningsAsErrors = true, int optimizationLevel = 2);
}
