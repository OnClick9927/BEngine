using BEngine.Rendering.Rhi;
namespace BEngine.Editor;

public enum ShaderBuildStage { Vertex, Fragment }
public enum ShaderBuildLanguage { Glsl, Hlsl, SpirV, Wgsl, Msl }

/// <summary>Compiler input independent of any graphics device, renderer or source file convention.</summary>
public readonly record struct ShaderBuildSource(
    ShaderBuildStage Stage, ShaderBuildLanguage Language, string Code, string EntryPoint = "main")
{
    public string GetResourcePath(GraphicsBackend backend) =>
        BEngine.Build.BuildArtifactKey.Shader(backend, (int)Stage, (int)Language, Code, EntryPoint);
}

public interface IShaderBuildSourceReader
{
    bool TryReadSource(string path, GraphicsBackend backend, out ShaderBuildSource source);
}
