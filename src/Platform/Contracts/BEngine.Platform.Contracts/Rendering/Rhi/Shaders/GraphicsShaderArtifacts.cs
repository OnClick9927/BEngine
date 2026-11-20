
namespace BEngine.Rendering.Rhi;

/// <summary>Content-addressed, backend-specific shader artifacts supplied by any resource provider.</summary>
public static class GraphicsShaderArtifacts
{
    public static string GetResourcePath(GraphicsShaderSource source, GraphicsBackend backend) =>
        BEngine.Build.BuildArtifactKey.Shader(backend, (int)source.Stage, (int)source.Language, source.Code, source.EntryPoint);

    public static byte[]? Load(GraphicsShaderSource source, GraphicsBackend backend) =>
        Resources.Load<byte[]>(GetResourcePath(source, backend));
}
