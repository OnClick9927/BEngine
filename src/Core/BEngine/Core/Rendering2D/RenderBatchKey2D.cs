namespace BEngine;

public readonly record struct RenderBatchKey2D(Guid Material, string Shader, string Atlas)
{
    public RenderBatchKey2D(Material material, string atlas)
        : this(material?.Id ?? throw new ArgumentNullException(nameof(material)),
            material.shader.shaderName, atlas?.Trim() ?? string.Empty)
    {
    }
}
