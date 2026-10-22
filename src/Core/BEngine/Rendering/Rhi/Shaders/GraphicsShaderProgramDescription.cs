namespace BEngine.Rendering.Rhi;

public sealed record GraphicsShaderProgramDescription(
    string Label,
    GraphicsShaderSource VertexShader,
    GraphicsShaderSource FragmentShader)
{
    /// <summary>Consecutive sampled slots. Vulkan uses binding 1+2*slot for the texture and 2+2*slot for its sampler.</summary>
    public int SampledTextureCount { get; init; } = 1;
    public int UniformBufferSizeBytes { get; init; } = 16;
    public IReadOnlyList<GraphicsUniformBinding> Uniforms { get; init; } =
    [
        new("uViewportWidth", 0, 1),
        new("uViewportHeight", 4, 1)
    ];

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Label);
        if (VertexShader.Stage != GraphicsShaderStage.Vertex)
            throw new ArgumentException("VertexShader must contain a vertex stage.", nameof(VertexShader));
        if (FragmentShader.Stage != GraphicsShaderStage.Fragment)
            throw new ArgumentException("FragmentShader must contain a fragment stage.", nameof(FragmentShader));
        if (VertexShader.Language != FragmentShader.Language)
            throw new ArgumentException("Both shader stages must use the same source language.");
        ArgumentException.ThrowIfNullOrWhiteSpace(VertexShader.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(FragmentShader.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(VertexShader.EntryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(FragmentShader.EntryPoint);
        if (SampledTextureCount is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(SampledTextureCount), "A program supports one to eight sampled texture slots.");
        if (UniformBufferSizeBytes <= 0 || UniformBufferSizeBytes % 16 != 0)
            throw new ArgumentOutOfRangeException(nameof(UniformBufferSizeBytes));
        ArgumentNullException.ThrowIfNull(Uniforms);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var uniform in Uniforms)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uniform.Name);
            if (!names.Add(uniform.Name) || uniform.OffsetBytes < 0 || uniform.OffsetBytes % 4 != 0 ||
                uniform.ComponentCount is not (1 or 4 or 16) ||
                (long)uniform.OffsetBytes + uniform.ComponentCount * sizeof(float) > UniformBufferSizeBytes)
                throw new ArgumentException("Invalid uniform buffer binding.", nameof(Uniforms));
        }
    }
}

public readonly record struct GraphicsUniformBinding(string Name, int OffsetBytes, int ComponentCount);
