namespace BEngine.Rendering.Rhi;

public sealed record GraphicsBackendDescriptor(
    GraphicsBackend Backend,
    string DisplayName,
    bool HasBuiltInProvider,
    IReadOnlyList<GraphicsShaderLanguage> ShaderLanguages);
