namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IShaderCompiler through dependency injection.</summary>
public sealed class ShaderCompilerUtility
{
    private readonly IShaderCompiler _implementation;

    public ShaderCompilerUtility(IShaderCompiler implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static ShaderCompilerUtility Current => EditorServices.GetRequiredService<ShaderCompilerUtility>();

    public byte[] Compile(string source, string sourceName, ShaderCompilationStage stage, string entryPoint = "main", bool warningsAsErrors = true, int optimizationLevel = 2) =>
        _implementation.Compile(source, sourceName, stage, entryPoint, warningsAsErrors, optimizationLevel);
}
