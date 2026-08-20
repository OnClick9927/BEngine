namespace BEngine.Rendering.Rhi;

public sealed record GraphicsShaderProgramDescription(
    string Label,
    GraphicsShaderSource VertexShader,
    GraphicsShaderSource FragmentShader)
{
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
    }
}
